using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 統計資訊 MCP 工具
/// </summary>
[McpServerToolType]
public static class StatisticsTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 取得所有表格的統計資訊
    /// </summary>
    [McpServerTool, Description("取得所有資料表的統計資訊，包含列數、資料大小、索引大小等")]
    public static async Task<string> GetTableStatistics(
        ITableStatisticsService statisticsService)
    {
        try
        {
            var stats = await statisticsService.GetAllTableStatisticsAsync();

            if (stats.Count == 0)
                return "沒有表格統計資料。";

            return JsonSerializer.Serialize(stats, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得表格統計資訊失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得指定表格的精確列數
    /// </summary>
    [McpServerTool, Description("取得指定資料表的精確列數（執行 COUNT(*)，較慢但精確）")]
    public static async Task<string> GetExactRowCount(
        ITableStatisticsService statisticsService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("資料表名稱")] string tableName)
    {
        try
        {
            var count = await statisticsService.GetExactRowCountAsync(schema, tableName);
            return $"[{schema}].[{tableName}] 的精確列數為 {count:N0}。";
        }
        catch (Exception ex)
        {
            return $"取得精確列數失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得欄位使用統計
    /// </summary>
    [McpServerTool, Description("取得資料庫欄位的使用狀態統計，分析各欄位的資料型別一致性")]
    public static async Task<string> GetColumnUsageStatistics(
        IColumnUsageService columnUsageService,
        [Description("搜尋篩選文字（可選）")] string? searchText = null)
    {
        try
        {
            var stats = string.IsNullOrEmpty(searchText)
                ? await columnUsageService.GetStatisticsAsync()
                : await columnUsageService.GetFilteredStatisticsAsync(searchText);

            if (stats.Count == 0)
                return "沒有欄位使用統計資料。";

            return JsonSerializer.Serialize(stats, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得欄位使用統計失敗：{ex.Message}";
        }
    }
}
