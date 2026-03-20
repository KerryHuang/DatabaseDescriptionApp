using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Interfaces;

namespace Specurai.McpServer.Tools;

/// <summary>
/// Schema 比對 MCP 工具
/// </summary>
[McpServerToolType]
public static class SchemaCompareTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("比對兩個資料庫連線的 Schema 差異，顯示新增、修改、刪除的物件及風險等級")]
    public static async Task<string> CompareSchemas(
        IConnectionManager connectionManager,
        ISchemaCollector schemaCollector,
        ISchemaCompareService schemaCompareService,
        [Description("基準資料庫連線名稱或 ID")] string baseProfileNameOrId,
        [Description("目標資料庫連線名稱或 ID")] string targetProfileNameOrId)
    {
        try
        {
            var baseProfile = ProfileResolver.Resolve(connectionManager, baseProfileNameOrId);
            if (baseProfile == null)
                return $"找不到基準連線「{baseProfileNameOrId}」。";

            var targetProfile = ProfileResolver.Resolve(connectionManager, targetProfileNameOrId);
            if (targetProfile == null)
                return $"找不到目標連線「{targetProfileNameOrId}」。";

            var baseConnStr = connectionManager.GetConnectionString(baseProfile.Id);
            var targetConnStr = connectionManager.GetConnectionString(targetProfile.Id);

            if (string.IsNullOrEmpty(baseConnStr) || string.IsNullOrEmpty(targetConnStr))
                return "無法取得連線字串，請確認連線設定正確。";

            var baseSchema = await schemaCollector.CollectAsync(baseConnStr, baseProfile.Name);
            var targetSchema = await schemaCollector.CollectAsync(targetConnStr, targetProfile.Name);

            var comparison = await schemaCompareService.CompareAsync(baseSchema, targetSchema);

            var summary = new
            {
                comparison.BaseEnvironment,
                comparison.TargetEnvironment,
                comparison.ComparedAt,
                DifferenceCount = comparison.Differences.Count,
                comparison.RiskSummary,
                Differences = comparison.Differences.Select(d => new
                {
                    d.ObjectType,
                    d.ObjectName,
                    d.DifferenceType,
                    d.RiskLevel,
                    d.Description
                })
            };

            return JsonSerializer.Serialize(summary, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Schema 比對失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("比對一個基準資料庫與多個目標資料庫的 Schema 差異")]
    public static async Task<string> CompareMultipleSchemas(
        IConnectionManager connectionManager,
        ISchemaCollector schemaCollector,
        ISchemaCompareService schemaCompareService,
        [Description("基準資料庫連線名稱或 ID")] string baseProfileNameOrId,
        [Description("目標資料庫連線名稱或 ID（逗號分隔）")] string targetProfileNameOrIds)
    {
        try
        {
            var baseProfile = ProfileResolver.Resolve(connectionManager, baseProfileNameOrId);
            if (baseProfile == null)
                return $"找不到基準連線「{baseProfileNameOrId}」。";

            var baseConnStr = connectionManager.GetConnectionString(baseProfile.Id);
            if (string.IsNullOrEmpty(baseConnStr))
                return "無法取得基準連線字串。";

            var baseSchema = await schemaCollector.CollectAsync(baseConnStr, baseProfile.Name);

            var targetSchemas = new List<Domain.Entities.SchemaCompare.DatabaseSchema>();
            foreach (var nameOrId in targetProfileNameOrIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var profile = ProfileResolver.Resolve(connectionManager, nameOrId);
                if (profile == null) continue;

                var connStr = connectionManager.GetConnectionString(profile.Id);
                if (string.IsNullOrEmpty(connStr)) continue;

                targetSchemas.Add(await schemaCollector.CollectAsync(connStr, profile.Name));
            }

            if (targetSchemas.Count == 0)
                return "沒有有效的目標連線。";

            var comparisons = await schemaCompareService.CompareMultipleAsync(baseSchema, targetSchemas);

            var results = comparisons.Select(c => new
            {
                c.BaseEnvironment,
                c.TargetEnvironment,
                DifferenceCount = c.Differences.Count,
                c.RiskSummary
            });

            return JsonSerializer.Serialize(results, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"多資料庫 Schema 比對失敗：{ex.Message}";
        }
    }
}
