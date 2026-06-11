# B5 · MCP 復原模式與遷移工具（RecoveryModelTools / MigrationTools）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 `Specurai.McpServer` 新增 `RecoveryModelTools`（list / set）與 `MigrationTools`（analyze / dry_run / apply / preview / log_resize），鏡像 CLI `recovery-model` 與 `migration` 命令，完成 CLI⇄MCP 雙向對齊（依決議全部暴露，含破壞性 `set_recovery_model`、`migration_apply`、`migration_log_resize`）。

**Architecture:** 純展示層接線。MCP 工具為帶 `[McpServerToolType]`/`[McpServerTool]` 的靜態類別，由 `.WithToolsFromAssembly()` 自動探索，不需改 `Program.cs`。服務 `IDatabaseRecoveryModelService`、`ISchemaMigrationService`、`ISqlScriptGenerator`、`ISchemaMigrationExecutor`、`IConnectionManager` 皆已由 `AddSpecuraiCore()` 註冊。連線解析以 `GetCurrentProfile()`（base 預設）與依名稱查找（target）取代 CLI 的 ConnectionResolver。回傳字串、try/catch 包友善錯誤訊息（既有 MCP 慣例）。

**Tech Stack:** .NET 8、ModelContextProtocol SDK、System.Text.Json。

---

## 測試與驗證策略

沿用既有 MCP 工具「薄 DI 包裝、無單元測試」慣例（專案無 McpServer 測試專案）。驗證：`dotnet build` 成功 + 程式碼審查 + **安全唯讀**煙霧測試（`list_recovery_models`、`migration_analyze`、`migration_preview` 皆唯讀）。**破壞性 `set_recovery_model`、`migration_apply`、`migration_log_resize` 不對真實資料庫煙霧測試**，以忠實鏡像已審查 CLI 邏輯 + code review 保證。

---

## File Structure

- Create: `src/Specurai.McpServer/Tools/RecoveryModelTools.cs`
- Create: `src/Specurai.McpServer/Tools/MigrationTools.cs`

鏡像來源：`src/Specurai.Cli/Commands/RecoveryModelCommand.cs`、`MigrationCommand.cs`。

關鍵既有型別（已驗證）：
- `IDatabaseRecoveryModelService.GetAllAsync()→Task<IEnumerable<DatabaseRecoveryModel>>`（`{ DatabaseName, RecoveryModel }`）；`SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)>, ct)`。
- `ISchemaMigrationService.AnalyzeAsync(baseConn, targetConn, baseEnvName, targetEnvName, ct)→Task<MigrationAnalysis>`。
- `ISqlScriptGenerator.Generate(IList<SchemaDifference> selected, DatabaseSchema baseSchema, string baseEnvName, string targetEnvName, DatabaseSchema? targetSchema)→SyncScript`。
- `ISchemaMigrationExecutor.ExecuteAsync(SyncScript, targetConn, bool dryRun, ct)→Task<MigrationReport>`；`ResizeLogAsync(targetConn, int sizeMb, ct)→Task<ResizeLogResult>`。
- `MigrationAnalysis { Comparison{ HasDifferences, RiskSummary{HighCount,MediumCount,LowCount,TotalCount}, Differences[]{ObjectType,ObjectName,DifferenceType,RiskLevel,TargetTableRowCount,Description} }, BaseSchema{ConnectionName}, TargetSchema{ConnectionName} }`。
- `SyncScript { ApplyScript, Differences }`；`MigrationReport { IsSuccess, SuccessCount, SkippedCount, Entries[]{Status}, TotalDuration, ErrorMessage, FailedStatement }`；`ResizeLogResult { IsSuccess, Operation, BeforeSizeMb, AfterSizeMb, LogReuseWait, ErrorMessage }`。
- Enums：`RiskLevel`（Low/Medium/High/Forbidden）、`DifferenceType`、`MigrationLogStatus`。

---

## Task 1: `RecoveryModelTools`

**Files:** Create `src/Specurai.McpServer/Tools/RecoveryModelTools.cs`

- [ ] **Step 1: 建立工具類別**

```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫 Recovery Model MCP 工具
/// </summary>
[McpServerToolType]
public static class RecoveryModelTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("列出目前伺服器上所有資料庫的 Recovery Model")]
    public static async Task<string> ListRecoveryModels(IDatabaseRecoveryModelService service)
    {
        try
        {
            var list = (await service.GetAllAsync()).ToList();
            return JsonSerializer.Serialize(list, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得 Recovery Model 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("設定指定資料庫的 Recovery Model（⚠️ 變更資料庫設定，會影響交易記錄行為）")]
    public static async Task<string> SetRecoveryModel(
        IDatabaseRecoveryModelService service,
        [Description("資料庫名稱")] string database,
        [Description("Recovery Model：FULL / SIMPLE / BULK_LOGGED")] string model)
    {
        try
        {
            var normalized = model.ToUpperInvariant().Replace("-", "_");
            if (normalized is not ("FULL" or "SIMPLE" or "BULK_LOGGED"))
                return "Model 必須為 FULL / SIMPLE / BULK_LOGGED。";

            await service.SaveChangesAsync(new[] { (database, normalized) });
            return $"已設定 [{database}] 的 Recovery Model = {normalized}。";
        }
        catch (Exception ex)
        {
            return $"設定 Recovery Model 失敗：{ex.Message}";
        }
    }
}
```

