using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 效能診斷命令群組
/// </summary>
public static class PerfCommand
{
    public static Command Create()
    {
        var command = new Command("perf", "效能診斷");
        command.AddCommand(CreateWaitsCommand());
        command.AddCommand(CreateQueriesCommand());
        command.AddCommand(CreateProceduresCommand());
        command.AddCommand(CreateMissingIndexesCommand());
        command.AddCommand(CreateUnusedIndexesCommand());
        command.AddCommand(CreateErrorsCommand());
        return command;
    }

    private static Command CreateWaitsCommand()
    {
        var topOption = new Option<int>("--top", () => 20, "顯示前 N 筆");
        var command = new Command("waits", "等候事件統計") { topOption };

        command.SetHandler(async (top) =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var waits = await service.GetWaitStatisticsAsync(top);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(waits, waits.Count);
                return;
            }

            if (waits.Count == 0) { CliOutput.Info("沒有等候事件資料。"); return; }

            var table = new Table().Title("等候事件統計");
            table.AddColumn("等候類型");
            table.AddColumn("等候時間(秒)");
            table.AddColumn("佔比(%)");
            table.AddColumn("次數");
            table.AddColumn("平均(秒)");

            foreach (var w in waits)
            {
                table.AddRow(
                    w.WaitType.EscapeMarkup(),
                    w.WaitTimeSeconds.ToString("N1"),
                    w.Percentage.ToString("N2"),
                    w.WaitCount.ToString("N0"),
                    w.AvgWaitTimeSeconds.ToString("N4"));
            }

            AnsiConsole.Write(table);
        }, topOption);

        return command;
    }

    private static Command CreateQueriesCommand()
    {
        var topOption = new Option<int>("--top", () => 5, "顯示前 N 筆");
        var command = new Command("queries", "耗時查詢 Top N") { topOption };

        command.SetHandler(async (top) =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var queries = await service.GetTopExpensiveQueriesAsync(top);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(queries, queries.Count);
                return;
            }

            if (queries.Count == 0) { CliOutput.Info("沒有耗時查詢資料。"); return; }

            var table = new Table().Title("耗時查詢");
            table.AddColumn("資料庫");
            table.AddColumn(new TableColumn("查詢").Width(60));
            table.AddColumn("執行次數");
            table.AddColumn("總耗時(ms)");
            table.AddColumn("最大耗時(ms)");

            foreach (var q in queries)
            {
                var queryText = q.QueryText.Length > 80 ? q.QueryText[..80] + "..." : q.QueryText;
                table.AddRow(
                    (q.DatabaseName ?? "").EscapeMarkup(),
                    queryText.EscapeMarkup(),
                    q.ExecutionCount.ToString("N0"),
                    q.TotalElapsedTimeMs.ToString("N0"),
                    q.MaxElapsedTimeMs.ToString("N0"));
            }

            AnsiConsole.Write(table);
        }, topOption);

        return command;
    }

    private static Command CreateProceduresCommand()
    {
        var topOption = new Option<int>("--top", () => 5, "顯示前 N 筆");
        var command = new Command("procedures", "耗時預存程序 Top N") { topOption };

        command.SetHandler(async (top) =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var procs = await service.GetTopExpensiveProceduresAsync(top);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(procs, procs.Count);
                return;
            }

            if (procs.Count == 0) { CliOutput.Info("沒有耗時預存程序資料。"); return; }

            var table = new Table().Title("耗時預存程序");
            table.AddColumn("資料庫");
            table.AddColumn("物件");
            table.AddColumn("執行次數");
            table.AddColumn("總耗時(ms)");
            table.AddColumn("最大耗時(ms)");

            foreach (var p in procs)
            {
                table.AddRow(
                    (p.DatabaseName ?? "").EscapeMarkup(),
                    (p.ObjectName ?? p.QueryText[..Math.Min(60, p.QueryText.Length)]).EscapeMarkup(),
                    p.ExecutionCount.ToString("N0"),
                    p.TotalElapsedTimeMs.ToString("N0"),
                    p.MaxElapsedTimeMs.ToString("N0"));
            }

            AnsiConsole.Write(table);
        }, topOption);

        return command;
    }

    private static Command CreateMissingIndexesCommand()
    {
        var command = new Command("missing-indexes", "缺少索引建議");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var indexes = await service.GetMissingIndexesAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(indexes, indexes.Count);
                return;
            }

            if (indexes.Count == 0) { CliOutput.Info("沒有缺少索引建議。"); return; }

            var table = new Table().Title("缺少索引建議");
            table.AddColumn("表格");
            table.AddColumn("改善幅度");
            table.AddColumn("使用者搜尋");
            table.AddColumn("平均影響(%)");
            table.AddColumn(new TableColumn("建議語句").Width(60));

            foreach (var idx in indexes)
            {
                table.AddRow(
                    idx.ShortTableName.EscapeMarkup(),
                    idx.ImprovementMeasure.ToString("N0"),
                    idx.UserSeeks.ToString("N0"),
                    idx.AvgUserImpact.ToString("N1"),
                    idx.CreateIndexStatement.EscapeMarkup()[..Math.Min(80, idx.CreateIndexStatement.Length)]);
            }

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateUnusedIndexesCommand()
    {
        var command = new Command("unused-indexes", "未使用索引");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var indexes = await service.GetUnusedIndexesAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(indexes, indexes.Count);
                return;
            }

            if (indexes.Count == 0) { CliOutput.Info("沒有未使用索引。"); return; }

            var table = new Table().Title("未使用索引");
            table.AddColumn("表格");
            table.AddColumn("索引名稱");
            table.AddColumn("類型");
            table.AddColumn("大小(MB)");
            table.AddColumn("更新次數");
            table.AddColumn("嚴重度");

            foreach (var idx in indexes)
            {
                var severity = idx.SeverityLevel switch
                {
                    "HIGH" => "[red]高[/]",
                    "MEDIUM" => "[yellow]中[/]",
                    _ => "[green]低[/]"
                };
                table.AddRow(
                    $"{idx.SchemaName}.{idx.TableName}".EscapeMarkup(),
                    idx.IndexName.EscapeMarkup(),
                    idx.IndexType.EscapeMarkup(),
                    idx.IndexSizeMB.ToString("N1"),
                    idx.UserUpdates.ToString("N0"),
                    severity);
            }

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateErrorsCommand()
    {
        var daysOption = new Option<int>("--days", () => 7, "查詢天數");
        var command = new Command("errors", "SQL Server 錯誤記錄") { daysOption };

        command.SetHandler(async (days) =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var errors = await service.GetErrorLogAsync(days);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(errors, errors.Count);
                return;
            }

            if (errors.Count == 0) { CliOutput.Info($"最近 {days} 天沒有錯誤記錄。"); return; }

            var table = new Table().Title($"錯誤記錄（最近 {days} 天）");
            table.AddColumn("時間");
            table.AddColumn("來源");
            table.AddColumn(new TableColumn("訊息").Width(80));

            foreach (var e in errors)
            {
                table.AddRow(
                    e.LogDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    (e.ProcessInfo ?? "").EscapeMarkup(),
                    e.Text.EscapeMarkup()[..Math.Min(100, e.Text.Length)]);
            }

            AnsiConsole.Write(table);
        }, daysOption);

        return command;
    }
}
