using System.Text;
using Specurai.Application.Models;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;

namespace Specurai.Infrastructure.Services;

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
            MaintenancePlanStep.SetCompatibilityLevel => GenerateSetCompatibilityLevel(config),
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

        // 獨立步驟：更新相容性層級（ALTER DATABASE 不能在交易中執行）
        var compatStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.SetCompatibilityLevel);
        if (compatStep is not null)
        {
            sb.AppendLine($"PRINT N'===== 更新相容性層級 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine(GenerateStepSql(compatStep.Step, config, compatStep.SelectedAction));
            sb.AppendLine($"    PRINT N'===== 更新相容性層級 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N'##### 更新相容性層級發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 基本設定步驟：包在交易中
        var transactionSteps = activeResults
            .Where(r => r.Step is MaintenancePlanStep.SetRecoveryModel
                or MaintenancePlanStep.RenameLogicalFiles
                or MaintenancePlanStep.CreateLoginAndUser
                or MaintenancePlanStep.AddToDbOwner)
            .ToList();

        if (transactionSteps.Any())
        {
            sb.AppendLine($"PRINT N'===== 基本設定 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();

            foreach (var result in transactionSteps)
            {
                sb.AppendLine(GenerateStepSql(result.Step, config, result.SelectedAction));
                sb.AppendLine();
            }

            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine($"    PRINT N'===== 基本設定 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N'##### 基本設定發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 5：備份排程
        var backupStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateBackupJob);
        if (backupStep is not null)
        {
            sb.AppendLine($"PRINT N'===== 建立備份排程 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine(GenerateStepSql(backupStep.Step, config, backupStep.SelectedAction));
            sb.AppendLine($"    PRINT N'===== 建立備份排程 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N'##### 建立備份排程發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 6：還原排程
        var restoreStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob);
        if (restoreStep is not null)
        {
            sb.AppendLine($"PRINT N'===== 建立還原排程 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine(GenerateStepSql(restoreStep.Step, config, restoreStep.SelectedAction));
            sb.AppendLine($"    PRINT N'===== 建立還原排程 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N'##### 建立還原排程發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        sb.AppendLine("PRINT N'維護計劃設定完成。';");
        return sb.ToString();
    }

    private static string GenerateSetCompatibilityLevel(MaintenancePlanConfig config)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var dbName = EscapeSingleQuote(config.DatabaseName);

        sb.AppendLine("    -- 取得當前 SQL Server 版本對應的相容性層級");
        sb.AppendLine("    DECLARE @serverLevel INT = CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) * 10;");
        sb.AppendLine("    DECLARE @currentLevel INT;");
        sb.AppendLine($"    SELECT @currentLevel = compatibility_level FROM sys.databases WHERE name = N'{dbName}';");
        sb.AppendLine();
        sb.AppendLine("    IF @currentLevel < @serverLevel");
        sb.AppendLine("    BEGIN");
        sb.AppendLine($"        PRINT N'更新 {db} 的相容性層級：' + CAST(@currentLevel AS NVARCHAR) + N' → ' + CAST(@serverLevel AS NVARCHAR);");
        sb.AppendLine($"        DECLARE @compatSql NVARCHAR(200) = N'ALTER DATABASE {db} SET COMPATIBILITY_LEVEL = ' + CAST(@serverLevel AS NVARCHAR);");
        sb.AppendLine("        EXEC sp_executesql @compatSql;");
        sb.AppendLine($"        PRINT N'相容性層級更新完成';");
        sb.AppendLine("    END");
        sb.AppendLine("    ELSE");
        sb.AppendLine("    BEGIN");
        sb.AppendLine($"        PRINT N'{db} 的相容性層級已為最新（' + CAST(@currentLevel AS NVARCHAR) + N'），無需更新';");
        sb.AppendLine("    END");

        return sb.ToString();
    }

    private static string GenerateSetRecoveryModel(MaintenancePlanConfig config)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);

        sb.AppendLine("    -- 切換到 master 資料庫");
        sb.AppendLine("    PRINT N'1.1 切換到 master 資料庫...';");
        sb.AppendLine("    USE [master];");
        sb.AppendLine($"    -- 設定主要資料庫的 Recovery Model 為 SIMPLE");
        sb.AppendLine($"    PRINT N'1.2 將資料庫 {db} 的 Recovery Model 設為 SIMPLE...';");
        sb.AppendLine($"    ALTER DATABASE {db}");
        sb.AppendLine($"    SET RECOVERY SIMPLE");
        sb.AppendLine($"    WITH NO_WAIT;");
        sb.AppendLine($"    PRINT N'1.2 完成：主要資料庫 {db} 設定為 SIMPLE 模式';");
        sb.AppendLine();
        sb.AppendLine($"    -- 設定測試資料庫的 Recovery Model 為 SIMPLE");
        sb.AppendLine($"    PRINT N'1.3 將資料庫 {testDb} 的 Recovery Model 設為 SIMPLE...';");
        sb.AppendLine($"    ALTER DATABASE {testDb}");
        sb.AppendLine($"    SET RECOVERY SIMPLE");
        sb.AppendLine($"    WITH NO_WAIT;");
        sb.AppendLine($"    PRINT N'1.3 完成：測試資料庫 {testDb} 設定為 SIMPLE 模式';");

        return sb.ToString();
    }

    private static string GenerateRenameLogicalFiles(MaintenancePlanConfig config)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var dbName = EscapeSingleQuote(config.DatabaseName);

        sb.AppendLine("    USE [master];");
        sb.AppendLine($"    DECLARE @dbName SYSNAME = N'{dbName}';");
        sb.AppendLine();
        sb.AppendLine($"    -- 3.1 如果存在原本的邏輯資料檔 'shltw_Data'，將其改為 '{dbName}_Data'");
        sb.AppendLine($"    PRINT N'3.1 檢查並重新命名邏輯資料檔 (Data)...';");
        sb.AppendLine($"    IF EXISTS (");
        sb.AppendLine($"        SELECT 1");
        sb.AppendLine($"        FROM sys.master_files");
        sb.AppendLine($"        WHERE database_id = DB_ID(@dbName)");
        sb.AppendLine($"          AND name = N'shltw_Data'");
        sb.AppendLine($"    )");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'3.1-1 重新命名邏輯 Data 檔: ''shltw_Data'' → ''{dbName}_Data''';");
        sb.AppendLine($"        ALTER DATABASE {db}");
        sb.AppendLine($"        MODIFY FILE (");
        sb.AppendLine($"          NAME    = N'shltw_Data',");
        sb.AppendLine($"          NEWNAME = N'{dbName}_Data'");
        sb.AppendLine($"        );");
        sb.AppendLine($"        PRINT N'3.1-2 完成 Data 檔重新命名';");
        sb.AppendLine($"    END");
        sb.AppendLine($"    ELSE");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'3.1-3 資料檔 ''shltw_Data'' 不存在或已被更名';");
        sb.AppendLine($"    END");
        sb.AppendLine();
        sb.AppendLine($"    -- 3.2 如果存在原本的邏輯日誌檔 'shltw_Log'，將其改為 '{dbName}_Log'");
        sb.AppendLine($"    PRINT N'3.2 檢查並重新命名邏輯日誌檔 (Log)...';");
        sb.AppendLine($"    IF EXISTS (");
        sb.AppendLine($"        SELECT 1");
        sb.AppendLine($"        FROM sys.master_files");
        sb.AppendLine($"        WHERE database_id = DB_ID(@dbName)");
        sb.AppendLine($"          AND name = N'shltw_Log'");
        sb.AppendLine($"    )");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'3.2-1 重新命名邏輯 Log 檔: ''shltw_Log'' → ''{dbName}_Log''';");
        sb.AppendLine($"        ALTER DATABASE {db}");
        sb.AppendLine($"        MODIFY FILE (");
        sb.AppendLine($"          NAME    = N'shltw_Log',");
        sb.AppendLine($"          NEWNAME = N'{dbName}_Log'");
        sb.AppendLine($"        );");
        sb.AppendLine($"        PRINT N'3.2-2 完成 Log 檔重新命名';");
        sb.AppendLine($"    END");
        sb.AppendLine($"    ELSE");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'3.2-3 日誌檔 ''shltw_Log'' 不存在或已被更名';");
        sb.AppendLine($"    END");

        return sb.ToString();
    }

    private static string GenerateCreateLoginAndUser(MaintenancePlanConfig config, string? action)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);
        var login = QuoteName(config.LoginName);
        var escapedPassword = EscapeSingleQuote(config.LoginPassword);
        var escapedLogin = EscapeSingleQuote(config.LoginName);

        sb.AppendLine("    -- 切換到 master");
        sb.AppendLine("    PRINT N'5.1 切換到 master 資料庫...';");
        sb.AppendLine("    USE [master];");
        sb.AppendLine();

        if (action == "刪除重建")
        {
            sb.AppendLine($"    -- 刪除現有登入帳號");
            sb.AppendLine($"    IF EXISTS (");
            sb.AppendLine($"        SELECT 1 FROM sys.server_principals WHERE name = N'{escapedLogin}'");
            sb.AppendLine($"    )");
            sb.AppendLine($"    BEGIN");
            sb.AppendLine($"        PRINT N'5.1-1 刪除現有登入帳號 {login}...';");
            sb.AppendLine($"        DROP LOGIN {login};");
            sb.AppendLine($"        PRINT N'5.1-2 刪除完成';");
            sb.AppendLine($"    END");
            sb.AppendLine();
        }

        sb.AppendLine($"    -- 建立 SQL Server 登入帳號 {login}");
        sb.AppendLine($"    PRINT N'5.2 建立 SQL Server 登入帳號 {login}...';");
        sb.AppendLine($"    IF NOT EXISTS (");
        sb.AppendLine($"        SELECT 1 FROM sys.server_principals WHERE name = N'{escapedLogin}'");
        sb.AppendLine($"    )");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        CREATE LOGIN {login} WITH");
        sb.AppendLine($"            PASSWORD = '{escapedPassword}',");
        sb.AppendLine($"            DEFAULT_DATABASE = {db},");
        sb.AppendLine($"            CHECK_EXPIRATION = OFF,");
        sb.AppendLine($"            CHECK_POLICY = OFF;");
        sb.AppendLine($"        PRINT N'5.2-1 登入帳號 {login} 建立成功';");
        sb.AppendLine($"    END");
        sb.AppendLine($"    ELSE");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'5.2-2 登入帳號 {login} 已存在，跳過建立';");
        sb.AppendLine($"    END");
        sb.AppendLine();

        // 主資料庫使用者
        sb.AppendLine($"    -- 切換到目標資料庫");
        sb.AppendLine($"    PRINT N'5.3 切換到資料庫 {db}...';");
        sb.AppendLine($"    USE {db};");
        sb.AppendLine();
        sb.AppendLine($"    -- 檢查 database user 是否已存在，若不存在則建立；若已存在則重新綁定");
        sb.AppendLine($"    PRINT N'5.4 檢查資料庫使用者 {login} 是否存在...';");
        sb.AppendLine($"    IF NOT EXISTS (");
        sb.AppendLine($"        SELECT 1");
        sb.AppendLine($"        FROM sys.database_principals");
        sb.AppendLine($"        WHERE name = N'{escapedLogin}'");
        sb.AppendLine($"    )");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'5.4-1 資料庫使用者 {login} 不存在，開始建立...';");
        sb.AppendLine($"        CREATE USER {login} FOR LOGIN {login};");
        sb.AppendLine($"        PRINT N'5.4-2 資料庫使用者 {login} 建立成功';");
        sb.AppendLine($"    END");
        sb.AppendLine($"    ELSE");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'5.4-3 資料庫使用者 {login} 已存在，重新綁定至 LOGIN {login}...';");
        sb.AppendLine($"        ALTER USER {login} WITH LOGIN = {login};");
        sb.AppendLine($"        PRINT N'5.4-4 資料庫使用者 {login} 重新綁定完成';");
        sb.AppendLine($"    END");
        sb.AppendLine();

        // 測試資料庫使用者
        sb.AppendLine($"    -- 切換到目標測試資料庫");
        sb.AppendLine($"    PRINT N'5.5 切換到測試資料庫 {testDb}...';");
        sb.AppendLine($"    USE {testDb};");
        sb.AppendLine();
        sb.AppendLine($"    -- 檢查 database user 是否已存在，若不存在則建立；若已存在則重新綁定");
        sb.AppendLine($"    PRINT N'5.6 檢查測試資料庫使用者 {login} 是否存在...';");
        sb.AppendLine($"    IF NOT EXISTS (");
        sb.AppendLine($"        SELECT 1");
        sb.AppendLine($"        FROM sys.database_principals");
        sb.AppendLine($"        WHERE name = N'{escapedLogin}'");
        sb.AppendLine($"    )");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'5.6-1 測試資料庫使用者 {login} 不存在，開始建立...';");
        sb.AppendLine($"        CREATE USER {login} FOR LOGIN {login};");
        sb.AppendLine($"        PRINT N'5.6-2 測試資料庫使用者 {login} 建立成功';");
        sb.AppendLine($"    END");
        sb.AppendLine($"    ELSE");
        sb.AppendLine($"    BEGIN");
        sb.AppendLine($"        PRINT N'5.6-3 測試資料庫使用者 {login} 已存在，重新綁定至 LOGIN {login}...';");
        sb.AppendLine($"        ALTER USER {login} WITH LOGIN = {login};");
        sb.AppendLine($"        PRINT N'5.6-4 測試資料庫使用者 {login} 重新綁定完成';");
        sb.AppendLine($"    END");

        return sb.ToString();
    }

    private static string GenerateAddToDbOwner(MaintenancePlanConfig config)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        var testDb = QuoteName(config.TestDatabaseName);
        var login = QuoteName(config.LoginName);

        sb.AppendLine($"    USE {db};");
        sb.AppendLine($"    ALTER ROLE [db_owner]");
        sb.AppendLine($"    ADD MEMBER {login};");
        sb.AppendLine();
        sb.AppendLine($"    USE {testDb};");
        sb.AppendLine($"    ALTER ROLE [db_owner]");
        sb.AppendLine($"    ADD MEMBER {login};");
        sb.AppendLine($"    PRINT N'使用者 {login} 已成功加入 db_owner 角色';");

        return sb.ToString();
    }

    private static string GenerateCreateBackupJob(MaintenancePlanConfig config, string? action)
    {
        var sb = new StringBuilder();
        var dbName = EscapeSingleQuote(config.DatabaseName);
        var jobName = $"{config.DatabaseName}_{config.RecoveryModel}Backup";
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
        sb.AppendLine($"    @description = N'[Specurai] 每日對 {dbName} 做完整備份，保留 {config.RetentionDays} 天';");
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
        sb.AppendLine($"    @name              = N'{EscapeSingleQuote(jobName)}_Schedule',");
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
        sb.AppendLine($"    @description = N'[Specurai] 每日將 {dbName} 還原到 {testDbName}';");
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
        sb.AppendLine($"    @name              = N'{EscapeSingleQuote(jobName)}_Schedule',");
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

    /// <inheritdoc/>
    public string GenerateExportSql(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults)
    {
        var sb = new StringBuilder();
        var activeResults = checkResults.Where(r => r.SelectedAction != "跳過").ToList();

        if (!activeResults.Any())
            return string.Empty;

        // 檔頭說明
        sb.AppendLine("-- ============================================================");
        sb.AppendLine("-- Specurai 維護計劃腳本");
        sb.AppendLine($"-- 產生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- 說明: 此腳本由 Specurai 自動產生，您可以修改下方變數後直接執行");
        sb.AppendLine("-- ============================================================");
        sb.AppendLine();

        // 變數宣告區
        sb.AppendLine("-- ============================================================");
        sb.AppendLine("-- 變數設定區（請依需求修改）");
        sb.AppendLine("-- ============================================================");
        sb.AppendLine();
        sb.AppendLine("-- 主要資料庫名稱");
        sb.AppendLine($"DECLARE @DatabaseName NVARCHAR(128) = N'{EscapeSingleQuote(config.DatabaseName)}';");
        sb.AppendLine();
        sb.AppendLine("-- 測試資料庫名稱（用於還原驗證）");
        sb.AppendLine($"DECLARE @TestDatabaseName NVARCHAR(128) = N'{EscapeSingleQuote(config.TestDatabaseName)}';");
        sb.AppendLine();
        sb.AppendLine("-- 備份存放路徑（結尾需包含斜線）");
        sb.AppendLine($"DECLARE @BackupPath NVARCHAR(260) = N'{EscapeSingleQuote(config.BackupPath)}';");
        sb.AppendLine();
        sb.AppendLine("-- 還原資料檔路徑（結尾需包含斜線）");
        sb.AppendLine($"DECLARE @RestorePath NVARCHAR(260) = N'{EscapeSingleQuote(config.RestorePath)}';");
        sb.AppendLine();
        sb.AppendLine("-- SQL Server 登入帳號名稱");
        sb.AppendLine($"DECLARE @LoginName NVARCHAR(128) = N'{EscapeSingleQuote(config.LoginName)}';");
        sb.AppendLine();
        sb.AppendLine("-- SQL Server 登入密碼");
        sb.AppendLine($"DECLARE @LoginPassword NVARCHAR(128) = N'{EscapeSingleQuote(config.LoginPassword)}';");
        sb.AppendLine();
        sb.AppendLine("-- 備份保留天數（超過此天數的備份檔將自動刪除）");
        sb.AppendLine($"DECLARE @RetentionDays INT = {config.RetentionDays};");
        sb.AppendLine();
        sb.AppendLine("-- 每日備份排程時間（HHMMSS 格式，例如 20000 = 02:00:00）");
        sb.AppendLine($"DECLARE @BackupTime INT = {config.BackupTime};");
        sb.AppendLine();
        sb.AppendLine("-- 每日還原排程時間（HHMMSS 格式，例如 30000 = 03:00:00）");
        sb.AppendLine($"DECLARE @RestoreTime INT = {config.RestoreTime};");
        sb.AppendLine();
        sb.AppendLine("-- 資料庫復原模式（用於備份 Job 命名）");
        sb.AppendLine($"DECLARE @RecoveryModel NVARCHAR(20) = N'{EscapeSingleQuote(config.RecoveryModel)}';");
        sb.AppendLine();

        int stepNumber = 0;

        // 更新相容性層級
        var compatStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.SetCompatibilityLevel);
        if (compatStep is not null)
        {
            stepNumber++;
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 更新相容性層級");
            sb.AppendLine("-- 說明: 將資料庫的相容性層級更新至當前 SQL Server 版本，");
            sb.AppendLine("--       以啟用最新的查詢最佳化和語法功能");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 更新相容性層級 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @compatLevel INT = CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) * 10;");
            sb.AppendLine("    DECLARE @compatSql NVARCHAR(MAX);");
            sb.AppendLine("    DECLARE @currentCompat INT;");
            sb.AppendLine();
            sb.AppendLine("    SELECT @currentCompat = compatibility_level FROM sys.databases WHERE name = @DatabaseName;");
            sb.AppendLine();
            sb.AppendLine("    IF @currentCompat < @compatLevel");
            sb.AppendLine("    BEGIN");
            sb.AppendLine("        SET @compatSql = N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' SET COMPATIBILITY_LEVEL = ' + CAST(@compatLevel AS NVARCHAR) + N';';");
            sb.AppendLine("        PRINT N'  更新 ' + @DatabaseName + N' 的相容性層級：' + CAST(@currentCompat AS NVARCHAR) + N' → ' + CAST(@compatLevel AS NVARCHAR);");
            sb.AppendLine("        EXEC sp_executesql @compatSql;");
            sb.AppendLine("        PRINT N'  相容性層級更新完成';");
            sb.AppendLine("    END");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        PRINT N'  相容性層級已為最新（' + CAST(@currentCompat AS NVARCHAR) + N'），無需更新';");
            sb.AppendLine();
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 更新相容性層級 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 設定 Recovery Model
        var recoveryStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.SetRecoveryModel);
        if (recoveryStep is not null)
        {
            stepNumber++;
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 設定 Recovery Model");
            sb.AppendLine("-- 說明: 將主資料庫和測試資料庫的 Recovery Model 設為 SIMPLE，");
            sb.AppendLine("--       以減少交易記錄空間佔用");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 設定 Recovery Model (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @sql NVARCHAR(MAX);");
            sb.AppendLine();
            sb.AppendLine("    -- 設定主資料庫");
            sb.AppendLine("    SET @sql = N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' SET RECOVERY SIMPLE WITH NO_WAIT;';");
            sb.AppendLine("    PRINT N'  設定 ' + @DatabaseName + N' 為 SIMPLE 模式...';");
            sb.AppendLine("    EXEC sp_executesql @sql;");
            sb.AppendLine();
            sb.AppendLine("    -- 設定測試資料庫");
            sb.AppendLine("    SET @sql = N'ALTER DATABASE ' + QUOTENAME(@TestDatabaseName) + N' SET RECOVERY SIMPLE WITH NO_WAIT;';");
            sb.AppendLine("    PRINT N'  設定 ' + @TestDatabaseName + N' 為 SIMPLE 模式...';");
            sb.AppendLine("    EXEC sp_executesql @sql;");
            sb.AppendLine();
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 設定 Recovery Model (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 2: 重新命名邏輯檔名
        var renameStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.RenameLogicalFiles);
        if (renameStep is not null)
        {
            stepNumber++;
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 重新命名邏輯檔名");
            sb.AppendLine("-- 說明: 將舊的邏輯檔名 (shltw_Data/shltw_Log) 更名為目前資料庫名稱");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 重新命名邏輯檔名 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @renameSql NVARCHAR(MAX);");
            sb.AppendLine("    USE [master];");
            sb.AppendLine();
            sb.AppendLine("    -- 重新命名邏輯資料檔");
            sb.AppendLine("    IF EXISTS (");
            sb.AppendLine("        SELECT 1 FROM sys.master_files");
            sb.AppendLine("        WHERE database_id = DB_ID(@DatabaseName)");
            sb.AppendLine("          AND name = N'shltw_Data'");
            sb.AppendLine("    )");
            sb.AppendLine("    BEGIN");
            sb.AppendLine("        SET @renameSql = N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' MODIFY FILE (NAME = N''shltw_Data'', NEWNAME = N''' + @DatabaseName + N'_Data'');';");
            sb.AppendLine("        PRINT N'  重新命名邏輯 Data 檔: shltw_Data → ' + @DatabaseName + N'_Data';");
            sb.AppendLine("        EXEC sp_executesql @renameSql;");
            sb.AppendLine("    END");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        PRINT N'  資料檔 shltw_Data 不存在或已被更名';");
            sb.AppendLine();
            sb.AppendLine("    -- 重新命名邏輯日誌檔");
            sb.AppendLine("    IF EXISTS (");
            sb.AppendLine("        SELECT 1 FROM sys.master_files");
            sb.AppendLine("        WHERE database_id = DB_ID(@DatabaseName)");
            sb.AppendLine("          AND name = N'shltw_Log'");
            sb.AppendLine("    )");
            sb.AppendLine("    BEGIN");
            sb.AppendLine("        SET @renameSql = N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' MODIFY FILE (NAME = N''shltw_Log'', NEWNAME = N''' + @DatabaseName + N'_Log'');';");
            sb.AppendLine("        PRINT N'  重新命名邏輯 Log 檔: shltw_Log → ' + @DatabaseName + N'_Log';");
            sb.AppendLine("        EXEC sp_executesql @renameSql;");
            sb.AppendLine("    END");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        PRINT N'  日誌檔 shltw_Log 不存在或已被更名';");
            sb.AppendLine();
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 重新命名邏輯檔名 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 3: 建立登入帳號與使用者
        var loginStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateLoginAndUser);
        if (loginStep is not null)
        {
            stepNumber++;
            var isRecreate = loginStep.SelectedAction == "刪除重建";
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 建立登入帳號與使用者");
            sb.AppendLine("-- 說明: 建立 SQL Server 登入帳號，並在主資料庫和測試資料庫中建立使用者");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立登入帳號與使用者 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @loginSql NVARCHAR(MAX);");
            sb.AppendLine("    USE [master];");
            sb.AppendLine();

            if (isRecreate)
            {
                sb.AppendLine("    -- 刪除現有登入帳號");
                sb.AppendLine("    IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @LoginName)");
                sb.AppendLine("    BEGIN");
                sb.AppendLine("        SET @loginSql = N'DROP LOGIN ' + QUOTENAME(@LoginName) + N';';");
                sb.AppendLine("        PRINT N'  刪除現有登入帳號 ' + @LoginName + N'...';");
                sb.AppendLine("        EXEC sp_executesql @loginSql;");
                sb.AppendLine("    END");
                sb.AppendLine();
            }

            sb.AppendLine("    -- 建立登入帳號");
            sb.AppendLine("    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @LoginName)");
            sb.AppendLine("    BEGIN");
            sb.AppendLine("        SET @loginSql = N'CREATE LOGIN ' + QUOTENAME(@LoginName) + N' WITH PASSWORD = N''' + REPLACE(@LoginPassword, N'''', N'''''') + N''', DEFAULT_DATABASE = ' + QUOTENAME(@DatabaseName) + N', CHECK_EXPIRATION = OFF, CHECK_POLICY = OFF;';");
            sb.AppendLine("        PRINT N'  建立登入帳號 ' + @LoginName + N'...';");
            sb.AppendLine("        EXEC sp_executesql @loginSql;");
            sb.AppendLine("    END");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        PRINT N'  登入帳號 ' + @LoginName + N' 已存在，跳過建立';");
            sb.AppendLine();

            // 主資料庫使用者
            sb.AppendLine("    -- 在主資料庫建立使用者");
            sb.AppendLine("    SET @loginSql = N'USE ' + QUOTENAME(@DatabaseName) + N';");
            sb.AppendLine("    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''' + REPLACE(@LoginName, N'''', N'''''') + N''')");
            sb.AppendLine("        CREATE USER ' + QUOTENAME(@LoginName) + N' FOR LOGIN ' + QUOTENAME(@LoginName) + N';");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        ALTER USER ' + QUOTENAME(@LoginName) + N' WITH LOGIN = ' + QUOTENAME(@LoginName) + N';';");
            sb.AppendLine("    PRINT N'  在 ' + @DatabaseName + N' 建立/綁定使用者...';");
            sb.AppendLine("    EXEC sp_executesql @loginSql;");
            sb.AppendLine();

            // 測試資料庫使用者
            sb.AppendLine("    -- 在測試資料庫建立使用者");
            sb.AppendLine("    SET @loginSql = N'USE ' + QUOTENAME(@TestDatabaseName) + N';");
            sb.AppendLine("    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''' + REPLACE(@LoginName, N'''', N'''''') + N''')");
            sb.AppendLine("        CREATE USER ' + QUOTENAME(@LoginName) + N' FOR LOGIN ' + QUOTENAME(@LoginName) + N';");
            sb.AppendLine("    ELSE");
            sb.AppendLine("        ALTER USER ' + QUOTENAME(@LoginName) + N' WITH LOGIN = ' + QUOTENAME(@LoginName) + N';';");
            sb.AppendLine("    PRINT N'  在 ' + @TestDatabaseName + N' 建立/綁定使用者...';");
            sb.AppendLine("    EXEC sp_executesql @loginSql;");
            sb.AppendLine();
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立登入帳號與使用者 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 4: 加入 db_owner 角色
        var dbOwnerStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.AddToDbOwner);
        if (dbOwnerStep is not null)
        {
            stepNumber++;
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 加入 db_owner 角色");
            sb.AppendLine("-- 說明: 將使用者加入主資料庫和測試資料庫的 db_owner 角色");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 加入 db_owner 角色 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @roleSql NVARCHAR(MAX);");
            sb.AppendLine();
            sb.AppendLine("    -- 主資料庫");
            sb.AppendLine("    SET @roleSql = N'USE ' + QUOTENAME(@DatabaseName) + N'; ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@LoginName) + N';';");
            sb.AppendLine("    PRINT N'  在 ' + @DatabaseName + N' 加入 db_owner...';");
            sb.AppendLine("    EXEC sp_executesql @roleSql;");
            sb.AppendLine();
            sb.AppendLine("    -- 測試資料庫");
            sb.AppendLine("    SET @roleSql = N'USE ' + QUOTENAME(@TestDatabaseName) + N'; ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@LoginName) + N';';");
            sb.AppendLine("    PRINT N'  在 ' + @TestDatabaseName + N' 加入 db_owner...';");
            sb.AppendLine("    EXEC sp_executesql @roleSql;");
            sb.AppendLine();
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 加入 db_owner 角色 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 5: 建立備份排程 Job
        var backupStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateBackupJob);
        if (backupStep is not null)
        {
            stepNumber++;
            var isRecreate = backupStep.SelectedAction == "刪除重建";
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 建立每日備份排程 Job");
            sb.AppendLine("-- 說明: 建立 SQL Agent Job，每日在指定時間對資料庫做完整備份，");
            sb.AppendLine("--       並自動清理超過保留天數的舊備份檔案");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立備份排程 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("    USE [msdb];");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @jobName NVARCHAR(256) = @DatabaseName + N'_' + @RecoveryModel + N'Backup';");
            sb.AppendLine();

            if (isRecreate)
            {
                sb.AppendLine("    -- 若 Job 已存在，先刪除");
                sb.AppendLine("    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @jobName)");
                sb.AppendLine("    BEGIN");
                sb.AppendLine("        PRINT N'  刪除現有 Job: ' + @jobName;");
                sb.AppendLine("        EXEC dbo.sp_delete_job @job_name = @jobName, @delete_unused_schedule = 1;");
                sb.AppendLine("    END");
                sb.AppendLine();
            }

            sb.AppendLine("    -- 建立 Job");
            sb.AppendLine("    PRINT N'  建立 Job: ' + @jobName;");
            sb.AppendLine("    EXEC dbo.sp_add_job");
            sb.AppendLine("        @job_name    = @jobName,");
            sb.AppendLine("        @enabled     = 1,");
            sb.AppendLine("        @description = N'[Specurai] 每日完整備份';");
            sb.AppendLine();

            // 備份步驟命令（使用動態 SQL 建構 @command）
            sb.AppendLine("    -- 建立備份步驟的命令");
            sb.AppendLine("    DECLARE @backupCmd NVARCHAR(MAX) = N'");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
            sb.AppendLine("    DECLARE @fullPath  NVARCHAR(260) = N''' + @BackupPath + @DatabaseName + N'_FULL_'' + @today + ''.bak'';");
            sb.AppendLine();
            sb.AppendLine("    BACKUP DATABASE [' + @DatabaseName + N']");
            sb.AppendLine("    TO DISK = @fullPath");
            sb.AppendLine("    WITH NOFORMAT, INIT,");
            sb.AppendLine("         NAME = N''' + @DatabaseName + N'-完整備份'',");
            sb.AppendLine("         SKIP, NOREWIND, NOUNLOAD, STATS = 10;");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @deleteday VARCHAR(8);");
            sb.AppendLine("    SELECT @deleteday = CONVERT(VARCHAR(8), DATEADD(DAY, -' + CAST(@RetentionDays AS NVARCHAR) + N', GETDATE()), 112);");
            sb.AppendLine("    EXEC master.dbo.xp_delete_file 0, N''' + @BackupPath + N''', N''bak'', @deleteday, 1;");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N''錯誤: '' + ERROR_MESSAGE();");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH';");
            sb.AppendLine();

            sb.AppendLine("    EXEC dbo.sp_add_jobstep");
            sb.AppendLine("        @job_name       = @jobName,");
            sb.AppendLine("        @step_name      = N'Full Backup',");
            sb.AppendLine("        @subsystem      = N'TSQL',");
            sb.AppendLine("        @on_success_action = 1,");
            sb.AppendLine("        @on_fail_action    = 2,");
            sb.AppendLine("        @command        = @backupCmd;");
            sb.AppendLine();

            sb.AppendLine("    -- 建立排程");
            sb.AppendLine("    DECLARE @scheduleName NVARCHAR(256) = @jobName + N'_Schedule';");
            sb.AppendLine("    EXEC dbo.sp_add_jobschedule");
            sb.AppendLine("        @job_name          = @jobName,");
            sb.AppendLine("        @name              = @scheduleName,");
            sb.AppendLine("        @freq_type         = 4,");
            sb.AppendLine("        @freq_interval     = 1,");
            sb.AppendLine("        @active_start_time = @BackupTime;");
            sb.AppendLine();

            sb.AppendLine("    -- 指定本機執行");
            sb.AppendLine("    EXEC dbo.sp_add_jobserver @job_name = @jobName;");
            sb.AppendLine();
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立備份排程 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 步驟 6: 建立還原排程 Job
        var restoreStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob);
        if (restoreStep is not null)
        {
            stepNumber++;
            var isRecreate = restoreStep.SelectedAction == "刪除重建";
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"-- 步驟 {stepNumber}: 建立每日還原排程 Job");
            sb.AppendLine("-- 說明: 建立 SQL Agent Job，每日在指定時間將備份還原到測試資料庫，");
            sb.AppendLine("--       以驗證備份完整性");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立還原排程 (開始) =====';");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("    USE [msdb];");
            sb.AppendLine();
            sb.AppendLine("    DECLARE @restoreJobName NVARCHAR(256) = @DatabaseName + N'_FullRestore';");
            sb.AppendLine();

            if (isRecreate)
            {
                sb.AppendLine("    -- 若 Job 已存在，先刪除");
                sb.AppendLine("    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @restoreJobName)");
                sb.AppendLine("    BEGIN");
                sb.AppendLine("        PRINT N'  刪除現有 Job: ' + @restoreJobName;");
                sb.AppendLine("        EXEC dbo.sp_delete_job @job_name = @restoreJobName, @delete_unused_schedule = 1;");
                sb.AppendLine("    END");
                sb.AppendLine();
            }

            sb.AppendLine("    -- 建立 Job");
            sb.AppendLine("    PRINT N'  建立 Job: ' + @restoreJobName;");
            sb.AppendLine("    EXEC dbo.sp_add_job");
            sb.AppendLine("        @job_name    = @restoreJobName,");
            sb.AppendLine("        @enabled     = 1,");
            sb.AppendLine("        @description = N'[Specurai] 每日將備份還原到測試資料庫';");
            sb.AppendLine();

            // 還原步驟命令
            sb.AppendLine("    -- 建立還原步驟的命令");
            sb.AppendLine("    DECLARE @restoreCmd NVARCHAR(MAX) = N'");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
            sb.AppendLine("    DECLARE @fullPath  NVARCHAR(260) = N''' + @BackupPath + @DatabaseName + N'_FULL_'' + @today + ''.bak'';");
            sb.AppendLine();
            sb.AppendLine("    PRINT N''開始：將 [' + @TestDatabaseName + N'] 設為 SINGLE_USER 並強制回滾...'';");
            sb.AppendLine("    ALTER DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
            sb.AppendLine();
            sb.AppendLine("    PRINT N''開始執行還原到 [' + @TestDatabaseName + N']...'';");
            sb.AppendLine("    RESTORE DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    FROM DISK = @fullPath");
            sb.AppendLine("    WITH");
            sb.AppendLine("      MOVE N''' + @DatabaseName + N'_Data'' TO N''' + @RestorePath + @TestDatabaseName + N'.mdf'',");
            sb.AppendLine("      MOVE N''' + @DatabaseName + N'_Log'' TO N''' + @RestorePath + @TestDatabaseName + N'.ldf'',");
            sb.AppendLine("      REPLACE, RECOVERY, STATS = 5;");
            sb.AppendLine();
            sb.AppendLine("    ALTER DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    SET MULTI_USER;");
            sb.AppendLine("    PRINT N''還原完成，已切回 MULTI_USER'';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N''錯誤: '' + ERROR_MESSAGE();");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH';");
            sb.AppendLine();

            sb.AppendLine("    EXEC dbo.sp_add_jobstep");
            sb.AppendLine("        @job_name       = @restoreJobName,");
            sb.AppendLine("        @step_name      = N'Restore Full',");
            sb.AppendLine("        @subsystem      = N'TSQL',");
            sb.AppendLine("        @on_success_action = 1,");
            sb.AppendLine("        @on_fail_action    = 2,");
            sb.AppendLine("        @command        = @restoreCmd;");
            sb.AppendLine();

            sb.AppendLine("    -- 建立排程");
            sb.AppendLine("    DECLARE @restoreScheduleName NVARCHAR(256) = @restoreJobName + N'_Schedule';");
            sb.AppendLine("    EXEC dbo.sp_add_jobschedule");
            sb.AppendLine("        @job_name          = @restoreJobName,");
            sb.AppendLine("        @name              = @restoreScheduleName,");
            sb.AppendLine("        @freq_type         = 4,");
            sb.AppendLine("        @freq_interval     = 1,");
            sb.AppendLine("        @active_start_time = @RestoreTime;");
            sb.AppendLine();

            sb.AppendLine("    -- 指定本機執行");
            sb.AppendLine("    EXEC dbo.sp_add_jobserver @job_name = @restoreJobName;");
            sb.AppendLine();
            sb.AppendLine($"PRINT N'===== 步驟 {stepNumber}: 建立還原排程 (完成) =====';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine($"    PRINT N'##### 步驟 {stepNumber} 發生錯誤 #####';");
            sb.AppendLine("    PRINT ERROR_MESSAGE();");
            sb.AppendLine("END CATCH;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        sb.AppendLine("PRINT N'維護計劃設定完成。';");
        return sb.ToString();
    }

    private static string EscapeSingleQuote(string value) => value.Replace("'", "''");

    private static string QuoteName(string name) => $"[{name.Replace("]", "]]")}]";

    private static string GetStepDescription(MaintenancePlanStep step) => step switch
    {
        MaintenancePlanStep.SetCompatibilityLevel => "更新相容性層級",
        MaintenancePlanStep.SetRecoveryModel => "設定 Recovery Model",
        MaintenancePlanStep.RenameLogicalFiles => "重新命名邏輯檔名",
        MaintenancePlanStep.CreateLoginAndUser => "建立登入帳號與使用者",
        MaintenancePlanStep.AddToDbOwner => "加入 db_owner 角色",
        MaintenancePlanStep.CreateBackupJob => "建立備份排程",
        MaintenancePlanStep.CreateRestoreJob => "建立還原排程",
        _ => step.ToString()
    };

    /// <inheritdoc/>
    public string GenerateAdjustAutoGrowthSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> files)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        sb.AppendLine($"-- 調整 {db} 的檔案 autogrowth");
        foreach (var f in files)
        {
            var growMB = f.FileType == DatabaseFileType.Data ? config.AutoGrowthDataMB : config.AutoGrowthLogMB;
            var name = EscapeSingleQuote(f.LogicalName);
            sb.AppendLine($"ALTER DATABASE {db} MODIFY FILE (NAME = N'{name}', FILEGROWTH = {growMB}MB);");
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public string GeneratePreExpandDataFileSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> dataFiles)
    {
        var sb = new StringBuilder();
        var db = QuoteName(config.DatabaseName);
        sb.AppendLine($"-- 預擴 {db} 的資料檔");
        foreach (var f in dataFiles.Where(x => x.FileType == DatabaseFileType.Data))
        {
            // 目前大小向上湊整到 GB，再加緩衝 GB
            var currentGB = (int)Math.Ceiling(f.SizeMB / 1024.0);
            var targetMB = (currentGB + config.PreExpandBufferGB) * 1024;
            var name = EscapeSingleQuote(f.LogicalName);
            sb.AppendLine($"ALTER DATABASE {db} MODIFY FILE (NAME = N'{name}', SIZE = {targetMB}MB);");
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public string GenerateCreateCheckDbJobSql(MaintenancePlanConfig config, string? action = null)
        => throw new NotImplementedException();
}
