using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 跨資料庫欄位搜尋 MCP 工具
/// </summary>
[McpServerToolType]
public static class ColumnSearchTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("在多個資料庫中同時搜尋欄位名稱，支援模糊和精確比對")]
    public static async Task<string> SearchColumnsMultiDatabase(
        IColumnSearchService columnSearchService,
        IConnectionManager connectionManager,
        [Description("欄位名稱（支援模糊搜尋）")] string columnName,
        [Description("連線名稱或 ID（逗號分隔，空字串=所有連線）")] string profileNameOrIds = "",
        [Description("精確比對（預設 false）")] bool exactMatch = false,
        [Description("限定資料表名稱（可選）")] string? tableName = null)
    {
        try
        {
            var profileIds = ProfileResolver.ResolveMultiple(connectionManager, profileNameOrIds);
            if (profileIds.Count == 0)
                return "沒有可用的連線設定。";

            var results = await columnSearchService.SearchColumnsMultiAsync(
                columnName, profileIds, exactMatch, tableName);

            if (results.Count == 0)
                return $"在 {profileIds.Count} 個資料庫中找不到符合「{columnName}」的欄位。";

            return JsonSerializer.Serialize(results, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"跨資料庫欄位搜尋失敗：{ex.Message}";
        }
    }
}
