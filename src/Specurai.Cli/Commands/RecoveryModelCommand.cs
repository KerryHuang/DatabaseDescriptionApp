using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 資料庫 Recovery Model 管理命令群組
/// </summary>
public static class RecoveryModelCommand
{
    public static Command Create()
    {
        var command = new Command("recovery-model", "資料庫 Recovery Model 管理");
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateSetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出所有資料庫的 Recovery Model");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IDatabaseRecoveryModelService>();
            var list = (await service.GetAllAsync()).ToList();

            if (CliOutput.JsonMode) { CliOutput.Success(list, list.Count); return; }

            var table = new Table().Title("Recovery Models");
            table.AddColumn("資料庫"); table.AddColumn("Recovery Model");
            foreach (var r in list)
                table.AddRow(r.DatabaseName.EscapeMarkup(), r.RecoveryModel.EscapeMarkup());
            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateSetCommand()
    {
        var dbArg = new Argument<string>("database", "資料庫名稱");
        var modelArg = new Argument<string>("model", "Recovery Model：FULL / SIMPLE / BULK_LOGGED");
        var command = new Command("set", "設定資料庫 Recovery Model") { dbArg, modelArg };

        command.SetHandler(async (db, model) =>
        {
            var normalized = model.ToUpperInvariant().Replace("-", "_");
            if (normalized is not ("FULL" or "SIMPLE" or "BULK_LOGGED"))
            {
                CliOutput.Error("Model 必須為 FULL / SIMPLE / BULK_LOGGED。");
                Environment.ExitCode = 2;
                return;
            }

            var service = Program.Services.GetRequiredService<IDatabaseRecoveryModelService>();
            await service.SaveChangesAsync(new[] { (db, normalized) });

            if (CliOutput.JsonMode) CliOutput.Success(new { Database = db, RecoveryModel = normalized });
            else CliOutput.SuccessMessage($"已設定 [{db}] 的 Recovery Model = {normalized}");
        }, dbArg, modelArg);

        return command;
    }
}
