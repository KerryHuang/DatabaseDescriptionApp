using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// Agent Job 管理命令群組
/// </summary>
public static class JobsCommand
{
    public static Command Create()
    {
        var command = new Command("jobs", "Agent Job 管理");
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateHistoryCommand());
        command.AddCommand(CreateStartCommand());
        command.AddCommand(CreateEnableCommand());
        command.AddCommand(CreateDisableCommand());
        command.AddCommand(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出 Specurai 管理的 Job");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IAgentJobService>();
            var jobs = await service.GetJobsAsync();

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(jobs, jobs.Count);
                return;
            }

            if (jobs.Count == 0) { CliOutput.Info("沒有 Specurai 管理的 Agent Job。"); return; }

            var table = new Table().Title("Agent Jobs");
            table.AddColumn("名稱");
            table.AddColumn("狀態");
            table.AddColumn("上次執行");
            table.AddColumn("結果");
            table.AddColumn("下次執行");

            foreach (var j in jobs)
            {
                var status = j.IsEnabled ? "[green]啟用[/]" : "[grey]停用[/]";
                var outcome = j.LastRunOutcomeText;
                table.AddRow(
                    j.Name.EscapeMarkup(),
                    status,
                    j.LastRunDate?.ToString("yyyy-MM-dd HH:mm") ?? "從未",
                    outcome.EscapeMarkup(),
                    j.NextRunDate?.ToString("yyyy-MM-dd HH:mm") ?? "N/A");
            }

            AnsiConsole.Write(table);
        });

        return command;
    }

    private static Command CreateHistoryCommand()
    {
        var jobIdArg = new Argument<string>("jobId", "Job ID (GUID)");
        var maxOption = new Option<int>("--max", () => 10, "最多顯示筆數");
        var command = new Command("history", "查看 Job 執行歷史") { jobIdArg, maxOption };

        command.SetHandler(async (jobIdStr, max) =>
        {
            if (!Guid.TryParse(jobIdStr, out var jobId))
            {
                CliOutput.Error("無效的 Job ID 格式。");
                Environment.ExitCode = 2;
                return;
            }

            var service = Program.Services.GetRequiredService<IAgentJobService>();
            var history = await service.GetJobHistoryAsync(jobId, max);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(history, history.Count);
                return;
            }

            if (history.Count == 0) { CliOutput.Info("沒有執行歷史。"); return; }

            var table = new Table().Title("執行歷史");
            table.AddColumn("時間");
            table.AddColumn("步驟");
            table.AddColumn("狀態");
            table.AddColumn("耗時(秒)");
            table.AddColumn("訊息");

            foreach (var h in history)
            {
                var status = h.RunStatusText;
                table.AddRow(
                    h.RunDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    h.StepName.EscapeMarkup(),
                    status.EscapeMarkup(),
                    h.DurationSeconds.ToString(),
                    (h.Message ?? "").EscapeMarkup());
            }

            AnsiConsole.Write(table);
        }, jobIdArg, maxOption);

        return command;
    }

    private static Command CreateStartCommand()
    {
        var jobIdArg = new Argument<string>("jobId", "Job ID (GUID)");
        var command = new Command("start", "立即執行 Job") { jobIdArg };

        command.SetHandler(async (jobIdStr) =>
        {
            if (!Guid.TryParse(jobIdStr, out var jobId))
            {
                CliOutput.Error("無效的 Job ID 格式。");
                Environment.ExitCode = 2;
                return;
            }

            var service = Program.Services.GetRequiredService<IAgentJobService>();
            await service.StartJobAsync(jobId);

            if (CliOutput.JsonMode)
                CliOutput.Success(new { JobId = jobId, Message = "Job 已啟動" });
            else
                CliOutput.SuccessMessage("Job 已啟動");
        }, jobIdArg);

        return command;
    }

    private static Command CreateEnableCommand()
    {
        var jobIdArg = new Argument<string>("jobId", "Job ID (GUID)");
        var command = new Command("enable", "啟用 Job") { jobIdArg };

        command.SetHandler(async (jobIdStr) =>
        {
            if (!Guid.TryParse(jobIdStr, out var jobId)) { CliOutput.Error("無效的 Job ID。"); Environment.ExitCode = 2; return; }
            var service = Program.Services.GetRequiredService<IAgentJobService>();
            await service.SetJobEnabledAsync(jobId, true);
            if (CliOutput.JsonMode) CliOutput.Success(new { JobId = jobId, Enabled = true });
            else CliOutput.SuccessMessage("Job 已啟用");
        }, jobIdArg);

        return command;
    }

    private static Command CreateDisableCommand()
    {
        var jobIdArg = new Argument<string>("jobId", "Job ID (GUID)");
        var command = new Command("disable", "停用 Job") { jobIdArg };

        command.SetHandler(async (jobIdStr) =>
        {
            if (!Guid.TryParse(jobIdStr, out var jobId)) { CliOutput.Error("無效的 Job ID。"); Environment.ExitCode = 2; return; }
            var service = Program.Services.GetRequiredService<IAgentJobService>();
            await service.SetJobEnabledAsync(jobId, false);
            if (CliOutput.JsonMode) CliOutput.Success(new { JobId = jobId, Enabled = false });
            else CliOutput.SuccessMessage("Job 已停用");
        }, jobIdArg);

        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var jobIdArg = new Argument<string>("jobId", "Job ID (GUID)");
        var command = new Command("delete", "刪除 Job") { jobIdArg };

        command.SetHandler(async (jobIdStr) =>
        {
            if (!Guid.TryParse(jobIdStr, out var jobId)) { CliOutput.Error("無效的 Job ID。"); Environment.ExitCode = 2; return; }

            if (!CliOutput.JsonMode && !AnsiConsole.Confirm("確定要刪除此 Job?", false))
                return;

            var service = Program.Services.GetRequiredService<IAgentJobService>();
            await service.DeleteJobAsync(jobId);
            if (CliOutput.JsonMode) CliOutput.Success(new { JobId = jobId, Message = "Job 已刪除" });
            else CliOutput.SuccessMessage("Job 已刪除");
        }, jobIdArg);

        return command;
    }
}
