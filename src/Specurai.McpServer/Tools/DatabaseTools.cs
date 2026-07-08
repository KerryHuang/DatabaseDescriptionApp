using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫瀏覽 MCP 工具（SSMS 式：一個連線可瀏覽伺服器上所有使用者資料庫）
/// </summary>
[McpServerToolType]
public static class DatabaseTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 列出目前連線伺服器上的使用者資料庫
    /// </summary>
    [McpServerTool, Description("列出目前連線伺服器上的所有使用者資料庫，並標示目前使用中的資料庫與連線設定檔預設資料庫")]
    public static async Task<string> ListDatabases(IConnectionManager connectionManager)
    {
        var profile = connectionManager.GetCurrentProfile();
        if (profile == null)
            return "目前沒有選擇任何連線設定。請先使用 switch_connection 選擇連線。";

        try
        {
            var databases = await connectionManager.GetDatabasesAsync();
            var current = connectionManager.GetCurrentDatabase();

            var result = databases.Select(name => new
            {
                Name = name,
                IsCurrent = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                IsProfileDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase)
            });

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"無法列舉資料庫（{profile.Server}）：{ex.Message}";
        }
    }

    /// <summary>
    /// 切換目前使用的資料庫
    /// </summary>
    [McpServerTool, Description("切換目前使用的資料庫（僅影響本次工作階段，不變更連線設定檔；使用 switch_connection 可重設回設定檔預設資料庫）")]
    public static async Task<string> SwitchDatabase(
        IConnectionManager connectionManager,
        [Description("資料庫名稱")] string databaseName)
    {
        var profile = connectionManager.GetCurrentProfile();
        if (profile == null)
            return "目前沒有選擇任何連線設定。請先使用 switch_connection 選擇連線。";

        IReadOnlyList<string> databases;
        try
        {
            databases = await connectionManager.GetDatabasesAsync();
        }
        catch (Exception ex)
        {
            return $"無法列舉資料庫（{profile.Server}）：{ex.Message}";
        }

        var target = databases.FirstOrDefault(d =>
            d.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return databases.Count == 0
                ? $"伺服器 {profile.Server} 上找不到使用者資料庫「{databaseName}」。伺服器上目前沒有使用者資料庫。"
                : $"伺服器 {profile.Server} 上找不到使用者資料庫「{databaseName}」。可用資料庫：{string.Join("、", databases)}";
        }

        connectionManager.SetCurrentDatabase(target);
        return $"已切換至資料庫「{target}」（{profile.Server}）";
    }
}
