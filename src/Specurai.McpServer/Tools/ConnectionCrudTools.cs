using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線設定 CRUD MCP 工具
/// </summary>
[McpServerToolType]
public static class ConnectionCrudTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("新增資料庫連線設定（⚠️ 寫入操作）")]
    public static string AddConnection(
        IConnectionManager connectionManager,
        [Description("連線名稱")] string name,
        [Description("伺服器名稱")] string server,
        [Description("資料庫名稱")] string database,
        [Description("認證方式（Windows 或 SqlServer，預設 Windows）")] string authType = "Windows",
        [Description("SQL Server 帳號（SqlServer 認證時必填）")] string? username = null,
        [Description("SQL Server 密碼（SqlServer 認證時必填）")] string? password = null,
        [Description("是否設為預設連線")] bool isDefault = false)
    {
        try
        {
            var auth = authType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? AuthenticationType.SqlServerAuthentication
                : AuthenticationType.WindowsAuthentication;

            var profile = new ConnectionProfile
            {
                Id = Guid.NewGuid(),
                Name = name,
                Server = server,
                Database = database,
                AuthType = auth,
                Username = username ?? string.Empty,
                Password = password ?? string.Empty,
                IsDefault = isDefault
            };

            connectionManager.AddProfile(profile);
            return $"已新增連線「{name}」（{server}/{database}）";
        }
        catch (Exception ex)
        {
            return $"新增連線失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("更新現有的連線設定（⚠️ 寫入操作，僅更新指定的欄位）")]
    public static string UpdateConnection(
        IConnectionManager connectionManager,
        [Description("目前的連線名稱或 ID")] string nameOrId,
        [Description("新名稱（不更改則留空）")] string? newName = null,
        [Description("新伺服器（不更改則留空）")] string? newServer = null,
        [Description("新資料庫（不更改則留空）")] string? newDatabase = null,
        [Description("新認證方式（Windows 或 SqlServer，不更改則留空）")] string? newAuthType = null,
        [Description("新帳號（不更改則留空）")] string? newUsername = null,
        [Description("新密碼（不更改則留空）")] string? newPassword = null)
    {
        try
        {
            var profile = ProfileResolver.ResolveAny(connectionManager, nameOrId);
            if (profile == null)
                return $"找不到連線「{nameOrId}」。";

            if (newName != null) profile.Name = newName;
            if (newServer != null) profile.Server = newServer;
            if (newDatabase != null) profile.Database = newDatabase;
            if (newAuthType != null)
                profile.AuthType = newAuthType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? AuthenticationType.SqlServerAuthentication
                    : AuthenticationType.WindowsAuthentication;
            if (newUsername != null) profile.Username = newUsername;
            if (newPassword != null) profile.Password = newPassword;

            connectionManager.UpdateProfile(profile);
            return $"已更新連線「{profile.Name}」。";
        }
        catch (Exception ex)
        {
            return $"更新連線失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("刪除連線設定（⚠️ 破壞性操作，無法復原；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static string DeleteConnection(
        IConnectionManager connectionManager,
        [Description("連線名稱或 ID")] string nameOrId,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            var profile = ProfileResolver.ResolveAny(connectionManager, nameOrId);
            if (profile == null)
                return $"找不到連線「{nameOrId}」。";

            if (!confirm)
                return $"將刪除連線「{profile.Name}」。加 confirm:true 執行。";

            connectionManager.DeleteProfile(profile.Id);
            return $"已刪除連線「{profile.Name}」。";
        }
        catch (Exception ex)
        {
            return $"刪除連線失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("匯出連線設定為 JSON 檔案")]
    public static string ExportConnections(
        IConnectionManager connectionManager,
        IConnectionExportService exportService,
        [Description("輸出檔案路徑")] string outputPath,
        [Description("是否包含密碼（預設 false）")] bool includePasswords = false)
    {
        try
        {
            var profiles = connectionManager.GetAllProfiles();
            if (profiles.Count == 0)
                return "沒有連線設定可匯出。";

            var data = exportService.ExportToJson(profiles, includePasswords);
            File.WriteAllBytes(outputPath, data);
            return $"已匯出 {profiles.Count} 個連線設定至 {outputPath}";
        }
        catch (Exception ex)
        {
            return $"匯出連線失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("從 JSON 檔案匯入連線設定（⚠️ 寫入操作）")]
    public static string ImportConnections(
        IConnectionManager connectionManager,
        IConnectionExportService exportService,
        [Description("JSON 檔案路徑")] string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"找不到檔案：{filePath}";

            var data = File.ReadAllBytes(filePath);
            var exportData = exportService.ImportFromJson(data);
            var count = 0;

            foreach (var profile in exportData.Profiles)
            {
                var newProfile = new ConnectionProfile
                {
                    Id = Guid.NewGuid(),
                    Name = profile.Name,
                    Server = profile.Server,
                    Database = profile.Database,
                    AuthType = profile.AuthType,
                    Username = profile.Username,
                    Password = profile.Password,
                    IsDefault = false,
                    Environment = profile.Environment,
                    IsEnabled = profile.IsEnabled
                };
                connectionManager.AddProfile(newProfile);
                count++;
            }

            return $"已匯入 {count} 個連線設定。";
        }
        catch (Exception ex)
        {
            return $"匯入連線失敗：{ex.Message}";
        }
    }
}
