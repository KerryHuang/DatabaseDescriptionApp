using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TableSpec.Application.Services;

namespace TableSpec.McpServer.Tools;

/// <summary>
/// 效能診斷 MCP 工具
/// </summary>
[McpServerToolType]
public static class PerformanceTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 取得等候事件統計
    /// </summary>
    [McpServerTool, Description("取得 SQL Server 等候事件統計，用於分析效能瓶頸")]
    public static async Task<string> GetWaitStatistics(
        IPerformanceDiagnosticsService diagnosticsService,
        [Description("顯示前 N 筆（預設 20）")] int top = 20,
        [Description("最小百分比篩選（預設 0）")] decimal minPercentage = 0)
    {
        try
        {
            var stats = await diagnosticsService.GetWaitStatisticsAsync(top, minPercentage);

            if (stats.Count == 0)
                return "沒有等候事件統計資料。";

            return JsonSerializer.Serialize(stats, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得等候事件統計失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得最耗時的查詢
    /// </summary>
    [McpServerTool, Description("取得最耗時的 SQL 查詢，包含執行時間、CPU 時間、讀取次數等")]
    public static async Task<string> GetExpensiveQueries(
        IPerformanceDiagnosticsService diagnosticsService,
        [Description("顯示前 N 筆（預設 5）")] int top = 5)
    {
        try
        {
            var queries = await diagnosticsService.GetTopExpensiveQueriesAsync(top);

            if (queries.Count == 0)
                return "沒有查詢效能資料。";

            return JsonSerializer.Serialize(queries, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得耗時查詢失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得最耗時的預存程序
    /// </summary>
    [McpServerTool, Description("取得最耗時的預存程序，包含執行時間、CPU 時間等")]
    public static async Task<string> GetExpensiveProcedures(
        IPerformanceDiagnosticsService diagnosticsService,
        [Description("顯示前 N 筆（預設 5）")] int top = 5)
    {
        try
        {
            var procedures = await diagnosticsService.GetTopExpensiveProceduresAsync(top);

            if (procedures.Count == 0)
                return "沒有預存程序效能資料。";

            return JsonSerializer.Serialize(procedures, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得耗時預存程序失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得缺少索引建議
    /// </summary>
    [McpServerTool, Description("取得 SQL Server 建議新增的索引，包含預估改善程度和建議的 CREATE INDEX 語句")]
    public static async Task<string> GetMissingIndexes(
        IPerformanceDiagnosticsService diagnosticsService)
    {
        try
        {
            var indexes = await diagnosticsService.GetMissingIndexesAsync();

            if (indexes.Count == 0)
                return "沒有缺少索引的建議。";

            return JsonSerializer.Serialize(indexes, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得缺少索引建議失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得未使用索引清單
    /// </summary>
    [McpServerTool, Description("取得未使用的索引清單，這些索引可能可以移除以改善寫入效能")]
    public static async Task<string> GetUnusedIndexes(
        IPerformanceDiagnosticsService diagnosticsService)
    {
        try
        {
            var indexes = await diagnosticsService.GetUnusedIndexesAsync();

            if (indexes.Count == 0)
                return "沒有未使用的索引。";

            return JsonSerializer.Serialize(indexes, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得未使用索引失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得錯誤記錄
    /// </summary>
    [McpServerTool, Description("取得 SQL Server 錯誤記錄")]
    public static async Task<string> GetErrorLog(
        IPerformanceDiagnosticsService diagnosticsService,
        [Description("查詢天數（預設 7 天）")] int days = 7)
    {
        try
        {
            var errors = await diagnosticsService.GetErrorLogAsync(days);

            if (errors.Count == 0)
                return $"最近 {days} 天沒有錯誤記錄。";

            return JsonSerializer.Serialize(errors, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得錯誤記錄失敗：{ex.Message}";
        }
    }
}
