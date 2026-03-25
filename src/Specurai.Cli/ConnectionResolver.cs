using System.Text.Json;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Cli;

/// <summary>
/// 連線解析器，支援多種連線來源
/// 優先順序：CLI 參數 > stdin > 環境變數 > profile 名稱 > connections.json 預設
/// </summary>
public class ConnectionResolver
{
    private readonly IConnectionManager _connectionManager;

    public ConnectionResolver(IConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// 解析連線設定
    /// </summary>
    public ConnectionProfile? Resolve(GlobalOptions options)
    {
        // 1. 完整連線字串
        if (!string.IsNullOrEmpty(options.ConnectionString))
            return FromConnectionString(options.ConnectionString);

        // 2. CLI 參數
        if (!string.IsNullOrEmpty(options.Server))
            return FromCliArgs(options);

        // 3. stdin JSON
        if (options.ConnStdin && Console.IsInputRedirected)
            return FromStdin();

        // 4. 環境變數
        var envProfile = FromEnvironment();
        if (envProfile != null)
            return envProfile;

        // 5. 指定的 profile 名稱
        if (!string.IsNullOrEmpty(options.Profile))
            return FromProfileName(options.Profile);

        // 6. connections.json 預設
        return _connectionManager.GetCurrentProfile();
    }

    /// <summary>
    /// 從連線字串建立 profile
    /// </summary>
    internal static ConnectionProfile FromConnectionString(string connectionString)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        return new ConnectionProfile
        {
            Name = $"{builder.DataSource}/{builder.InitialCatalog}",
            Server = builder.DataSource,
            Database = builder.InitialCatalog,
            AuthType = builder.IntegratedSecurity
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = builder.IntegratedSecurity ? null : builder.UserID,
            Password = builder.IntegratedSecurity ? null : builder.Password
        };
    }

    /// <summary>
    /// 從 CLI 參數建立 profile
    /// </summary>
    internal static ConnectionProfile FromCliArgs(GlobalOptions options)
    {
        var server = options.Server!;
        if (options.Port.HasValue && options.Port.Value != 1433)
            server = $"{server},{options.Port.Value}";

        return new ConnectionProfile
        {
            Name = $"{server}/{options.Database}",
            Server = server,
            Database = options.Database ?? "master",
            AuthType = string.IsNullOrEmpty(options.User)
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = options.User,
            Password = options.Password
        };
    }

    /// <summary>
    /// 從 stdin JSON 建立 profile（相容 mpe show --json 格式）
    /// </summary>
    private static ConnectionProfile? FromStdin()
    {
        try
        {
            var json = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 嘗試 mpe 格式：{ mssql: { host, port, userId, password, applicationDatabase } }
            if (root.TryGetProperty("mssql", out var mssql))
                return FromMpeJson(root, mssql);

            // 簡易格式：{ server, database, user, password }
            return FromSimpleJson(root);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 mpe show --json 格式
    /// </summary>
    internal static ConnectionProfile FromMpeJson(JsonElement root, JsonElement mssql)
    {
        var host = mssql.GetProperty("host").GetString() ?? "localhost";
        var port = mssql.TryGetProperty("port", out var p) ? p.GetString() ?? "1433" : "1433";
        var server = port == "1433" ? host : $"{host},{port}";

        var name = root.TryGetProperty("displayName", out var dn) ? dn.GetString() :
                   root.TryGetProperty("name", out var n) ? n.GetString() : server;

        return new ConnectionProfile
        {
            Name = name ?? server,
            Server = server,
            Database = mssql.TryGetProperty("applicationDatabase", out var db) ? db.GetString() ?? "master" :
                       mssql.TryGetProperty("database", out var db2) ? db2.GetString() ?? "master" : "master",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = mssql.TryGetProperty("userId", out var u) ? u.GetString() :
                       mssql.TryGetProperty("user", out var u2) ? u2.GetString() : null,
            Password = mssql.TryGetProperty("password", out var pw) ? pw.GetString() : null
        };
    }

    /// <summary>
    /// 解析簡易 JSON 格式
    /// </summary>
    internal static ConnectionProfile FromSimpleJson(JsonElement root)
    {
        var server = root.TryGetProperty("server", out var s) ? s.GetString() ?? "localhost" : "localhost";
        var port = root.TryGetProperty("port", out var p) ? p.GetInt32() : 1433;
        if (port != 1433)
            server = $"{server},{port}";

        return new ConnectionProfile
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? server : server,
            Server = server,
            Database = root.TryGetProperty("database", out var db) ? db.GetString() ?? "master" : "master",
            AuthType = root.TryGetProperty("user", out _)
                ? AuthenticationType.SqlServerAuthentication
                : AuthenticationType.WindowsAuthentication,
            Username = root.TryGetProperty("user", out var u) ? u.GetString() : null,
            Password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null
        };
    }

    /// <summary>
    /// 從環境變數建立 profile
    /// </summary>
    internal static ConnectionProfile? FromEnvironment()
    {
        // 先檢查完整連線字串
        var connStr = Environment.GetEnvironmentVariable("SPECURAI_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connStr))
            return FromConnectionString(connStr);

        // 再檢查個別欄位
        var server = Environment.GetEnvironmentVariable("SPECURAI_SERVER");
        if (string.IsNullOrEmpty(server))
            return null;

        var portStr = Environment.GetEnvironmentVariable("SPECURAI_PORT");
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var port) && port != 1433)
            server = $"{server},{port}";

        var database = Environment.GetEnvironmentVariable("SPECURAI_DATABASE") ?? "master";
        var user = Environment.GetEnvironmentVariable("SPECURAI_USER");
        var password = Environment.GetEnvironmentVariable("SPECURAI_PASSWORD");

        return new ConnectionProfile
        {
            Name = $"{server}/{database}",
            Server = server,
            Database = database,
            AuthType = string.IsNullOrEmpty(user)
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = user,
            Password = password
        };
    }

    /// <summary>
    /// 從已儲存的 profile 名稱查找
    /// </summary>
    private ConnectionProfile? FromProfileName(string name)
    {
        return _connectionManager.GetAllProfiles()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 全域選項
/// </summary>
public class GlobalOptions
{
    public string? Server { get; set; }
    public int? Port { get; set; }
    public string? Database { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? ConnectionString { get; set; }
    public bool ConnStdin { get; set; }
    public string? Profile { get; set; }
    public bool Json { get; set; }
}
