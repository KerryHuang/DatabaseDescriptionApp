using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 使用分析命令群組
/// </summary>
public static class UsageCommand
{
    public static Command Create()
    {
        var command = new Command("usage", "使用狀態分析（跨資料庫）");
        command.AddCommand(CreateScanCommand());
        command.AddCommand(CreateCompareCommand());
        command.AddCommand(CreateDropTableSqlCommand());
        command.AddCommand(CreateDropColumnSqlCommand());
        return command;
    }

    private static Command CreateScanCommand()
    {
        var yearsOption = new Option<int>("--years", () => 2, "閒置年數閾值");
        var command = new Command("scan", "掃描未使用的資料表和欄位") { yearsOption };

        command.SetHandler(async (context) =>
        {
            var years = context.ParseResult.GetValueForOption(yearsOption);
            var service = Program.Services.GetRequiredService<IUsageAnalysisService>();

            if (!CliOutput.JsonMode)
                CliOutput.Info($"正在掃描（閾值：{years} 年）...");

            var result = await service.ScanAsync(years, null, CancellationToken.None);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    result.YearsThreshold,
                    UnusedTableCount = result.UnusedTableCount(years),
                    UsedTableCount = result.UsedTableCount(years),
                    UnusedColumnCount = result.UnusedColumnCount,
                    UsedColumnCount = result.UsedColumnCount,
                    UnusedTables = result.Tables.Where(t => !t.HasQueryActivity).Select(t => new
                    {
                        t.SchemaName,
                        t.TableName,
                        t.LastUserSeek,
                        t.LastUserScan,
                        t.LastUserUpdate
                    }),
                    UnusedColumns = result.Columns.Where(c => !c.IsUsed).Select(c => new
                    {
                        c.SchemaName,
                        c.TableName,
                        c.ColumnName,
                        c.DataType,
                        c.IsAllNull,
                        c.IsAllDefault
                    })
                });
                return;
            }

            AnsiConsole.MarkupLine($"[bold]掃描結果[/]（閾值：{years} 年）");
            AnsiConsole.MarkupLine($"  資料表：{result.UsedTableCount(years)} 使用中 / [yellow]{result.UnusedTableCount(years)} 閒置[/]");
            AnsiConsole.MarkupLine($"  欄位：{result.UsedColumnCount} 使用中 / [yellow]{result.UnusedColumnCount} 閒置[/]");

            var unusedTables = result.Tables.Where(t => !t.HasQueryActivity).ToList();
            if (unusedTables.Count > 0)
            {
                var table = new Table().Title("[yellow]閒置資料表[/]");
                table.AddColumn("Schema");
                table.AddColumn("表格");
                table.AddColumn("最後搜尋");
                table.AddColumn("最後更新");

                foreach (var t in unusedTables.Take(20))
                {
                    table.AddRow(
                        t.SchemaName.EscapeMarkup(),
                        t.TableName.EscapeMarkup(),
                        t.LastUserSeek?.ToString("yyyy-MM-dd") ?? "從未",
                        t.LastUserUpdate?.ToString("yyyy-MM-dd") ?? "從未");
                }

                AnsiConsole.Write(table);
                if (unusedTables.Count > 20)
                    CliOutput.Info($"（顯示前 20 筆，共 {unusedTables.Count} 筆）");
            }
        });

        return command;
    }

    private static Command CreateCompareCommand()
    {
        var baseOption = new Option<string?>("--base", "基準連線名稱");
        var targetsOption = new Option<string>("--targets", "目標連線名稱（逗號分隔）") { IsRequired = true };
        var yearsOption = new Option<int>("--years", () => 2, "閒置年數閾值");
        var command = new Command("compare", "多環境使用狀態比對") { baseOption, targetsOption, yearsOption };

        command.SetHandler(async (baseName, targetsStr, years) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var service = Program.Services.GetRequiredService<IUsageAnalysisService>();

            var baseProfile = string.IsNullOrEmpty(baseName)
                ? cm.GetCurrentProfile()
                : cm.GetAllProfiles().FirstOrDefault(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));

            if (baseProfile == null)
            {
                CliOutput.Error("找不到基準連線。");
                Environment.ExitCode = 1;
                return;
            }

            var targetNames = targetsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var targetIds = new List<Guid>();
            foreach (var tn in targetNames)
            {
                var tp = cm.GetAllProfiles().FirstOrDefault(p => p.Name.Equals(tn, StringComparison.OrdinalIgnoreCase));
                if (tp != null) targetIds.Add(tp.Id);
                else CliOutput.Warning($"找不到連線「{tn}」，已跳過");
            }

            if (targetIds.Count == 0)
            {
                CliOutput.Error("沒有有效的目標連線。");
                Environment.ExitCode = 1;
                return;
            }

            if (!CliOutput.JsonMode)
                CliOutput.Info($"正在比對 {baseProfile.Name} ↔ {targetIds.Count} 個目標（閾值：{years} 年）...");

            var result = await service.CompareAsync(baseProfile.Id, targetIds, years, null, CancellationToken.None);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    result.BaseEnvironment,
                    result.TargetEnvironments,
                    TableCount = result.TableRows.Count,
                    ColumnCount = result.ColumnRows.Count
                });
                return;
            }

            AnsiConsole.MarkupLine($"[bold]多環境比對：[/]{result.BaseEnvironment} ↔ {string.Join(", ", result.TargetEnvironments)}");
            AnsiConsole.MarkupLine($"  資料表比對：{result.TableRows.Count} 筆");
            AnsiConsole.MarkupLine($"  欄位比對：{result.ColumnRows.Count} 筆");
        }, baseOption, targetsOption, yearsOption);

        return command;
    }

    private static Command CreateDropTableSqlCommand()
    {
        var objectArg = new Argument<string>("object", "表格名稱（格式：schema.name）");
        var command = new Command("drop-table-sql", "產生 DROP TABLE SQL") { objectArg };

        command.SetHandler((objectName) =>
        {
            var (schema, name) = TablesCommand.ParseObjectName(objectName);
            var service = Program.Services.GetRequiredService<IUsageAnalysisService>();
            var sql = service.GenerateDropTableSql(schema, name);

            if (CliOutput.JsonMode)
                CliOutput.Success(new { Schema = schema, Table = name, Sql = sql });
            else
                Console.WriteLine(sql);
        }, objectArg);

        return command;
    }

    private static Command CreateDropColumnSqlCommand()
    {
        var objectArg = new Argument<string>("object", "欄位名稱（格式：schema.table.column）");
        var command = new Command("drop-column-sql", "產生 DROP COLUMN SQL") { objectArg };

        command.SetHandler((objectName) =>
        {
            var parts = objectName.Split('.', 3);
            string schema, tableName, columnName;

            if (parts.Length == 3) { schema = parts[0]; tableName = parts[1]; columnName = parts[2]; }
            else if (parts.Length == 2) { schema = "dbo"; tableName = parts[0]; columnName = parts[1]; }
            else { CliOutput.Error("格式錯誤，需為 schema.table.column 或 table.column"); Environment.ExitCode = 2; return; }

            var service = Program.Services.GetRequiredService<IUsageAnalysisService>();
            var sql = service.GenerateDropColumnSql(schema, tableName, columnName);

            if (CliOutput.JsonMode)
                CliOutput.Success(new { Schema = schema, Table = tableName, Column = columnName, Sql = sql });
            else
                Console.WriteLine(sql);
        }, objectArg);

        return command;
    }
}