- [ ] **Step 2: 建置 McpServer**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.McpServer/Tools/RecoveryModelTools.cs
git commit -m "feat(mcp): RecoveryModelTools 對齊 CLI recovery-model（list/set）"
```

---

## Task 2: `MigrationTools`

**Files:** Create `src/Specurai.McpServer/Tools/MigrationTools.cs`

- [ ] **Step 1: 建立工具類別**

```csharp
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
        [Description("基準連線名稱（不指定則用目前連線）")] string? @base = null)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, @base, target);
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
        [Description("基準連線名稱（不指定則用目前連線）")] string? @base = null)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, @base, target);
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

    [McpServerTool, Description("實際執行 Migration（⚠️ 破壞性操作：會對目標庫套用 Schema 變更，GO 分批 + idempotent；已排除高風險）")]
    public static async Task<string> MigrationApply(
        IConnectionManager connectionManager,
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        ISchemaMigrationExecutor executor,
        [Description("目標連線名稱")] string target,
        [Description("基準連線名稱（不指定則用目前連線）")] string? @base = null,
        [Description("Migration 成功後將目標 LDF 調整到指定 MB（可選）")] int? logResizeMb = null)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, @base, target);
            if (error != null) return error;

            var baseConn = connectionManager.BuildConnectionString(baseProfile!);
            var targetConn = connectionManager.BuildConnectionString(targetProfile!);
            var analysis = await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile!.Name, targetProfile!.Name);

            var script = GenerateScript(scriptGenerator, analysis, includeHighRisk: false);
            if (script.Differences.Count == 0)
                return "沒有可執行的差異（高風險已排除）。";

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
        [Description("基準連線名稱（不指定則用目前連線）")] string? @base = null,
        [Description("是否包含高風險項目（預設 false；高風險僅供預覽，不會被 dry-run/apply 執行）")] bool includeHighRisk = false)
    {
        try
        {
            var (baseProfile, targetProfile, error) = ResolveProfiles(connectionManager, @base, target);
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

    [McpServerTool, Description("調整目標庫 transaction log（LDF）大小（⚠️ 變更資料庫檔案：縮小走 CHECKPOINT + SHRINKFILE，放大走預擴）")]
    public static async Task<string> MigrationLogResize(
        IConnectionManager connectionManager,
        ISchemaMigrationExecutor executor,
        [Description("目標連線名稱")] string target,
        [Description("目標 LDF 大小（MB），須介於 64 ~ 102400")] int sizeMb)
    {
        try
        {
            var targetProfile = connectionManager.GetAllProfiles()
                .FirstOrDefault(p => p.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (targetProfile == null)
                return $"找不到連線「{target}」。";

            if (sizeMb < 64 || sizeMb > 102400)
                return "sizeMb 必須介於 64 ~ 102400 之間。";

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
```

- [ ] **Step 2: 建置 McpServer**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.McpServer/Tools/MigrationTools.cs
git commit -m "feat(mcp): MigrationTools 對齊 CLI migration（analyze/dry-run/apply/preview/log-resize）"
```

---

## Task 3: 驗證與審查

- [ ] **Step 1: McpServer 建置綠燈**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded。

- [ ] **Step 2: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查 `RecoveryModelTools.cs`、`MigrationTools.cs`，確認與 CLI 鏡像來源行為一致、連線解析正確、破壞性工具描述清楚標註。

- [ ] **Step 3: 安全煙霧測試（需 republish）**

> MCP 變更需重新 publish `Specurai.McpServer.exe`（須先關閉執行中的 MCP 行程）才會在 client 生效。republish 後僅以唯讀的 `list_recovery_models`、`migration_analyze`、`migration_preview` 驗證；勿執行破壞性 `set_recovery_model`/`migration_apply`/`migration_log_resize`。

---

## Self-Review 紀錄

- **Spec 覆蓋**：B5 範圍（RecoveryModelTools list/set、MigrationTools analyze/dry-run/apply/preview/log-resize）皆有對應 Task。✅
- **Placeholder 掃描**：無 TBD/TODO；每個程式碼步驟均含完整程式碼。✅
- **型別一致性**：所有服務方法簽章與實體屬性、enum 名稱與既有定義相符；`GenerateScript` 的 `includeHighRisk` 過濾邏輯與 CLI `GenerateScript(highRiskOnly)` 一致（true → 全部、false → 排除高風險）。✅
- **鏡像一致性**：ResolveProfiles、analyze→generate→execute 流程與 `MigrationCommand.cs` 一致；CLI 的互動確認（apply 的 AnsiConsole.Confirm）依「全部暴露」決議於 MCP 省略，並以 ⚠️ 標註破壞性。log-resize 的 64~102400 範圍檢查與 CLI 一致。
- **刻意取捨**：沿用既有 MCP 工具「薄包裝無單元測試」慣例；破壞性操作不對真實資料庫煙霧測試。Recovery Model `set` 鏡像 CLI 一次設定單一資料庫（`SaveChangesAsync` 單元素陣列）。
