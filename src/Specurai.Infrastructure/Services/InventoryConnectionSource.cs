using System.Globalization;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Specurai.Infrastructure.Services;

public class InventoryConnectionSource : IExternalConnectionSource
{
    private readonly IExternalSourceSettings _settings;

    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public InventoryConnectionSource(IExternalSourceSettings settings)
    {
        _settings = settings;
    }

    public async Task<ExternalConnectionResult> SyncAsync()
    {
        var config = _settings.Load();
        if (string.IsNullOrWhiteSpace(config.SourceDirectory))
            return new ExternalConnectionResult([], []);

        var inventoryDir = Path.Combine(
            config.SourceDirectory, "ansible", "customer", "inventory");
        var hostsFile = Path.Combine(inventoryDir, "hosts.yml");

        if (!File.Exists(hostsFile))
            return new ExternalConnectionResult([], []);

        var groupVarsDir = Path.Combine(inventoryDir, "group_vars");
        var customers = ParseCustomers(hostsFile);
        var profiles = new List<ConnectionProfile>();
        var failedItems = new List<string>();

        foreach (var customer in customers)
        {
            foreach (var env in customer.Environments)
            {
                var profile = await BuildProfileAsync(
                    customer, env, groupVarsDir, config.KeyFilePath);
                if (profile != null)
                    profiles.Add(profile);
                else
                    failedItems.Add($"{customer.CustomerName}/{env}");
            }
        }

        return new ExternalConnectionResult(profiles, failedItems);
    }

    private List<CustomerInfo> ParseCustomers(string hostsFile)
    {
        var content = File.ReadAllText(hostsFile);
        var root = YamlDeserializer.Deserialize<Dictionary<string, object>>(content);
        var customers = new List<CustomerInfo>();

        if (root.TryGetValue("all", out var allObj) &&
            allObj is Dictionary<object, object> all &&
            all.TryGetValue("children", out var childrenObj) &&
            childrenObj is Dictionary<object, object> topChildren)
        {
            var children = FindCustomerChildren(topChildren);
            foreach (var (groupKey, groupVal) in children)
            {
                var groupName = groupKey.ToString()!;
                if (!groupName.StartsWith("customer_")) continue;

                var suffix = groupName["customer_".Length..];
                if (suffix.Contains('_')) continue;

                if (groupVal is not Dictionary<object, object> groupDict) continue;

                var vars = groupDict.TryGetValue("vars", out var v)
                    ? v as Dictionary<object, object> : null;

                var mssqlHost = vars?.GetValueOrDefault("mssql_host")?.ToString() ?? string.Empty;
                var tailscaleIp = vars?.GetValueOrDefault("tailscale_ip")?.ToString() ?? string.Empty;
                var customerName = vars?.GetValueOrDefault("customer")?.ToString() ?? suffix;

                var environments = new List<string>();
                if (groupDict.TryGetValue("hosts", out var hostsObj) &&
                    hostsObj is Dictionary<object, object> hosts)
                {
                    foreach (var (_, hostVal) in hosts)
                    {
                        if (hostVal is Dictionary<object, object> hostDict &&
                            hostDict.TryGetValue("env", out var envObj))
                            environments.Add(envObj.ToString()!);
                    }
                }

                if (environments.Count == 0) continue;

                customers.Add(new CustomerInfo
                {
                    CustomerName = customerName,
                    MssqlHost = mssqlHost,
                    TailscaleIp = tailscaleIp,
                    Environments = environments.Distinct().ToList()
                });
            }
        }

        return customers;
    }

    private static Dictionary<object, object> FindCustomerChildren(
        Dictionary<object, object> topChildren)
    {
        if (topChildren.Keys.Any(k => k.ToString()!.StartsWith("customer_")))
            return topChildren;

        foreach (var (_, val) in topChildren)
        {
            if (val is Dictionary<object, object> groupDict &&
                groupDict.TryGetValue("children", out var subChildrenObj) &&
                subChildrenObj is Dictionary<object, object> subChildren &&
                subChildren.Keys.Any(k => k.ToString()!.StartsWith("customer_")))
                return subChildren;
        }

        return topChildren;
    }

