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

        sb.AppendLine("USE [msdb];");
        sb.AppendLine();

        if (action == "刪除重建")
        {
            sb.AppendLine($"-- 刪除現有的 Job: [{jobName}]");
            sb.AppendLine($"IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'{escapedJobName}')");
            sb.AppendLine($"    EXEC dbo.sp_delete_job");
            sb.AppendLine($"        @job_name = N'{escapedJobName}',");
            sb.AppendLine($"        @delete_unused_schedule = 1;");
            sb.AppendLine();
        }

        // 建立 Job
        sb.AppendLine($"-- 建立 Job: [{jobName}]");
        sb.AppendLine($"EXEC dbo.sp_add_job");
        sb.AppendLine($"    @job_name    = N'{escapedJobName}',");
        sb.AppendLine($"    @enabled     = 1,");
        sb.AppendLine($"    @description = N'[TableSpec] 每日對 {dbName} 做完整備份，保留 {config.RetentionDays} 天';");
        sb.AppendLine();

        // 備份步驟命令（@command 內的字串用 '' 代表單引號）
        sb.AppendLine($"-- 新增 Step: Full Backup {dbName}");
        sb.AppendLine($"EXEC dbo.sp_add_jobstep");
        sb.AppendLine($"    @job_name       = N'{escapedJobName}',");
        sb.AppendLine($"    @step_name      = N'Full Backup {dbName}',");
        sb.AppendLine($"    @subsystem      = N'TSQL',");
        sb.AppendLine($"    @on_success_action = 1,");
        sb.AppendLine($"    @on_fail_action    = 2,");
        sb.AppendLine($"    @command = N'");
        sb.AppendLine($"BEGIN TRY");
        sb.AppendLine($"    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
        sb.AppendLine($"    DECLARE @fullPath  NVARCHAR(260) = N''{backupPath}{dbName}_FULL_'' + @today + ''.bak'';");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始：執行 FULL 備份到 '' + @fullPath + N''...'';");
        sb.AppendLine($"    BACKUP DATABASE [{config.DatabaseName}]");
        sb.AppendLine($"    TO DISK = @fullPath");
        sb.AppendLine($"    WITH NOFORMAT, INIT, NAME = N''{dbName}-完整 資料庫 備份'',");
        sb.AppendLine($"         SKIP, NOREWIND, NOUNLOAD, STATS = 10;");
        sb.AppendLine($"    PRINT N''FULL 備份完成'';");
        sb.AppendLine();
        sb.AppendLine($"    DECLARE @deleteday VARCHAR(8);");
        sb.AppendLine($"    SELECT @deleteday = CONVERT(VARCHAR(8), DATEADD(DAY, -{config.RetentionDays}, GETDATE()), 112);");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始刪除 {config.RetentionDays} 天前的 .bak：刪除日期 = '' + @deleteday + N''...'';");
        sb.AppendLine($"    EXEC master.dbo.xp_delete_file");
        sb.AppendLine($"        0,");
        sb.AppendLine($"        N''{backupPath}'',");
        sb.AppendLine($"        N''bak'',");
        sb.AppendLine($"        @deleteday,");
        sb.AppendLine($"        1;");
        sb.AppendLine($"    PRINT N''刪除過期備份完成'';");
        sb.AppendLine($"END TRY");
        sb.AppendLine($"BEGIN CATCH");
        sb.AppendLine($"    PRINT N''錯誤: '' + ERROR_MESSAGE();");
        sb.AppendLine($"    THROW;");
        sb.AppendLine($"END CATCH");
        sb.AppendLine($"';");
        sb.AppendLine();

        // 建立排程
        sb.AppendLine($"-- 建立排程: 每日 {config.BackupTime / 10000:D2}:{config.BackupTime % 10000 / 100:D2} 執行");
        sb.AppendLine($"EXEC dbo.sp_add_jobschedule");
        sb.AppendLine($"    @job_name          = N'{escapedJobName}',");
        sb.AppendLine($"    @name              = N'Nightly Full Backup Schedule',");
        sb.AppendLine($"    @freq_type         = 4,");
        sb.AppendLine($"    @freq_interval     = 1,");
        sb.AppendLine($"    @active_start_time = {config.BackupTime};");
        sb.AppendLine();

        // 指定本機執行
        sb.AppendLine($"-- 指定 Job 在本機伺服器執行");
        sb.AppendLine($"EXEC dbo.sp_add_jobserver");
        sb.AppendLine($"    @job_name = N'{escapedJobName}';");

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

        sb.AppendLine("USE [msdb];");
        sb.AppendLine();

        if (action == "刪除重建")
        {
            sb.AppendLine($"-- 刪除現有的 Job: [{jobName}]");
            sb.AppendLine($"IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'{escapedJobName}')");
            sb.AppendLine($"    EXEC dbo.sp_delete_job");
            sb.AppendLine($"        @job_name = N'{escapedJobName}',");
            sb.AppendLine($"        @delete_unused_schedule = 1;");
            sb.AppendLine();
        }

        // 建立 Job
        sb.AppendLine($"-- 建立 Job: [{jobName}]");
        sb.AppendLine($"EXEC dbo.sp_add_job");
        sb.AppendLine($"    @job_name    = N'{escapedJobName}',");
        sb.AppendLine($"    @enabled     = 1,");
        sb.AppendLine($"    @description = N'[TableSpec] 每日將 {dbName} 還原到 {testDbName}';");
        sb.AppendLine();

        // 還原步驟命令
        sb.AppendLine($"-- 新增 Step: Restore Full to {testDbName}");
        sb.AppendLine($"EXEC dbo.sp_add_jobstep");
        sb.AppendLine($"    @job_name       = N'{escapedJobName}',");
        sb.AppendLine($"    @step_name      = N'Restore Full to {testDbName}',");
        sb.AppendLine($"    @subsystem      = N'TSQL',");
        sb.AppendLine($"    @on_success_action = 1,");
        sb.AppendLine($"    @on_fail_action    = 2,");
        sb.AppendLine($"    @command = N'");
        sb.AppendLine($"BEGIN TRY");
        sb.AppendLine($"    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
        sb.AppendLine($"    DECLARE @fullPath  NVARCHAR(260) = N''{backupPath}{dbName}_FULL_'' + @today + ''.bak'';");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始：將 [{config.TestDatabaseName}] 設為 SINGLE_USER 並強制回滾...'';");
        sb.AppendLine($"    ALTER DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    SET SINGLE_USER");
        sb.AppendLine($"    WITH ROLLBACK IMMEDIATE;");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始執行還原到 [{config.TestDatabaseName}]，來源檔案 = '' + @fullPath + N''...'';");
        sb.AppendLine($"    RESTORE DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    FROM DISK = @fullPath");
        sb.AppendLine($"    WITH");
        sb.AppendLine($"      MOVE ''{dbName}_Data'' TO ''{restorePath}{testDbName}.mdf'',");
        sb.AppendLine($"      MOVE ''{dbName}_Log'' TO ''{restorePath}{testDbName}.ldf'',");
        sb.AppendLine($"      REPLACE,");
        sb.AppendLine($"      RECOVERY,");
        sb.AppendLine($"      STATS = 5;");
        sb.AppendLine($"    PRINT N''還原完成，開始切回 MULTI_USER...'';");
        sb.AppendLine();
        sb.AppendLine($"    ALTER DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    SET MULTI_USER;");
        sb.AppendLine($"    PRINT N''完成：已切回 MULTI_USER'';");
        sb.AppendLine($"END TRY");
        sb.AppendLine($"BEGIN CATCH");
        sb.AppendLine($"    PRINT N''錯誤: '' + ERROR_MESSAGE();");
        sb.AppendLine($"    THROW;");
        sb.AppendLine($"END CATCH");
        sb.AppendLine($"';");
        sb.AppendLine();

        // 建立排程
        sb.AppendLine($"-- 建立排程: 每日 {config.RestoreTime / 10000:D2}:{config.RestoreTime % 10000 / 100:D2} 執行");
        sb.AppendLine($"EXEC dbo.sp_add_jobschedule");
        sb.AppendLine($"    @job_name          = N'{escapedJobName}',");
        sb.AppendLine($"    @name              = N'Nightly Full Restore Schedule',");
        sb.AppendLine($"    @freq_type         = 4,");
        sb.AppendLine($"    @freq_interval     = 1,");
        sb.AppendLine($"    @active_start_time = {config.RestoreTime};");
        sb.AppendLine();

        // 指定本機執行
        sb.AppendLine($"-- 指定 Job 在本機伺服器執行");
        sb.AppendLine($"EXEC dbo.sp_add_jobserver");
        sb.AppendLine($"    @job_name = N'{escapedJobName}';");

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
