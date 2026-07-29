using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Interfaces;

namespace Specurai.Cli.Commands;

/// <summary>
/// Schema 比對命令群組（跨資料庫核心功能）
/// </summary>
public static class SchemaCommand
{
    public static Command Create()
    {
        var command = new Command("schema", "Schema 比對（跨資料庫）");
        command.AddCommand(CreateCompareCommand());
        command.AddCommand(CreateCompareMultiCommand());
        return command;
    }

    private static Command CreateCompareCommand()
    {
        var baseOption = new Option<string?>("--base", "基準連線名稱（不指定則用目前連線）");
        var targetOption = new Option<string>("--target", "目標連線名稱") { IsRequired = true };
        var command = new Command("compare", "兩環境 Schema 比對") { baseOption, targetOption };

        command.SetHandler(async (baseName, targetName) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var collector = Program.Services.GetRequiredService<ISchemaCollector>();
            var compareService = Program.Services.GetRequiredService<ISchemaCompareService>();

            // 解析 base 連線
            var baseProfile = string.IsNullOrEmpty(baseName)
                ? cm.GetCurrentProfile()
                : cm.GetEnabledProfiles().FirstOrDefault(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));

            if (baseProfile == null)
            {
                CliOutput.Error(string.IsNullOrEmpty(baseName) ? "未設定目前連線" : ConnectionResolver.DescribeMissing(cm, baseName));
                Environment.ExitCode = 1;
                return;
            }

            // 解析 target 連線
            var targetProfile = cm.GetEnabledProfiles()
                .FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

            if (targetProfile == null)
            {
                CliOutput.Error(ConnectionResolver.DescribeMissing(cm, targetName));
                Environment.ExitCode = 1;
                return;
            }

            if (!CliOutput.JsonMode)
                CliOutput.Info($"正在比對 {baseProfile.Name} ↔ {targetProfile.Name}...");

            var baseConnStr = cm.BuildConnectionString(baseProfile);
            var targetConnStr = cm.BuildConnectionString(targetProfile);

            var baseSchema = await collector.CollectAsync(baseConnStr, baseProfile.Name);
            var targetSchema = await collector.CollectAsync(targetConnStr, targetProfile.Name);
            var comparison = await compareService.CompareAsync(baseSchema, targetSchema);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    comparison.BaseEnvironment,
                    comparison.TargetEnvironment,
                    comparison.ComparedAt,
                    comparison.HasDifferences,
                    RiskSummary = new
                    {
                        comparison.RiskSummary.LowCount,
                        comparison.RiskSummary.MediumCount,
                        comparison.RiskSummary.HighCount,
                        comparison.RiskSummary.ForbiddenCount,
                        comparison.RiskSummary.TotalCount
                    },
                    Differences = comparison.Differences.Select(d => new
                    {
                        ObjectType = d.ObjectType.ToString(),
                        d.ObjectName,
                        DifferenceType = d.DifferenceType.ToString(),
                        RiskLevel = d.RiskLevel.ToString(),
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

            AnsiConsole.MarkupLine($"[bold]比對結果：[/]{baseProfile.Name} ↔ {targetProfile.Name}");
            AnsiConsole.MarkupLine($"  差異總數：{comparison.RiskSummary.TotalCount}");
            if (comparison.RiskSummary.HighCount > 0)
                AnsiConsole.MarkupLine($"  [red]高風險：{comparison.RiskSummary.HighCount}[/]");
            if (comparison.RiskSummary.MediumCount > 0)
                AnsiConsole.MarkupLine($"  [yellow]中風險：{comparison.RiskSummary.MediumCount}[/]");

            var table = new Table();
            table.AddColumn("物件類型");
            table.AddColumn("物件名稱");
            table.AddColumn("變更類型");
            table.AddColumn("風險");
            table.AddColumn("詳情");

            foreach (var d in comparison.Differences)
            {
                var riskStr = d.RiskLevel.ToString();
                var risk = d.RiskLevel switch
                {
                    Domain.Enums.RiskLevel.High or Domain.Enums.RiskLevel.Forbidden => $"[red]{riskStr}[/]",
                    Domain.Enums.RiskLevel.Medium => $"[yellow]{riskStr}[/]",
                    _ => $"[green]{riskStr}[/]"
                };
                var change = d.DifferenceType switch
                {
                    Domain.Enums.DifferenceType.Added => "[green]新增[/]",
                    Domain.Enums.DifferenceType.Modified => "[yellow]修改[/]",
                    _ => d.DifferenceType.ToString()
                };
                table.AddRow(
                    d.ObjectType.ToString(),
                    d.ObjectName.EscapeMarkup(),
                    change,
                    risk,
                    (d.Description ?? "").EscapeMarkup());
            }

            AnsiConsole.Write(table);
        }, baseOption, targetOption);

        return command;
    }

