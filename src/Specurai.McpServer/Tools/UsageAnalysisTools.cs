using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 使用狀態分析 MCP 工具
/// </summary>
[McpServerToolType]
public static class UsageAnalysisTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("掃描目前資料庫的資料表和欄位使用狀態，找出未使用的物件")]
    public static async Task<string> ScanUsage(
        IUsageAnalysisService usageAnalysisService,
        [Description("時間門檻（年，預設 2 年內無使用視為未使用）")] int years = 2)
    {
        try
        {
            var result = await usageAnalysisService.ScanAsync(years, null, CancellationToken.None);

            var summary = new
            {
                result.YearsThreshold,
                Tables = new
                {
                    Total = result.Tables.Count,
                    Used = result.Tables.Count(t => t.UserSeeks > 0 || t.UserScans > 0 || t.UserLookups > 0),
                    Unused = result.Tables.Count(t => t.UserSeeks == 0 && t.UserScans == 0 && t.UserLookups == 0),
                    Items = result.Tables
                },
                Columns = new
                {
                    Total = result.Columns.Count,
                    Used = result.Columns.Count(c => c.IsUsed),
                    Unused = result.Columns.Count(c => !c.IsUsed),
                    UnusedItems = result.Columns.Where(c => !c.IsUsed)
                }
            };

            return JsonSerializer.Serialize(summary, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"使用狀態掃描失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("比對多環境的資料表/欄位使用狀態")]
    public static async Task<string> CompareUsageMultiEnvironment(
        IUsageAnalysisService usageAnalysisService,
        IConnectionManager connectionManager,
        [Description("基準資料庫連線名稱或 ID")] string baseProfileNameOrId,
        [Description("目標資料庫連線名稱或 ID（逗號分隔）")] string targetProfileNameOrIds,
        [Description("時間門檻（年，預設 2）")] int years = 2)
    {
        try
        {
            var baseProfile = ProfileResolver.Resolve(connectionManager, baseProfileNameOrId);
            if (baseProfile == null)
                return $"找不到基準連線「{baseProfileNameOrId}」。";

            var targetIds = ProfileResolver.ResolveMultiple(connectionManager, targetProfileNameOrIds);
            if (targetIds.Count == 0)
                return "沒有有效的目標連線。";

            var result = await usageAnalysisService.CompareAsync(
                baseProfile.Id, targetIds, years, null, CancellationToken.None);

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"多環境使用狀態比對失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("產生 DROP TABLE SQL 語法（不會實際執行，僅回傳 SQL 文字）")]
    public static string GenerateDropTableSql(
        IUsageAnalysisService usageAnalysisService,
        [Description("Schema 名稱（如 dbo）")] string schema,
        [Description("資料表名稱")] string tableName)
    {
        return usageAnalysisService.GenerateDropTableSql(schema, tableName);
    }

    [McpServerTool, Description("產生 DROP COLUMN SQL 語法（不會實際執行，僅回傳 SQL 文字）")]
    public static string GenerateDropColumnSql(
        IUsageAnalysisService usageAnalysisService,
        [Description("Schema 名稱（如 dbo）")] string schema,
        [Description("資料表名稱")] string tableName,
        [Description("欄位名稱")] string columnName)
    {
        return usageAnalysisService.GenerateDropColumnSql(schema, tableName, columnName);
    }
}
