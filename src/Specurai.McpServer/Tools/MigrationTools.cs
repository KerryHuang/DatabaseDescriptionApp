using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.McpServer.Tools;

/// <summary>
/// Schema Migration MCP 工具（差異分析、Dry Run、執行、SQL 預覽、LDF 調整）
/// </summary>
[McpServerToolType]
public static class MigrationTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // base 預設為目前連線；target 依名稱查找。回傳 error 訊息（成功時為 null）。
    private static (ConnectionProfile? baseProfile, ConnectionProfile? targetProfile, string? error) ResolveProfiles(
        IConnectionManager cm, string? baseName, string targetName)
    {
        var baseProfile = string.IsNullOrEmpty(baseName)
            ? cm.GetCurrentProfile()
            : cm.GetAllProfiles().FirstOrDefault(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        if (baseProfile == null)
            return (null, null, string.IsNullOrEmpty(baseName) ? "未設定目前連線。" : $"找不到連線「{baseName}」。");

        var targetProfile = cm.GetAllProfiles()
            .FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (targetProfile == null)
            return (null, null, $"找不到連線「{targetName}」。");

        return (baseProfile, targetProfile, null);
    }

    // includeHighRisk=false 時排除高風險（與 CLI GenerateScript 一致）。
    private static SyncScript GenerateScript(ISqlScriptGenerator generator, MigrationAnalysis analysis, bool includeHighRisk)
    {
        var selected = analysis.Comparison.Differences
            .Where(d => includeHighRisk || d.RiskLevel < RiskLevel.High)
            .ToList();
        return generator.Generate(
            selected,
            analysis.BaseSchema,
            analysis.BaseSchema.ConnectionName,
            analysis.TargetSchema.ConnectionName,
            analysis.TargetSchema);
    }

    private static object ReportToObject(MigrationReport report, string title) => new
    {
        Title = title,
        report.IsSuccess,
        report.SuccessCount,
        report.SkippedCount,
        FailedCount = report.Entries.Count(e => e.Status == MigrationLogStatus.Failed),
        report.TotalDuration,
        report.ErrorMessage,
        report.FailedStatement
    };

    [McpServerTool, Description("分析基準連線（預設目前連線）與目標連線的 Schema 差異（含風險評估與目標列數，唯讀）")]
    public static async Task<string> MigrationAnalyze(
        IConnectionManager connectionManager,
        ISchemaMigrationService migrationService,
        [Description("目標連線名稱")] string target,
        [Description("基準連線名稱（不指定則用目前連線）")] string? baseConnection = null)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, baseConnection, target);
            if (error != null) return error;

            var baseConn = connectionManager.BuildConnectionString(baseProfile!);
            var targetConn = connectionManager.BuildConnectionString(targetProfile!);
            var analysis = await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile!.Name, targetProfile!.Name);
            var comparison = analysis.Comparison;

            return JsonSerializer.Serialize(new
            {
                Base = analysis.BaseSchema.ConnectionName,
                Target = analysis.TargetSchema.ConnectionName,
                comparison.HasDifferences,
                RiskSummary = new
                {
                    comparison.RiskSummary.HighCount,
                    comparison.RiskSummary.MediumCount,
                    comparison.RiskSummary.LowCount,
                    comparison.RiskSummary.TotalCount
                },
                Differences = comparison.Differences.Select(d => new
                {
                    ObjectType = d.ObjectType.ToString(),
                    d.ObjectName,
                    DifferenceType = d.DifferenceType.ToString(),
                    RiskLevel = d.RiskLevel.ToString(),
                    d.TargetTableRowCount,
                    d.Description
                })
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"分析失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("對目標庫驗證 Migration 腳本（每批 BEGIN TRAN + ROLLBACK，無實際變更；已排除高風險）")]
    public static async Task<string> MigrationDryRun(
        IConnectionManager connectionManager,
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        ISchemaMigrationExecutor executor,
        [Description("目標連線名稱")] string target,
        [Description("基準連線名稱（不指定則用目前連線）")] string? baseConnection = null)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, baseConnection, target);
            if (error != null) return error;

            var baseConn = connectionManager.BuildConnectionString(baseProfile!);
            var targetConn = connectionManager.BuildConnectionString(targetProfile!);
            var analysis = await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile!.Name, targetProfile!.Name);

            var script = GenerateScript(scriptGenerator, analysis, includeHighRisk: false);
            if (script.Differences.Count == 0)
                return "沒有可執行的差異（高風險已排除）。";

            var report = await executor.ExecuteAsync(script, targetConn, dryRun: true);
            return JsonSerializer.Serialize(ReportToObject(report, "Dry Run"), JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Dry Run 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("實際執行 Migration（⚠️ 破壞性操作：會對目標庫套用 Schema 變更，GO 分批 + idempotent；已排除高風險；預設僅回摘要含將套用的差異數，需 confirm:true 才實際執行）")]
    public static async Task<string> MigrationApply(
        IConnectionManager connectionManager,
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        ISchemaMigrationExecutor executor,
        [Description("目標連線名稱")] string target,
        [Description("基準連線名稱（不指定則用目前連線）")] string? baseConnection = null,
        [Description("Migration 成功後將目標 LDF 調整到指定 MB（可選）")] int? logResizeMb = null,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, baseConnection, target);
            if (error != null) return error;

            var baseConn = connectionManager.BuildConnectionString(baseProfile!);
            var targetConn = connectionManager.BuildConnectionString(targetProfile!);
            var analysis = await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile!.Name, targetProfile!.Name);

            var script = GenerateScript(scriptGenerator, analysis, includeHighRisk: false);
            if (script.Differences.Count == 0)
                return "沒有可執行的差異（高風險已排除）。";

            if (!confirm)
            {
                var resizeNote = logResizeMb.HasValue ? $"，並將 LDF 調整為 {logResizeMb} MB" : "";
                return $"將對 {analysis.TargetSchema.ConnectionName} 套用 {script.Differences.Count} 項變更（高風險已排除）{resizeNote}。加 confirm:true 執行。";
            }

            var report = await executor.ExecuteAsync(script, targetConn, dryRun: false);
            var result = ReportToObject(report, "Migration");

            if (report.IsSuccess && logResizeMb.HasValue)
            {
                var rr = await executor.ResizeLogAsync(targetConn, logResizeMb.Value);
                return JsonSerializer.Serialize(new { Migration = result, LogResize = rr }, JsonOptions);
            }

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Migration 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("產生 Migration SQL 腳本並回傳（唯讀，不執行）")]
    public static async Task<string> MigrationPreview(
        IConnectionManager connectionManager,
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        [Description("目標連線名稱")] string target,
        [Description("基準連線名稱（不指定則用目前連線）")] string? baseConnection = null,
        [Description("是否包含高風險項目（預設 false；高風險僅供預覽，不會被 dry-run/apply 執行）")] bool includeHighRisk = false)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, baseConnection, target);
            if (error != null) return error;

            var baseConn = connectionManager.BuildConnectionString(baseProfile!);
            var targetConn = connectionManager.BuildConnectionString(targetProfile!);
            var analysis = await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile!.Name, targetProfile!.Name);

            var script = GenerateScript(scriptGenerator, analysis, includeHighRisk);
            if (script.Differences.Count == 0)
                return "沒有差異可產生。";

            return script.ApplyScript;
        }
        catch (Exception ex)
        {
            return $"產生 SQL 腳本失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("調整目標庫 transaction log（LDF）大小（⚠️ 變更資料庫檔案：縮小走 CHECKPOINT + SHRINKFILE，放大走預擴；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static async Task<string> MigrationLogResize(
        IConnectionManager connectionManager,
        ISchemaMigrationExecutor executor,
        [Description("目標連線名稱")] string target,
        [Description("目標 LDF 大小（MB），須介於 64 ~ 102400")] int sizeMb,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            var targetProfile = connectionManager.GetAllProfiles()
                .FirstOrDefault(p => p.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (targetProfile == null)
                return $"找不到連線「{target}」。";

            if (sizeMb < 64 || sizeMb > 102400)
                return "sizeMb 必須介於 64 ~ 102400 之間。";

            if (!confirm)
                return $"將把 [{target}] 的 LDF 調整為 {sizeMb} MB。加 confirm:true 執行。";

            var targetConn = connectionManager.BuildConnectionString(targetProfile);
            var rr = await executor.ResizeLogAsync(targetConn, sizeMb);
            return JsonSerializer.Serialize(rr, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"LDF 調整失敗：{ex.Message}";
        }
    }
}
