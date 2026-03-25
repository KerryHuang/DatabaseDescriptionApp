using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 維護計劃命令群組
/// </summary>
public static class MaintenanceCommand
{
    public static Command Create()
    {
        var command = new Command("maintenance", "維護計劃");
        command.AddCommand(CreateCheckCommand());
        command.AddCommand(CreatePreviewCommand());
        return command;
    }

    private static Command CreateCheckCommand()
    {
        var command = new Command("check", "檢查 SQL Agent 前置條件");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IMaintenancePlanService>();
            var (isReady, errorMessage) = await service.CheckPrerequisitesAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new { IsReady = isReady, ErrorMessage = errorMessage });
                return;
            }

            if (isReady)
                CliOutput.SuccessMessage("SQL Agent 前置條件已滿足，可以建立維護計劃。");
            else
            {
                CliOutput.Error($"前置條件未滿足：{errorMessage}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }

    private static Command CreatePreviewCommand()
    {
        var command = new Command("preview", "預覽維護計劃 SQL（需透過桌面應用程式設定完整參數）");

        command.SetHandler(() =>
        {
            CliOutput.Info("維護計劃的完整設定（備份路徑、還原路徑、排程等）需透過桌面應用程式操作。");
            CliOutput.Info("CLI 提供 maintenance check 檢查前置條件。");
            CliOutput.Info("使用 specurai jobs list 管理已建立的維護排程。");
        });

        return command;
    }
}
