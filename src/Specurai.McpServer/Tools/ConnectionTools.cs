using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線管理 MCP 工具
/// </summary>
[McpServerToolType]
public static class ConnectionTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 列出所有已設定的連線設定檔
    /// </summary>
    [McpServerTool, Description("列出所有已設定的資料庫連線設定檔，包含伺服器、資料庫名稱和目前使用狀態")]
    public static string ListConnections(IConnectionManager connectionManager)
    {
        var profiles = connectionManager.GetAllProfiles();
        var current = connectionManager.GetCurrentProfile();

        if (profiles.Count == 0)
            return "目前沒有任何連線設定。請先在 Specurai 桌面應用程式中新增連線。";

        var result = profiles.Select(p => new
        {
            p.Id,
            p.Name,
            p.Server,
            p.Database,
            AuthType = p.AuthType.ToString(),
            IsCurrent = current?.Id == p.Id,
            // 目前使用中的資料庫（可能因 switch_database 而異於設定檔預設資料庫）
            CurrentDatabase = current?.Id == p.Id ? connectionManager.GetCurrentDatabase() : null,
            p.IsDefault
        });

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 切換至指定的連線設定檔
    /// </summary>
    [McpServerTool, Description("切換至指定的連線設定檔（依名稱或 ID）")]
    public static string SwitchConnection(
        IConnectionManager connectionManager,
        [Description("連線設定名稱或 ID")] string nameOrId)
    {
        var profiles = connectionManager.GetAllProfiles();

        var profile = profiles.FirstOrDefault(p =>
            p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
            return $"找不到名稱或 ID 為「{nameOrId}」的連線設定。";

        connectionManager.SetCurrentProfile(profile.Id);
        return $"已切換至連線「{profile.Name}」（{profile.Server}/{profile.Database}）";
    }

    /// <summary>
    /// 測試目前的連線是否正常
    /// </summary>
    [McpServerTool, Description("測試目前的資料庫連線是否正常")]
    public static async Task<string> TestConnection(IConnectionManager connectionManager)
    {
        var profile = connectionManager.GetCurrentProfile();
        if (profile == null)
            return "目前沒有選擇任何連線設定。請先使用 switch-connection 選擇連線。";

        var success = await connectionManager.TestConnectionAsync(profile);
        return success
            ? $"連線成功：{profile.Name}（{profile.Server}/{profile.Database}）"
            : $"連線失敗：{profile.Name}（{profile.Server}/{profile.Database}）";
    }
}