    private async Task<ConnectionProfile?> BuildProfileAsync(
        CustomerInfo customer, string env, string groupVarsDir, string keyFilePath)
    {
        var envGroup = $"customer_{customer.CustomerName}_{env}";
        var dbYmlPath = Path.Combine(groupVarsDir, envGroup, "database.yml");

        if (!File.Exists(dbYmlPath)) return null;

        string? database = null;
        try
        {
            var dbYml = YamlDeserializer.Deserialize<Dictionary<string, object>>(
                await File.ReadAllTextAsync(dbYmlPath));
            database = ExtractMainDatabase(dbYml);
        }
        catch { return null; }

        if (string.IsNullOrEmpty(database)) return null;

        var vaultVars = await MergeVaultVarsAsync(
            customer.CustomerName, env, groupVarsDir, keyFilePath);

        var isContainer = customer.MssqlHost.Equals(
            "container", StringComparison.OrdinalIgnoreCase);
        var server = isContainer ? customer.TailscaleIp : customer.MssqlHost;

        if (string.IsNullOrEmpty(server)) return null;

        var username = isContainer ? "SA" : "mis";
        var password = isContainer
            ? GetVaultVar(vaultVars, string.Empty, "vault_db_container_password")
            : GetVaultVar(vaultVars, string.Empty,
                "vault_db_main_password", "vault_db_admin_password", "vault_db_password");

        var envLabel = env == "production" ? "正式" : "測試";
        var displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(customer.CustomerName);

        return new ConnectionProfile
        {
            Name = $"{displayName} - {envLabel}",
            Server = server,
            Database = database,
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = username,
            Password = password
        };
    }

    private static string? ExtractMainDatabase(Dictionary<string, object> dbYml)
    {
        if (dbYml.TryGetValue("main_sql_override", out var overrideObj) &&
            overrideObj is Dictionary<object, object> overrideDict &&
            overrideDict.TryGetValue("database", out var db))
            return db?.ToString();
        return null;
    }

    private async Task<Dictionary<string, string>> MergeVaultVarsAsync(
        string customerName, string env, string groupVarsDir, string keyFilePath)
    {
        string? password = null;
        if (File.Exists(keyFilePath))
            password = (await File.ReadAllTextAsync(keyFilePath)).Trim();

        var merged = new Dictionary<string, string>();

        foreach (var group in new[]
        {
            $"customer_{customerName}",
            $"customer_{customerName}_{env}"
        })
        {
            var vaultFile = Path.Combine(groupVarsDir, group, "vault.yml");
            if (!File.Exists(vaultFile)) continue;

            try
            {
                var rawContent = await File.ReadAllTextAsync(vaultFile);
                string yamlContent;

                if (rawContent.TrimStart().StartsWith("$ANSIBLE_VAULT"))
                {
                    if (password == null) continue;
                    yamlContent = VaultDecryptor.Decrypt(rawContent, password);
                }
                else
                {
                    yamlContent = rawContent;
                }

                var vars = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                foreach (var (k, v) in vars)
                    merged[k] = v?.ToString() ?? string.Empty;
            }
            catch { /* 單一 vault 解密失敗不中斷整體流程 */ }
        }

        return merged;
    }

    private static string GetVaultVar(
        Dictionary<string, string> vars, string defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (vars.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
                return val;
        }
        return defaultValue;
    }

    private class CustomerInfo
    {
        public string CustomerName { get; set; } = string.Empty;
        public string MssqlHost { get; set; } = string.Empty;
        public string TailscaleIp { get; set; } = string.Empty;
        public List<string> Environments { get; set; } = [];
    }
}
