using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 索引診斷命令群組
/// </summary>
public static class IndexCommand
{
    public static Command Create()
    {
        var command = new Command("index", "索引診斷與建議");
        command.AddCommand(CreateMissingCommand());
        command.AddCommand(CreateUnusedCommand());
        command.AddCommand(CreateCreateCommand());
        command.AddCommand(CreateDropCommand());
        return command;
    }

    private static Command CreateMissingCommand()
    {
        var command = new Command("missing", "列出缺少的索引建議");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var list = await service.GetMissingIndexesAsync();

            if (CliOutput.JsonMode) { CliOutput.Success(list, list.Count); return; }
            if (list.Count == 0) { CliOutput.Info("沒有缺少索引建議。"); return; }

            var table = new Table().Title("Missing Indexes");
            table.AddColumn("資料表"); table.AddColumn("改善指標"); table.AddColumn("嚴重度"); table.AddColumn("Seek"); table.AddColumn("Scan");
            foreach (var m in list)
            {
                table.AddRow(
                    m.TableName.EscapeMarkup(),
                    m.ImprovementMeasure.ToString("F0"),
                    m.SeverityLevel,
                    m.UserSeeks.ToString(),
                    m.UserScans.ToString());
            }
            AnsiConsole.Write(table);
            CliOutput.Info($"共 {list.Count} 筆。使用 --json 取得 CREATE INDEX 完整語法。");
        });

        return command;
    }

    private static Command CreateUnusedCommand()
    {
        var command = new Command("unused", "列出未使用的索引");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            var list = await service.GetUnusedIndexesAsync();

            if (CliOutput.JsonMode) { CliOutput.Success(list, list.Count); return; }
            if (list.Count == 0) { CliOutput.Info("沒有未使用的索引。"); return; }

            var table = new Table().Title("Unused Indexes");
            table.AddColumn("資料庫"); table.AddColumn("資料表"); table.AddColumn("索引"); table.AddColumn("更新次數"); table.AddColumn("大小(MB)"); table.AddColumn("嚴重度");
            foreach (var u in list)
            {
                table.AddRow(
                    u.DatabaseName.EscapeMarkup(),
                    $"{u.SchemaName}.{u.TableName}".EscapeMarkup(),
                    u.IndexName.EscapeMarkup(),
                    u.UserUpdates.ToString(),
                    u.IndexSizeMB.ToString("F2"),
                    u.SeverityLevel);
            }
            AnsiConsole.Write(table);
            CliOutput.Info($"共 {list.Count} 筆。");
        });

        return command;
    }

    private static Command CreateCreateCommand()
    {
        var sqlArg = new Argument<string>("sql", "完整 CREATE INDEX 語法");
        var command = new Command("create", "執行 CREATE INDEX 語法") { sqlArg };

        command.SetHandler(async (sql) =>
        {
            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            try
            {
                await service.ExecuteCreateIndexAsync(sql);
                if (CliOutput.JsonMode) CliOutput.Success(new { Message = "索引已建立" });
                else CliOutput.SuccessMessage("索引已建立");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"建立失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, sqlArg);

        return command;
    }

    private static Command CreateDropCommand()
    {
        var sqlArg = new Argument<string>("sql", "完整 DROP INDEX 語法");
        var dbOption = new Option<string>("--database", "目標資料庫名稱") { IsRequired = true };
        var yesOption = new Option<bool>("--yes", "略過確認");
        var command = new Command("drop", "執行 DROP INDEX 語法") { sqlArg, dbOption, yesOption };

        command.SetHandler(async (sql, db, yes) =>
        {
            if (!yes && !CliOutput.JsonMode && !AnsiConsole.Confirm($"確定要在 [{db}] 執行此 DROP INDEX?", false))
                return;

            var service = Program.Services.GetRequiredService<IPerformanceDiagnosticsService>();
            try
            {
                await service.ExecuteDropIndexAsync(sql, db);
                if (CliOutput.JsonMode) CliOutput.Success(new { Message = "索引已刪除" });
                else CliOutput.SuccessMessage("索引已刪除");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"刪除失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, sqlArg, dbOption, yesOption);

        return command;
    }
}
