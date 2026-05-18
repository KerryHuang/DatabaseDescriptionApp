using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Entities;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Cli.Commands;

/// <summary>
/// Schema Migration 命令群組（對齊 Desktop 的 Schema Migration 分頁功能）：
///   analyze    - 分析差異並列出（含風險評估與目標列數）
///   dry-run    - 在目標庫上以 BEGIN TRAN + ROLLBACK 驗證腳本
///   apply      - 實際執行 Migration（GO 分批 + CHECKPOINT + idempotent）
///   preview    - 產生並輸出 Migration SQL 腳本到 stdout / 檔案
///   log-resize - 調整目標庫 LDF 大小（縮小或預擴）
/// </summary>
public static class MigrationCommand
{
    public static Command Create()
    {
        var command = new Command("migration", "Schema Migration（差異分析、Dry Run、執行、SQL 產生、LDF 調整）");
        command.AddCommand(CreateAnalyzeCommand());
        command.AddCommand(CreateDryRunCommand());
        command.AddCommand(CreateApplyCommand());
        command.AddCommand(CreatePreviewCommand());
        command.AddCommand(CreateLogResizeCommand());
        return command;
    }

    // ── 公用 ────────────────────────────────────────────────────────────

    private static (ConnectionProfile? baseProfile, ConnectionProfile? targetProfile) ResolveProfiles(
        IConnectionManager cm, string? baseName, string targetName)
    {
        var baseProfile = string.IsNullOrEmpty(baseName)
            ? cm.GetCurrentProfile()
            : cm.GetAllProfiles().FirstOrDefault(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        if (baseProfile == null)
        {
            CliOutput.Error(string.IsNullOrEmpty(baseName) ? "未設定目前連線" : $"找不到連線「{baseName}」");
            return (null, null);
        }
        var targetProfile = cm.GetAllProfiles()
            .FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (targetProfile == null)
        {
            CliOutput.Error($"找不到連線「{targetName}」");
            return (null, null);
        }
        return (baseProfile, targetProfile);
    }

    private static async Task<MigrationAnalysis?> AnalyzeAsync(string? baseName, string targetName)
    {
        var cm = Program.Services.GetRequiredService<IConnectionManager>();
        var (baseProfile, targetProfile) = ResolveProfiles(cm, baseName, targetName);
        if (baseProfile == null || targetProfile == null) return null;

        if (!CliOutput.JsonMode)
            CliOutput.Info($"正在分析 {baseProfile.Name} → {targetProfile.Name}...");

        var migrationService = Program.Services.GetRequiredService<ISchemaMigrationService>();
        var baseConn = cm.BuildConnectionString(baseProfile);
        var targetConn = cm.BuildConnectionString(targetProfile);
        return await migrationService.AnalyzeAsync(baseConn, targetConn, baseProfile.Name, targetProfile.Name);
    }

    private static SyncScript GenerateScript(MigrationAnalysis analysis, bool highRiskOnly = false)
    {
        var generator = Program.Services.GetRequiredService<ISqlScriptGenerator>();
        var selected = analysis.Comparison.Differences
            .Where(d => highRiskOnly || d.RiskLevel < RiskLevel.High)
            .ToList();
        return generator.Generate(
            selected,
            analysis.BaseSchema,
            analysis.BaseSchema.ConnectionName,
            analysis.TargetSchema.ConnectionName,
            analysis.TargetSchema);
    }

    // ── analyze ─────────────────────────────────────────────────────────

    private static Command CreateAnalyzeCommand()
    {
        var baseOpt = new Option<string?>("--base", "基準連線名稱（不指定則用目前連線）");
        var targetOpt = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var cmd = new Command("analyze", "分析 Schema 差異（含風險評估與目標列數）")
            { baseOpt, targetOpt };

        cmd.SetHandler(async (baseName, targetName) =>
        {
            var analysis = await AnalyzeAsync(baseName, targetName);
            if (analysis == null) { Environment.ExitCode = 1; return; }
            var comparison = analysis.Comparison;

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    Base = analysis.BaseSchema.ConnectionName,
                    Target = analysis.TargetSchema.ConnectionName,
                    HasDifferences = comparison.HasDifferences,
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
                });
                return;
            }

            if (!comparison.HasDifferences)
            {
                CliOutput.SuccessMessage("兩環境 Schema 完全一致！");
                return;
            }

            AnsiConsole.MarkupLine($"[bold]Migration 分析：[/]{analysis.BaseSchema.ConnectionName} → {analysis.TargetSchema.ConnectionName}");
            AnsiConsole.MarkupLine($"  總差異：{comparison.RiskSummary.TotalCount}，" +
                $"[red]高 {comparison.RiskSummary.HighCount}[/]，[yellow]中 {comparison.RiskSummary.MediumCount}[/]，[green]低 {comparison.RiskSummary.LowCount}[/]");

            var table = new Table();
            table.AddColumn("風險");
            table.AddColumn("類型");
            table.AddColumn("物件");
            table.AddColumn("目標列數");
            table.AddColumn("變更");
            table.AddColumn("說明");

            foreach (var d in comparison.Differences)
            {
                var riskStr = d.RiskLevel switch
                {
                    RiskLevel.High or RiskLevel.Forbidden => $"[red]{d.RiskLevel}[/]",
                    RiskLevel.Medium => $"[yellow]{d.RiskLevel}[/]",
                    _ => $"[green]{d.RiskLevel}[/]"
                };
                var changeStr = d.DifferenceType switch
                {
                    DifferenceType.Added => "[green]新增[/]",
                    DifferenceType.Modified => "[yellow]修改[/]",
                    _ => d.DifferenceType.ToString()
                };
                table.AddRow(
                    riskStr,
                    d.ObjectType.ToString(),
                    d.ObjectName.EscapeMarkup(),
                    d.TargetTableRowCount?.ToString("N0") ?? "-",
                    changeStr,
                    (d.Description ?? "").EscapeMarkup());
            }
            AnsiConsole.Write(table);
        }, baseOpt, targetOpt);
        return cmd;
    }

    // ── dry-run ─────────────────────────────────────────────────────────

    private static Command CreateDryRunCommand()
    {
        var baseOpt = new Option<string?>("--base", "基準連線名稱");
        var targetOpt = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var cmd = new Command("dry-run", "對目標庫驗證腳本（每批 BEGIN TRAN + ROLLBACK，無實際變更）")
            { baseOpt, targetOpt };

        cmd.SetHandler(async (baseName, targetName) =>
        {
            var analysis = await AnalyzeAsync(baseName, targetName);
            if (analysis == null) { Environment.ExitCode = 1; return; }
            var script = GenerateScript(analysis);
            if (script.Differences.Count == 0)
            {
                CliOutput.Warning("沒有可執行的差異（高風險已排除）");
                return;
            }

            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var executor = Program.Services.GetRequiredService<ISchemaMigrationExecutor>();
            var targetConn = cm.BuildConnectionString(cm.GetAllProfiles()
                .First(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)));

            if (!CliOutput.JsonMode)
                CliOutput.Info($"Dry Run：{script.Differences.Count} 項差異...");

            var report = await executor.ExecuteAsync(script, targetConn, dryRun: true);
            PrintReport(report, "Dry Run");
            if (!report.IsSuccess) Environment.ExitCode = 1;
        }, baseOpt, targetOpt);
        return cmd;
    }

    // ── apply ───────────────────────────────────────────────────────────

    private static Command CreateApplyCommand()
    {
        var baseOpt = new Option<string?>("--base", "基準連線名稱");
        var targetOpt = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var yesOpt = new Option<bool>("--yes", "略過互動確認直接執行");
        var resizeOpt = new Option<int?>("--log-resize-mb", "Migration 成功後將 LDF 調整到指定 MB");
        var cmd = new Command("apply", "實際執行 Migration（GO 分批 + CHECKPOINT + idempotent）")
            { baseOpt, targetOpt, yesOpt, resizeOpt };

        cmd.SetHandler(async (baseName, targetName, yes, resizeMb) =>
        {
            var analysis = await AnalyzeAsync(baseName, targetName);
            if (analysis == null) { Environment.ExitCode = 1; return; }
            var script = GenerateScript(analysis);
            if (script.Differences.Count == 0)
            {
                CliOutput.Warning("沒有可執行的差異（高風險已排除）");
                return;
            }

            if (!yes && !CliOutput.JsonMode)
            {
                AnsiConsole.MarkupLine($"[bold yellow]即將對 {analysis.TargetSchema.ConnectionName} 執行 {script.Differences.Count} 項變更。[/]");
                if (!AnsiConsole.Confirm("確定要繼續嗎？", false))
                {
                    CliOutput.Info("已取消");
                    return;
                }
            }

            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var executor = Program.Services.GetRequiredService<ISchemaMigrationExecutor>();
            var targetConn = cm.BuildConnectionString(cm.GetAllProfiles()
                .First(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)));

            if (!CliOutput.JsonMode)
                CliOutput.Info($"執行 Migration：{script.Differences.Count} 項差異...");

            var report = await executor.ExecuteAsync(script, targetConn, dryRun: false);
            PrintReport(report, "Migration");

            if (report.IsSuccess && resizeMb.HasValue)
            {
                if (!CliOutput.JsonMode) CliOutput.Info($"調整目標 LDF 至 {resizeMb} MB...");
                var rr = await executor.ResizeLogAsync(targetConn, resizeMb.Value);
                if (rr.IsSuccess) CliOutput.SuccessMessage($"LDF {rr.Operation}：{rr.BeforeSizeMb:N0} → {rr.AfterSizeMb:N0} MB");
                else CliOutput.Error($"LDF 調整失敗：{rr.ErrorMessage}");
            }

            if (!report.IsSuccess) Environment.ExitCode = 1;
        }, baseOpt, targetOpt, yesOpt, resizeOpt);
        return cmd;
    }

    // ── preview ─────────────────────────────────────────────────────────

    private static Command CreatePreviewCommand()
    {
        var baseOpt = new Option<string?>("--base", "基準連線名稱");
        var targetOpt = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var outOpt = new Option<string?>("--out", "輸出檔案路徑（不指定則輸出到 stdout）");
        var includeHighOpt = new Option<bool>("--include-high-risk", "包含高風險項目（預設排除；高風險僅用於 SQL 預覽，不會被 dry-run/apply 執行）");
        var cmd = new Command("preview", "產生 Migration SQL 腳本（輸出到 stdout 或檔案）")
            { baseOpt, targetOpt, outOpt, includeHighOpt };

        cmd.SetHandler(async (baseName, targetName, outFile, includeHigh) =>
        {
            var analysis = await AnalyzeAsync(baseName, targetName);
            if (analysis == null) { Environment.ExitCode = 1; return; }

            var script = GenerateScript(analysis, highRiskOnly: includeHigh);
            if (script.Differences.Count == 0)
            {
                CliOutput.Warning("沒有差異可產生");
                return;
            }

            if (!string.IsNullOrEmpty(outFile))
            {
                await File.WriteAllTextAsync(outFile, script.ApplyScript);
                CliOutput.SuccessMessage($"已寫入 {script.Differences.Count} 項差異到 {outFile}（{script.ApplyScript.Length:N0} 字元）");
            }
            else
            {
                Console.Out.Write(script.ApplyScript);
            }
        }, baseOpt, targetOpt, outOpt, includeHighOpt);
        return cmd;
    }

    // ── log-resize ──────────────────────────────────────────────────────

    private static Command CreateLogResizeCommand()
    {
        var targetOpt = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var sizeOpt = new Option<int>("--size-mb", "目標 LDF 大小 (MB)；> 現大小走預擴，< 現大小走 CHECKPOINT + SHRINKFILE")
            { IsRequired = true };
        var cmd = new Command("log-resize", "調整目標庫 LDF 大小（縮小或預擴）") { targetOpt, sizeOpt };

        cmd.SetHandler(async (targetName, sizeMb) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var targetProfile = cm.GetAllProfiles()
                .FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (targetProfile == null)
            {
                CliOutput.Error($"找不到連線「{targetName}」");
                Environment.ExitCode = 1;
                return;
            }
            if (sizeMb < 64 || sizeMb > 102400)
            {
                CliOutput.Error("--size-mb 必須介於 64 ~ 102400 之間");
                Environment.ExitCode = 1;
                return;
            }

            var executor = Program.Services.GetRequiredService<ISchemaMigrationExecutor>();
            var targetConn = cm.BuildConnectionString(targetProfile);

            if (!CliOutput.JsonMode)
                CliOutput.Info($"調整 {targetProfile.Name} 的 LDF 至 {sizeMb} MB...");

            var rr = await executor.ResizeLogAsync(targetConn, sizeMb);
            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    rr.IsSuccess,
                    rr.Operation,
                    rr.BeforeSizeMb,
                    rr.AfterSizeMb,
                    rr.LogReuseWait,
                    rr.ErrorMessage
                });
                if (!rr.IsSuccess) Environment.ExitCode = 1;
                return;
            }

            if (rr.IsSuccess)
                CliOutput.SuccessMessage($"LDF {rr.Operation}：{rr.BeforeSizeMb:N0} → {rr.AfterSizeMb:N0} MB（log_reuse_wait: {rr.LogReuseWait}）");
            else
            {
                CliOutput.Error($"LDF 調整失敗：{rr.ErrorMessage}");
                Environment.ExitCode = 1;
            }
        }, targetOpt, sizeOpt);
        return cmd;
    }

    // ── 報告輸出 ────────────────────────────────────────────────────────

    private static void PrintReport(MigrationReport report, string title)
    {
        if (CliOutput.JsonMode)
        {
            CliOutput.Success(new
            {
                Title = title,
                report.IsSuccess,
                report.SuccessCount,
                report.SkippedCount,
                FailedCount = report.Entries.Count(e => e.Status == MigrationLogStatus.Failed),
                report.TotalDuration,
                report.ErrorMessage,
                report.FailedStatement
            });
            return;
        }

        var color = report.IsSuccess ? "green" : "red";
        AnsiConsole.MarkupLine($"[bold {color}]{title} {(report.IsSuccess ? "成功" : "失敗")}[/]：" +
            $"成功 {report.SuccessCount}，略過 {report.SkippedCount}，失敗 {report.Entries.Count(e => e.Status == MigrationLogStatus.Failed)}，耗時 {report.TotalDuration.TotalSeconds:F1} 秒");
        if (!report.IsSuccess && !string.IsNullOrEmpty(report.ErrorMessage))
        {
            AnsiConsole.MarkupLine($"[red]錯誤：[/]{report.ErrorMessage.EscapeMarkup()}");
            if (!string.IsNullOrEmpty(report.FailedStatement))
                AnsiConsole.MarkupLine($"[grey]{report.FailedStatement.EscapeMarkup()}[/]");
        }
    }
}
