using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Cli.Commands;

/// <summary>
/// 資料庫備份命令群組
/// </summary>
public static class BackupCommand
{
    public static Command Create()
    {
        var command = new Command("backup", "資料庫備份管理");
        command.AddCommand(CreateRunCommand());
        command.AddCommand(CreateVerifyCommand());
        command.AddCommand(CreateInfoCommand());
        command.AddCommand(CreateHistoryCommand());
        return command;
    }

    private static Command CreateRunCommand()
    {
        var pathArg = new Argument<string>("path", "備份檔案路徑（.bak）");
        var typeOption = new Option<string>("--type", () => "full", "備份類型：full / diff / log");
        var descOption = new Option<string?>("--description", "備份描述");
        var noVerifyOption = new Option<bool>("--no-verify", "備份後不執行驗證");
        var command = new Command("run", "執行資料庫備份") { pathArg, typeOption, descOption, noVerifyOption };

        command.SetHandler(async (path, type, desc, noVerify) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null) { CliOutput.Error("找不到可用的連線設定。"); Environment.ExitCode = 2; return; }

            var backupType = type.ToLowerInvariant() switch
            {
                "full" => BackupType.Full,
                "diff" or "differential" => BackupType.Differential,
                "log" or "trn" => BackupType.TransactionLog,
                _ => BackupType.Full
            };

            var service = Program.Services.GetRequiredService<IBackupService>();
            var connStr = cm.BuildConnectionString(profile);

            try
            {
                var info = await service.BackupDatabaseAsync(connStr, profile.Id, profile.Name, path, backupType, desc);

                if (!noVerify)
                {
                    var verify = await service.VerifyBackupAsync(connStr, path);
                    if (!verify.IsValid)
                    {
                        CliOutput.Error($"備份驗證失敗：{verify.ErrorMessage}");
                        Environment.ExitCode = 1;
                        return;
                    }
                }

                if (CliOutput.JsonMode) CliOutput.Success(info);
                else CliOutput.SuccessMessage($"備份完成：{path}");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"備份失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, pathArg, typeOption, descOption, noVerifyOption);

        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var pathArg = new Argument<string>("path", "備份檔案路徑");
        var command = new Command("verify", "驗證備份檔案") { pathArg };

        command.SetHandler(async (path) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null) { CliOutput.Error("找不到可用的連線設定。"); Environment.ExitCode = 2; return; }

            var service = Program.Services.GetRequiredService<IBackupService>();
            var connStr = cm.BuildConnectionString(profile);
            var result = await service.VerifyBackupAsync(connStr, path);

            if (CliOutput.JsonMode) CliOutput.Success(result);
            else if (result.IsValid) CliOutput.SuccessMessage("備份檔案有效");
            else { CliOutput.Error($"備份檔案無效：{result.ErrorMessage}"); Environment.ExitCode = 1; }
        }, pathArg);

        return command;
    }

    private static Command CreateInfoCommand()
    {
        var pathArg = new Argument<string>("path", "備份檔案路徑");
        var command = new Command("info", "查看備份檔案資訊") { pathArg };

        command.SetHandler(async (path) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null) { CliOutput.Error("找不到可用的連線設定。"); Environment.ExitCode = 2; return; }

            var service = Program.Services.GetRequiredService<IBackupService>();
            var connStr = cm.BuildConnectionString(profile);
            var info = await service.GetBackupFileInfoAsync(connStr, path);

            if (CliOutput.JsonMode) { CliOutput.Success(info); return; }

            var table = new Table().Title($"備份資訊：{path.EscapeMarkup()}");
            table.AddColumn("欄位"); table.AddColumn("值");
            table.AddRow("資料庫", info.DatabaseName.EscapeMarkup());
            table.AddRow("伺服器", info.ServerName.EscapeMarkup());
            table.AddRow("備份類型", info.BackupType.ToString());
            table.AddRow("開始時間", info.BackupStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            table.AddRow("完成時間", info.BackupFinishTime.ToString("yyyy-MM-dd HH:mm:ss"));
            table.AddRow("大小", info.FormattedBackupSize);
            table.AddRow("SQL Server", info.SqlServerVersion.EscapeMarkup());
            AnsiConsole.Write(table);
        }, pathArg);

        return command;
    }

    private static Command CreateHistoryCommand()
    {
        var command = new Command("history", "顯示備份歷史記錄");

        command.SetHandler(() =>
        {
            var service = Program.Services.GetRequiredService<IBackupService>();
            var history = service.GetBackupHistory();
            var backups = history.Backups.OrderByDescending(b => b.BackupTime).ToList();

            if (CliOutput.JsonMode) { CliOutput.Success(backups, backups.Count); return; }
            if (backups.Count == 0) { CliOutput.Info("沒有備份歷史記錄。"); return; }

            var table = new Table().Title("備份歷史");
            table.AddColumn("時間"); table.AddColumn("連線"); table.AddColumn("類型"); table.AddColumn("路徑"); table.AddColumn("大小");
            foreach (var b in backups)
            {
                table.AddRow(
                    b.BackupTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    (b.ConnectionName ?? "").EscapeMarkup(),
                    b.BackupType.ToString(),
                    b.BackupFilePath.EscapeMarkup(),
                    b.FormattedFileSize.EscapeMarkup());
            }
            AnsiConsole.Write(table);
        });

        return command;
    }
}