    private static Command CreateCompareMultiCommand()
    {
        var baseOption = new Option<string?>("--base", "基準連線名稱（不指定則用目前連線）");
        var targetsOption = new Option<string>("--targets", "目標連線名稱（逗號分隔）") { IsRequired = true };
        var command = new Command("compare-multi", "一對多 Schema 比對") { baseOption, targetsOption };

        command.SetHandler(async (baseName, targetsStr) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var collector = Program.Services.GetRequiredService<ISchemaCollector>();
            var compareService = Program.Services.GetRequiredService<ISchemaCompareService>();

            var baseProfile = string.IsNullOrEmpty(baseName)
                ? cm.GetCurrentProfile()
                : cm.GetEnabledProfiles().FirstOrDefault(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));

            if (baseProfile == null)
            {
                CliOutput.Error(string.IsNullOrEmpty(baseName) ? "未設定目前連線" : ConnectionResolver.DescribeMissing(cm, baseName));
                Environment.ExitCode = 1;
                return;
            }

            var targetNames = targetsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var targetProfiles = new List<(Domain.Entities.ConnectionProfile profile, string connStr)>();

            foreach (var tn in targetNames)
            {
                var tp = cm.GetEnabledProfiles().FirstOrDefault(p => p.Name.Equals(tn, StringComparison.OrdinalIgnoreCase));
                if (tp == null)
                {
                    CliOutput.Warning($"{ConnectionResolver.DescribeMissing(cm, tn)}，已跳過");
                    continue;
                }
                targetProfiles.Add((tp, cm.BuildConnectionString(tp)));
            }

            if (targetProfiles.Count == 0)
            {
                CliOutput.Error("沒有有效的目標連線。");
                Environment.ExitCode = 1;
                return;
            }

            if (!CliOutput.JsonMode)
                CliOutput.Info($"正在比對 {baseProfile.Name} ↔ {targetProfiles.Count} 個目標環境...");

            var baseConnStr = cm.BuildConnectionString(baseProfile);
            var baseSchema = await collector.CollectAsync(baseConnStr, baseProfile.Name);

            var targetSchemas = new List<Domain.Entities.SchemaCompare.DatabaseSchema>();
            foreach (var (tp, connStr) in targetProfiles)
            {
                targetSchemas.Add(await collector.CollectAsync(connStr, tp.Name));
            }

            var comparisons = await compareService.CompareMultipleAsync(baseSchema, targetSchemas);

            if (CliOutput.JsonMode)
            {
                var data = comparisons.Select(c => new
                {
                    c.BaseEnvironment,
                    c.TargetEnvironment,
                    c.HasDifferences,
                    DifferenceCount = c.Differences.Count,
                    RiskSummary = new
                    {
                        c.RiskSummary.HighCount,
                        c.RiskSummary.MediumCount,
                        c.RiskSummary.LowCount,
                        c.RiskSummary.TotalCount
                    }
                }).ToList();
                CliOutput.Success(data, data.Count);
                return;
            }

            // 摘要表格
            var table = new Table().Title($"多環境比對摘要（基準：{baseProfile.Name}）");
            table.AddColumn("目標環境");
            table.AddColumn("差異數");
            table.AddColumn("高風險");
            table.AddColumn("中風險");
            table.AddColumn("狀態");

            foreach (var c in comparisons)
            {
                var status = !c.HasDifferences ? "[green]一致[/]" :
                    c.RiskSummary.HighCount > 0 ? "[red]有高風險差異[/]" :
                    "[yellow]有差異[/]";
                table.AddRow(
                    c.TargetEnvironment.EscapeMarkup(),
                    c.Differences.Count.ToString(),
                    c.RiskSummary.HighCount > 0 ? $"[red]{c.RiskSummary.HighCount}[/]" : "0",
                    c.RiskSummary.MediumCount > 0 ? $"[yellow]{c.RiskSummary.MediumCount}[/]" : "0",
                    status);
            }

            AnsiConsole.Write(table);
        }, baseOption, targetsOption);

        return command;
    }
}
