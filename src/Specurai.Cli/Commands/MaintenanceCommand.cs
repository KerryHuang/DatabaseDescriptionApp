using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;

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
        command.AddCommand(CreateRunCommand());
        command.AddCommand(CreateSampleConfigCommand());
        return command;
    }

    private static Command CreateCheckCommand()
    {
        var command = new Command("check", "檢查 SQL Agent 前置條件");

        command.SetHandler(async () =>
        {
            var service = Program.Services.GetRequiredService<IMaintenancePlanService>();
            var (isReady, errorMessage) = await service.CheckPrerequisitesAsync();

            if (CliOutput.JsonMode) { CliOutput.Success(new { IsReady = isReady, ErrorMessage = errorMessage }); return; }
            if (isReady) CliOutput.SuccessMessage("SQL Agent 前置條件已滿足，可以建立維護計劃。");
            else { CliOutput.Error($"前置條件未滿足：{errorMessage}"); Environment.ExitCode = 1; }
        });

        return command;
    }

    private static Command CreatePreviewCommand()
    {
        var configArg = new Argument<string>("config", "維護計劃設定 JSON 檔案路徑");
        var command = new Command("preview", "預覽維護計劃 SQL") { configArg };

        command.SetHandler(async (configPath) =>
        {
            var config = LoadConfig(configPath);
            if (config == null) return;

            var service = Program.Services.GetRequiredService<IMaintenancePlanService>();
            var checks = await service.CheckStepsAsync(config);
            var sql = await service.GeneratePreviewSqlAsync(config, checks);

            if (CliOutput.JsonMode) CliOutput.Success(new { Sql = sql });
            else Console.WriteLine(sql);
        }, configArg);

        return command;
    }

    private static Command CreateRunCommand()
    {
        var configArg = new Argument<string>("config", "維護計劃設定 JSON 檔案路徑");
        var yesOption = new Option<bool>("--yes", "略過確認");
        var command = new Command("run", "執行維護計劃") { configArg, yesOption };

        command.SetHandler(async (configPath, yes) =>
        {
            var config = LoadConfig(configPath);
            if (config == null) return;

            if (!yes && !CliOutput.JsonMode &&
                !AnsiConsole.Confirm($"即將對資料庫 [{config.DatabaseName}] 執行維護計劃，是否繼續?", false))
                return;

            var service = Program.Services.GetRequiredService<IMaintenancePlanService>();
            var checks = await service.CheckStepsAsync(config);

            try
            {
                var progress = new Progress<string>(msg => CliOutput.Info(msg));
                await service.ExecutePlanAsync(config, checks, progress);
                if (CliOutput.JsonMode) CliOutput.Success(new { Database = config.DatabaseName, Message = "維護計劃執行完成" });
                else CliOutput.SuccessMessage("維護計劃執行完成");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"執行失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, configArg, yesOption);

        return command;
    }

    private static Command CreateSampleConfigCommand()
    {
        var pathArg = new Argument<string>("path", "輸出範例設定的檔案路徑");
        var command = new Command("sample-config", "產生範例設定 JSON") { pathArg };

        command.SetHandler((path) =>
        {
            var sample = new
            {
                databaseName = "MyDatabase",
                backupPath = "D:/Backup/",
                restorePath = "D:/Restore/",
                testDatabaseName = "MyDatabase_Test",
                loginName = "app_user",
                loginPassword = "ChangeMe!",
                backupTime = 30000,
                restoreTime = 40000,
                selectedSteps = Enum.GetNames<MaintenancePlanStep>(),
                retentionDays = 7,
                recoveryModel = "FULL",
                autoGrowthDataMB = 256,
                autoGrowthLogMB = 128,
                preExpandBufferGB = 5,
                checkDbHour = 3,
                checkDbDayOfWeek = "Sunday"
            };
            File.WriteAllText(path,
                JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));
            if (CliOutput.JsonMode) CliOutput.Success(new { Path = path });
            else CliOutput.SuccessMessage($"範例設定已寫入：{path}");
        }, pathArg);

        return command;
    }

    private static MaintenancePlanConfig? LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            CliOutput.Error($"找不到設定檔：{path}");
            Environment.ExitCode = 2;
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var raw = JsonSerializer.Deserialize<ConfigDto>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (raw == null) { CliOutput.Error("設定檔內容無法解析。"); Environment.ExitCode = 2; return null; }

            var steps = (raw.SelectedSteps ?? Array.Empty<string>())
                .Select(s => Enum.TryParse<MaintenancePlanStep>(s, true, out var v) ? (MaintenancePlanStep?)v : null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();

            var dow = Enum.TryParse<DayOfWeek>(raw.CheckDbDayOfWeek ?? "Sunday", true, out var d) ? d : DayOfWeek.Sunday;

            return new MaintenancePlanConfig
            {
                DatabaseName = raw.DatabaseName ?? "",
                BackupPath = raw.BackupPath ?? "",
                RestorePath = raw.RestorePath ?? "",
                TestDatabaseName = raw.TestDatabaseName ?? "",
                LoginName = raw.LoginName ?? "",
                LoginPassword = raw.LoginPassword ?? "",
                BackupTime = raw.BackupTime ?? 30000,
                RestoreTime = raw.RestoreTime ?? 40000,
                SelectedSteps = steps,
                RetentionDays = raw.RetentionDays ?? 7,
                RecoveryModel = raw.RecoveryModel ?? "FULL",
                AutoGrowthDataMB = raw.AutoGrowthDataMB ?? 256,
                AutoGrowthLogMB = raw.AutoGrowthLogMB ?? 128,
                PreExpandBufferGB = raw.PreExpandBufferGB ?? 5,
                CheckDbHour = raw.CheckDbHour ?? 3,
                CheckDbDayOfWeek = dow
            };
        }
        catch (Exception ex)
        {
            CliOutput.Error($"設定檔解析失敗：{ex.Message}");
            Environment.ExitCode = 2;
            return null;
        }
    }

    private sealed class ConfigDto
    {
        public string? DatabaseName { get; set; }
        public string? BackupPath { get; set; }
        public string? RestorePath { get; set; }
        public string? TestDatabaseName { get; set; }
        public string? LoginName { get; set; }
        public string? LoginPassword { get; set; }
        public int? BackupTime { get; set; }
        public int? RestoreTime { get; set; }
        public string[]? SelectedSteps { get; set; }
        public int? RetentionDays { get; set; }
        public string? RecoveryModel { get; set; }
        public int? AutoGrowthDataMB { get; set; }
        public int? AutoGrowthLogMB { get; set; }
        public int? PreExpandBufferGB { get; set; }
        public int? CheckDbHour { get; set; }
        public string? CheckDbDayOfWeek { get; set; }
    }
}
