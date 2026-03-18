using System.Text;
using TableSpec.Application.Models;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// 維護計劃 SQL 產生器實作
/// </summary>
public class MaintenancePlanSqlGenerator : IMaintenancePlanSqlGenerator
{
    /// <inheritdoc/>
    public string GenerateStepSql(MaintenancePlanStep step, MaintenancePlanConfig config, string? action = null)
    {
        return step switch
        {
            MaintenancePlanStep.SetRecoveryModel => GenerateSetRecoveryModel(config),
            MaintenancePlanStep.RenameLogicalFiles => GenerateRenameLogicalFiles(config),
            MaintenancePlanStep.CreateLoginAndUser => GenerateCreateLoginAndUser(config, action),
            MaintenancePlanStep.AddToDbOwner => GenerateAddToDbOwner(config),
            MaintenancePlanStep.CreateBackupJob => GenerateCreateBackupJob(config, action),
            MaintenancePlanStep.CreateRestoreJob => GenerateCreateRestoreJob(config, action),
            _ => string.Empty
        };
    }

    /// <inheritdoc/>
    public string GenerateFullSql(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults)
    {
        var sb = new StringBuilder();
        var activeResults = checkResults.Where(r => r.SelectedAction != "跳過").ToList();

        if (!activeResults.Any())
            return string.Empty;

        // 步驟 1-4：包在交易中
        var transactionSteps = activeResults
            .Where(r => r.Step is MaintenancePlanStep.SetRecoveryModel
                or MaintenancePlanStep.RenameLogicalFiles
                or MaintenancePlanStep.CreateLoginAndUser
                or MaintenancePlanStep.AddToDbOwner)
            .ToList();

        if (transactionSteps.Any())
        {
            sb.AppendLine("-- ===== 基本設定（步驟 1-4）=====");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();

            foreach (var result in transactionSteps)
            {
                sb.AppendLine($"PRINT N'正在執行：{GetStepDescription(result.Step)}...';");
                sb.AppendLine(GenerateStepSql(result.Step, config, result.SelectedAction));
                sb.AppendLine();
            }

            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("PRINT N'基本設定完成。';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH");
            sb.AppendLine();
        }

        // 步驟 5：備份排程
        var backupStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateBackupJob);
        if (backupStep is not null)
        {
            sb.AppendLine("-- ===== 建立備份排程（步驟 5）=====");
            sb.AppendLine($"PRINT N'正在執行：{GetStepDescription(backupStep.Step)}...';");
            sb.AppendLine(GenerateStepSql(backupStep.Step, config, backupStep.SelectedAction));
            sb.AppendLine();
        }

