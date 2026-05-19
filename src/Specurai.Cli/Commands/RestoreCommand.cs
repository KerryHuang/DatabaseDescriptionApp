using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Cli.Commands;

/// <summary>
/// 資料庫還原命令群組
/// </summary>
public static class RestoreCommand
{
    public static Command Create()
    {
        var command = new Command("restore", "資料庫還原");
        command.AddCommand(CreateRunCommand());
        return command;
    }

    private static Command CreateRunCommand()
    {
        var pathArg = new Argument<string>("path", "備份檔案路徑");
        var modeOption = new Option<string>("--mode", () => "overwrite", "還原模式：overwrite / new");
        var targetOption = new Option<string?>("--target", "目標資料庫名稱（mode=new 時必填）");
        var dataPathOption = new Option<string?>("--data-path", "資料檔路徑（mode=new 時可指定）");
        var logPathOption = new Option<string?>("--log-path", "日誌檔路徑（mode=new 時可指定）");
        var yesOption = new Option<bool>("--yes", "略過確認");
        var command = new Command("run", "執行還原") { pathArg, modeOption, targetOption, dataPathOption, logPathOption, yesOption };

        command.SetHandler(async (path, modeStr, target, dataPath, logPath, yes) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null) { CliOutput.Error("找不到可用的連線設定。"); Environment.ExitCode = 2; return; }

            var mode = modeStr.ToLowerInvariant() switch
            {
                "new" or "create" => RestoreMode.CreateNew,
                _ => RestoreMode.OverwriteExisting
            };

            if (mode == RestoreMode.CreateNew && string.IsNullOrWhiteSpace(target))
            {
                CliOutput.Error("mode=new 時必須指定 --target。");
                Environment.ExitCode = 2;
                return;
            }

            if (!yes && !CliOutput.JsonMode)
            {
                var msg = mode == RestoreMode.OverwriteExisting
                    ? $"確定要覆蓋資料庫 [{profile.Database}]?"
                    : $"確定要還原到新資料庫 [{target}]?";
                if (!AnsiConsole.Confirm(msg, false)) return;
            }

            var options = new RestoreOptions
            {
                Mode = mode,
                TargetDatabaseName = target,
                DataFilePath = dataPath,
                LogFilePath = logPath,
                WithReplace = mode == RestoreMode.OverwriteExisting,
                WithRecovery = true,
                ShowProgress = false
            };

            var service = Program.Services.GetRequiredService<IBackupService>();
            // 還原需連到 master
            var masterProfile = new ConnectionProfile
            {
                Name = profile.Name,
                Server = profile.Server,
                Database = "master",
                AuthType = profile.AuthType,
                Username = profile.Username,
                Password = profile.Password
            };
            var connStr = cm.BuildConnectionString(masterProfile);

            try
            {
                await service.RestoreDatabaseAsync(connStr, path, options);
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Path = path, Mode = mode.ToString(), Target = target ?? profile.Database });
                else
                    CliOutput.SuccessMessage("還原完成");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"還原失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, pathArg, modeOption, targetOption, dataPathOption, logPathOption, yesOption);

        return command;
    }
}
