using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 健康監控命令群組
/// </summary>
public static class HealthCommand
{
    public static Command Create()
    {
        var command = new Command("health", "健康監控");
        command.AddCommand(CreateStatusCommand());
        command.AddCommand(CreateMetricsCommand());
        command.AddCommand(CreateAlertsCommand());
        command.AddCommand(CreateInstallCommand());
        command.AddCommand(CreateUninstallCommand());
        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "健康狀態摘要");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IHealthMonitoringService>();

            // 先確認是否已安裝
            var installStatus = await service.GetInstallStatusAsync();
            if (!installStatus.IsFullyInstalled)
            {
                CliOutput.Warning("健康監控系統尚未安裝。使用 specurai health install 安裝。");
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Installed = false });
                return;
            }

            var summaries = await service.GetStatusSummaryAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(summaries, summaries.Count);
                return;
            }

            if (summaries.Count == 0) { CliOutput.Info("沒有健康狀態資料。"); return; }

            var table = new Table().Title("健康狀態摘要");
            table.AddColumn("檢查類型");
            table.AddColumn("正常");
            table.AddColumn("警告");
            table.AddColumn("嚴重");
            table.AddColumn("上次檢查");

            foreach (var s in summaries)
            {
                var status = s.OverallStatus switch
                {
                    "OK" => "[green]●[/]",
                    "WARNING" => "[yellow]●[/]",
                    "CRITICAL" => "[red]●[/]",
                    _ => "●"
                };
                table.AddRow(
                    $"{status} {s.CheckType.EscapeMarkup()}",
                    s.OkCount.ToString(),
                    s.WarningCount > 0 ? $"[yellow]{s.WarningCount}[/]" : "0",
                    s.CriticalCount > 0 ? $"[red]{s.CriticalCount}[/]" : "0",
                    s.LastCheckTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A");
            }

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateMetricsCommand()
    {
        var command = new Command("metrics", "目前健康指標");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IHealthMonitoringService>();
            var metrics = await service.GetCurrentMetricsAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(metrics, metrics.Count);
                return;
            }

            if (metrics.Count == 0) { CliOutput.Info("沒有健康指標資料。"); return; }

            var table = new Table().Title("健康指標");
            table.AddColumn("類型");
            table.AddColumn("指標");
            table.AddColumn("數值");
            table.AddColumn("閾值");
            table.AddColumn("狀態");

            foreach (var m in metrics)
            {
                var status = m.Status switch
                {
                    "OK" => "[green]正常[/]",
                    "WARNING" => "[yellow]警告[/]",
                    "CRITICAL" => "[red]嚴重[/]",
                    _ => m.Status
                };
                table.AddRow(
                    m.CheckType.EscapeMarkup(),
                    m.MetricName.EscapeMarkup(),
                    m.MetricValue?.ToString("N2") ?? "N/A",
                    m.ThresholdValue?.ToString("N2") ?? "N/A",
                    status);
            }

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateAlertsCommand()
    {
        var daysOption = new Option<int>("--days", () => 7, "查詢天數");
        var command = new Command("alerts", "警示記錄") { daysOption };

        command.SetHandler(async (days) =>
        {
            var service = Program.Services.GetRequiredService<IHealthMonitoringService>();
            var alerts = await service.GetRecentAlertsAsync(days);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(alerts, alerts.Count);
                return;
            }

            if (alerts.Count == 0) { CliOutput.Info($"最近 {days} 天沒有警示。"); return; }

            var table = new Table().Title($"警示記錄（最近 {days} 天）");
            table.AddColumn("時間");
            table.AddColumn("類型");
            table.AddColumn("狀態");
            table.AddColumn("訊息");

            foreach (var a in alerts)
            {
                var status = a.Status switch
                {
                    "WARNING" => "[yellow]警告[/]",
                    "CRITICAL" => "[red]嚴重[/]",
                    _ => a.Status
                };
                table.AddRow(
                    a.CheckTime.ToString("yyyy-MM-dd HH:mm"),
                    a.CheckType.EscapeMarkup(),
                    status,
                    (a.AlertMessage ?? "").EscapeMarkup());
            }

            AnsiConsole.Write(table);
        }, daysOption);

        return command;
    }

    private static Command CreateInstallCommand()
    {
        var command = new Command("install", "安裝健康監控系統");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IHealthMonitoringService>();

            var installStatus = await service.GetInstallStatusAsync();
            if (installStatus.IsFullyInstalled)
            {
                CliOutput.Warning("健康監控系統已經安裝。");
                return;
            }

            if (!CliOutput.JsonMode)
                CliOutput.Info("正在安裝健康監控系統...");

            var result = await service.InstallAsync();

            if (result.Success)
            {
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Message = "安裝完成" });
                else
                    CliOutput.SuccessMessage("健康監控系統安裝完成");
            }
            else
            {
                CliOutput.Error($"安裝失敗：{result.ErrorMessage}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }

    private static Command CreateUninstallCommand()
    {
        var keepDataOption = new Option<bool>("--keep-data", "保留歷史資料");
        var command = new Command("uninstall", "移除健康監控系統") { keepDataOption };

        command.SetHandler(async (keepData) =>
        {
            var service = Program.Services.GetRequiredService<IHealthMonitoringService>();

            if (!CliOutput.JsonMode)
            {
                if (!Spectre.Console.AnsiConsole.Confirm("確定要移除健康監控系統?", false))
                    return;
            }

            var options = new UninstallOptions(KeepHistoryData: keepData);

            var result = await service.UninstallAsync(options);

            if (result.Success)
            {
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Message = "移除完成", KeepData = keepData });
                else
                    CliOutput.SuccessMessage("健康監控系統已移除");
            }
            else
            {
                CliOutput.Error($"移除失敗：{result.ErrorMessage}");
                Environment.ExitCode = 1;
            }
        }, keepDataOption);

        return command;
    }
}