        // 步驟 6：還原排程
        var restoreStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob);
        if (restoreStep is not null)
        {
            sb.AppendLine("-- ===== 建立還原排程（步驟 6）=====");
            sb.AppendLine($"PRINT N'正在執行：{GetStepDescription(restoreStep.Step)}...';");
            sb.AppendLine(GenerateStepSql(restoreStep.Step, config, restoreStep.SelectedAction));
            sb.AppendLine();
        }

        sb.AppendLine("PRINT N'維護計劃設定完成。';");
        return sb.ToString();
    }

    private static string GenerateSetRecoveryModel(MaintenancePlanConfig config)
    {
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);
        return $"""
            USE [master];
            ALTER DATABASE {db} SET RECOVERY SIMPLE WITH NO_WAIT;
            ALTER DATABASE {testDb} SET RECOVERY SIMPLE WITH NO_WAIT;
            """;
    }

    private static string GenerateRenameLogicalFiles(MaintenancePlanConfig config)
    {
        var db = QuoteName(config.DatabaseName);
        var dbName = EscapeSingleQuote(config.DatabaseName);
        return $"""
            USE [master];
            IF EXISTS (SELECT 1 FROM sys.master_files WHERE database_id = DB_ID(N'{dbName}') AND name = N'shltw_Data')
                ALTER DATABASE {db} MODIFY FILE (NAME = N'shltw_Data', NEWNAME = N'{dbName}_Data');
            IF EXISTS (SELECT 1 FROM sys.master_files WHERE database_id = DB_ID(N'{dbName}') AND name = N'shltw_Log')
                ALTER DATABASE {db} MODIFY FILE (NAME = N'shltw_Log', NEWNAME = N'{dbName}_Log');
            """;
    }

    private static string GenerateCreateLoginAndUser(MaintenancePlanConfig config, string? action)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);
        var login = QuoteName(config.LoginName);
        var escapedPassword = EscapeSingleQuote(config.LoginPassword);
        var escapedLogin = EscapeSingleQuote(config.LoginName);
        var dbName = EscapeSingleQuote(config.DatabaseName);

        sb.AppendLine("USE [master];");

        if (action == "刪除重建")
        {
            sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{escapedLogin}')");
            sb.AppendLine($"    DROP LOGIN {login};");
        }

        sb.AppendLine($"CREATE LOGIN {login} WITH PASSWORD = N'{escapedPassword}', DEFAULT_DATABASE = {db}, CHECK_EXPIRATION = OFF, CHECK_POLICY = OFF;");
        sb.AppendLine();

        // 主資料庫使用者
        sb.AppendLine($"USE {db};");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{escapedLogin}')");
        sb.AppendLine($"    ALTER USER {login} WITH LOGIN = {login};");
        sb.AppendLine("ELSE");
        sb.AppendLine($"    CREATE USER {login} FOR LOGIN {login};");
        sb.AppendLine();

        // 測試資料庫使用者
        sb.AppendLine($"USE {testDb};");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{escapedLogin}')");
        sb.AppendLine($"    ALTER USER {login} WITH LOGIN = {login};");
        sb.AppendLine("ELSE");
        sb.AppendLine($"    CREATE USER {login} FOR LOGIN {login};");

        return sb.ToString();
    }

    private static string GenerateAddToDbOwner(MaintenancePlanConfig config)
    {
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);
        var login = QuoteName(config.LoginName);
        return $"""
            USE {db};
            ALTER ROLE [db_owner] ADD MEMBER {login};
            USE {testDb};
            ALTER ROLE [db_owner] ADD MEMBER {login};
            """;
    }

    private static string GenerateCreateBackupJob(MaintenancePlanConfig config, string? action)
    {
        var sb = new StringBuilder();
        var dbName = EscapeSingleQuote(config.DatabaseName);
        var jobName = $"{config.DatabaseName}_FullBackup";
        var escapedJobName = EscapeSingleQuote(jobName);
        var backupPath = EscapeSingleQuote(config.BackupPath);

        if (action == "刪除重建")
        {
            sb.AppendLine($"EXEC msdb.dbo.sp_delete_job @job_name = N'{escapedJobName}', @delete_unused_schedule = 1;");
            sb.AppendLine();
        }

        sb.AppendLine($"EXEC msdb.dbo.sp_add_job @job_name = N'{escapedJobName}', @enabled = 1, @description = N'[TableSpec] 每日全備份 {dbName}';");
        sb.AppendLine();

        // 備份步驟命令（使用 DECLARE 變數避免路徑中的特殊字元問題）
        // 注意：@command 內的字串用 '' 代表單引號，不可再次 EscapeSingleQuote
        var backupCmd =
            $"DECLARE @today NVARCHAR(8) = CONVERT(VARCHAR(8), GETDATE(), 112);\r\n" +
            $"DECLARE @fullPath NVARCHAR(260) = N''{backupPath}{dbName}_FULL_'' + @today + ''.bak'';\r\n" +
            $"BACKUP DATABASE [{config.DatabaseName}] TO DISK = @fullPath WITH NOFORMAT, INIT, NAME = N''{dbName}-完整備份'', SKIP, NOREWIND, NOUNLOAD, STATS = 10;\r\n" +
            $"DECLARE @deleteday VARCHAR(8) = CONVERT(VARCHAR(8), DATEADD(DAY, -{config.RetentionDays}, GETDATE()), 112);\r\n" +
            $"EXEC master.dbo.xp_delete_file 0, N''{backupPath}'', N''bak'', @deleteday, 1;";

        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobstep @job_name = N'{escapedJobName}', @step_name = N'全備份', @subsystem = N'TSQL', @command = N'{backupCmd}';");
        sb.AppendLine();
        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobschedule @job_name = N'{escapedJobName}', @name = N'{escapedJobName}_Schedule', @freq_type = 4, @freq_interval = 1, @active_start_time = {config.BackupTime};");
        sb.AppendLine();
        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobserver @job_name = N'{escapedJobName}';");

        return sb.ToString();
    }

    private static string GenerateCreateRestoreJob(MaintenancePlanConfig config, string? action)
    {
        var sb = new StringBuilder();
        var dbName = EscapeSingleQuote(config.DatabaseName);
        var testDbName = EscapeSingleQuote(config.TestDatabaseName);
        var jobName = $"{config.DatabaseName}_FullRestore";
        var escapedJobName = EscapeSingleQuote(jobName);
        var restorePath = EscapeSingleQuote(config.RestorePath);
        var backupPath = EscapeSingleQuote(config.BackupPath);

        if (action == "刪除重建")
        {
            sb.AppendLine($"EXEC msdb.dbo.sp_delete_job @job_name = N'{escapedJobName}', @delete_unused_schedule = 1;");
            sb.AppendLine();
        }

        sb.AppendLine($"EXEC msdb.dbo.sp_add_job @job_name = N'{escapedJobName}', @enabled = 1, @description = N'[TableSpec] 每日全還原 {dbName}';");
        sb.AppendLine();

        // 還原步驟命令（使用 DECLARE 變數，不可再次 EscapeSingleQuote）
        var restoreCmd =
            $"DECLARE @today NVARCHAR(8) = CONVERT(VARCHAR(8), GETDATE(), 112);\r\n" +
            $"DECLARE @fullPath NVARCHAR(260) = N''{backupPath}{dbName}_FULL_'' + @today + ''.bak'';\r\n" +
            $"ALTER DATABASE [{config.TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\r\n" +
            $"RESTORE DATABASE [{config.TestDatabaseName}] FROM DISK = @fullPath " +
            $"WITH MOVE N''{dbName}_Data'' TO N''{restorePath}{testDbName}.mdf'', MOVE N''{dbName}_Log'' TO N''{restorePath}{testDbName}.ldf'', REPLACE, RECOVERY, STATS = 5;\r\n" +
            $"ALTER DATABASE [{config.TestDatabaseName}] SET MULTI_USER;";

        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobstep @job_name = N'{escapedJobName}', @step_name = N'全還原', @subsystem = N'TSQL', @command = N'{restoreCmd}';");
        sb.AppendLine();
        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobschedule @job_name = N'{escapedJobName}', @name = N'{escapedJobName}_Schedule', @freq_type = 4, @freq_interval = 1, @active_start_time = {config.RestoreTime};");
        sb.AppendLine();
        sb.AppendLine($"EXEC msdb.dbo.sp_add_jobserver @job_name = N'{escapedJobName}';");

        return sb.ToString();
    }

    private static string EscapeSingleQuote(string value) => value.Replace("'", "''");

    private static string QuoteName(string name) => $"[{name.Replace("]", "]]")}]";

    private static string GetStepDescription(MaintenancePlanStep step) => step switch
    {
        MaintenancePlanStep.SetRecoveryModel => "設定 Recovery Model",
        MaintenancePlanStep.RenameLogicalFiles => "重新命名邏輯檔名",
        MaintenancePlanStep.CreateLoginAndUser => "建立登入帳號與使用者",
        MaintenancePlanStep.AddToDbOwner => "加入 db_owner 角色",
        MaintenancePlanStep.CreateBackupJob => "建立備份排程",
        MaintenancePlanStep.CreateRestoreJob => "建立還原排程",
        _ => step.ToString()
    };
}
