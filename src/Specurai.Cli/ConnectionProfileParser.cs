using System.Text.Json;
using Specurai.Domain.Entities;

namespace Specurai.Cli;

/// <summary>
/// 連線設定 JSON 解析器（共用邏輯）
/// 支援 mpe show --json 格式和簡易 JSON 格式
/// </summary>
public static class ConnectionProfileParser
{
    /// <summary>
    /// 從 JSON 字串解析多個連線設定，支援陣列和單一物件
    /// </summary>
    public static List<ConnectionProfile> ParseMultiple(string json)
    {
        var profiles = new List<ConnectionProfile>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            var profile = ParseSingle(root);
            if (profile != null) profiles.Add(profile);
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                var profile = ParseSingle(element);
                if (profile != null) profiles.Add(profile);
            }
        }

        return profiles;
    }

    /// <summary>
    /// 從 JsonElement 解析單一連線設定
    /// </summary>
    public static ConnectionProfile? ParseSingle(JsonElement element)
    {
        try
        {
            // mpe 格式：{ mssql: { host, port, ... } }
            if (element.TryGetProperty("mssql", out var mssql))
                return FromMpeJson(element, mssql);

            // 簡易格式：{ server, database, user, password }（支援 PascalCase）
            if (element.TryGetProperty("server", out _) || element.TryGetProperty("Server", out _))
                return FromSimpleJson(element);

            return null;
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
                   root.TryGetProperty("name", out var n) ? n.GetString() : null;

        return new ConnectionProfile
        {
            Name = name ?? server,
            Server = server,
            Database = mssql.TryGetProperty("applicationDatabase", out var db) ? db.GetString() ?? "master" :
                       mssql.TryGetProperty("database", out var db2) ? db2.GetString() ?? "master" : "master",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = mssql.TryGetProperty("userId", out var u) ? u.GetString() :
                       mssql.TryGetProperty("user", out var u2) ? u2.GetString() : null,
            Password = mssql.TryGetProperty("password", out var pw) ? pw.GetString() : null,
            Environment = (root.TryGetProperty("envTag", out var et) ? et.GetString() : null) switch
            {
                "prod" => DatabaseEnvironment.Production,
                "dev" => DatabaseEnvironment.Development,
                _ => DatabaseEnvironment.Staging
            },
            IsExternal = true
        };
    }

    /// <summary>
    /// 解析簡易 JSON 格式（支援 camelCase 和 PascalCase）
    /// </summary>
    internal static ConnectionProfile FromSimpleJson(JsonElement root)
    {
        var server = (root.TryGetProperty("server", out var s) ? s.GetString() :
                      root.TryGetProperty("Server", out s) ? s.GetString() : null) ?? "localhost";

        var port = root.TryGetProperty("port", out var p) ? p.GetInt32() : 1433;
        if (port != 1433)
            server = $"{server},{port}";

        return new ConnectionProfile
        {
            Name = (root.TryGetProperty("name", out var nm) ? nm.GetString() :
                    root.TryGetProperty("Name", out nm) ? nm.GetString() : null) ?? server,
            Server = server,
            Database = (root.TryGetProperty("database", out var db) ? db.GetString() :
                        root.TryGetProperty("Database", out db) ? db.GetString() : null) ?? "master",
            AuthType = root.TryGetProperty("user", out _) || root.TryGetProperty("Username", out _)
                ? AuthenticationType.SqlServerAuthentication
                : AuthenticationType.WindowsAuthentication,
            Username = root.TryGetProperty("user", out var user) ? user.GetString() :
                       root.TryGetProperty("Username", out user) ? user.GetString() : null,
            Password = root.TryGetProperty("password", out var pw) ? pw.GetString() :
                       root.TryGetProperty("Password", out pw) ? pw.GetString() : null,
            Environment = ParseEnvironment(root),
            IsExternal = true
        };
    }

    /// <summary>
    /// 從 JSON 物件解析環境設定（支援 camelCase 和 PascalCase）
    /// </summary>
    private static DatabaseEnvironment ParseEnvironment(JsonElement root)
    {
        var text = root.TryGetProperty("environment", out var e) ? e.GetString() :
                   root.TryGetProperty("Environment", out e) ? e.GetString() : null;
        return Enum.TryParse<DatabaseEnvironment>(text, ignoreCase: true, out var env)
            ? env
            : DatabaseEnvironment.Staging;
    }
}
