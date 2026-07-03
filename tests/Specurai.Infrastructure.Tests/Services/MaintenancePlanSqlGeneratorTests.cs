using System.Text.RegularExpressions;
using FluentAssertions;
using Specurai.Application.Models;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// 維護計劃 SQL 產生器測試
/// </summary>
public class MaintenancePlanSqlGeneratorTests
{
    private readonly MaintenancePlanSqlGenerator _sut = new();

    private static MaintenancePlanConfig CreateConfig(
        string database = "MyDB",
        string testDatabase = "MyDB_Test",
        string loginName = "appUser",
        string password = "P@ssw0rd",
        string backupPath = @"D:\Backup\",
        string restorePath = @"D:\Data\",
        int backupTime = 230000,
        int restoreTime = 10000,
        int retentionDays = 7,
        string recoveryModel = "FULL") => new()
    {
        DatabaseName = database,
        TestDatabaseName = testDatabase,
        LoginName = loginName,
        LoginPassword = password,
        BackupPath = backupPath,
        RestorePath = restorePath,
        BackupTime = backupTime,
        RestoreTime = restoreTime,
        RetentionDays = retentionDays,
        RecoveryModel = recoveryModel,
        SelectedSteps = Enum.GetValues<MaintenancePlanStep>().ToList()
    };

    #region SetRecoveryModel

    [Fact]
    public void GenerateStepSql_SetRecoveryModel_應包含ALTER_DATABASE()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.SetRecoveryModel, CreateConfig());

        sql.Should().Contain("ALTER DATABASE [MyDB]");
        sql.Should().Contain("SET RECOVERY SIMPLE");
    }

    [Fact]
    public void GenerateStepSql_SetRecoveryModel_應同時設定測試資料庫()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.SetRecoveryModel, CreateConfig());

        sql.Should().Contain("ALTER DATABASE [MyDB_Test]");
    }

    #endregion

    #region CreateLoginAndUser

    [Fact]
    public void GenerateStepSql_CreateLoginAndUser_密碼應轉義單引號()
    {
        var config = CreateConfig(password: "It's a t'est");
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateLoginAndUser, config);

        sql.Should().Contain("It''s a t''est");
        sql.Should().NotContain("It's a t'est");
    }

    [Fact]
    public void GenerateStepSql_CreateLoginAndUser_應使用括號包裹識別符()
    {
        var config = CreateConfig(loginName: "myLogin");
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateLoginAndUser, config);

        sql.Should().Contain("[myLogin]");
    }

    [Fact]
    public void GenerateStepSql_CreateLoginAndUser_刪除重建_應先刪除Login()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateLoginAndUser, CreateConfig(), "刪除重建");

        sql.Should().Contain("DROP LOGIN");
        var dropIndex = sql.IndexOf("DROP LOGIN");
        var createIndex = sql.IndexOf("CREATE LOGIN");
        dropIndex.Should().BeLessThan(createIndex);
    }

    #endregion

    #region CreateBackupJob

    [Fact]
    public void GenerateStepSql_CreateBackupJob_應包含Specurai標記()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, CreateConfig());

        sql.Should().Contain("[Specurai]");
    }

    [Fact]
    public void GenerateStepSql_CreateBackupJob_應使用設定的排程時間()
    {
        var config = CreateConfig(backupTime: 233000);
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config);

        sql.Should().Contain("233000");
    }

    [Fact]
    public void GenerateStepSql_CreateBackupJob_刪除重建_應先刪除Job()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, CreateConfig(), "刪除重建");

        sql.Should().Contain("sp_delete_job");
        var deleteIndex = sql.IndexOf("sp_delete_job");
        var addIndex = sql.IndexOf("sp_add_job");
        deleteIndex.Should().BeLessThan(addIndex);
    }

    [Fact]
    public void GenerateCreateBackupJob_合併還原_應產生單一Job雙步驟且還原預設不觸發()
    {
        // Arrange
        var config = CreateConfig(
            database: "WayDoSoft01",
            recoveryModel: "SIMPLE",
            backupPath: @"D:\SQLBackup\",
            restorePath: @"D:\sql_data\",
            testDatabase: "WayDoSoft01",
            backupTime: 20000);

        // Act
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, null);

        // Assert：單一 Job
        sql.Should().Contain("WayDoSoft01_SIMPLEBackup");
        sql.Should().NotContain("_FullRestore");
        // 兩個步驟
        sql.Should().Contain("Full Backup");
        sql.Should().Contain("Restore Full to");
        Regex.Matches(sql, "sp_add_jobstep").Count.Should().Be(2);
        // 單一排程，時間為 BackupTime
        Regex.Matches(sql, "sp_add_jobschedule").Count.Should().Be(1);
        sql.Should().Contain("@active_start_time = 20000");
        // Step 1 成功即結束（還原預設不觸發）
        sql.Should().Contain("@on_success_action = 1");
        // 啟用還原的說明註解
        sql.Should().Contain("移至下一步");
    }

    #endregion

    #region GenerateFullSql

    [Fact]
    public void GenerateFullSql_應包含BEGIN_TRANSACTION()
    {
        var results = CreateAllCheckResults("執行");
        var sql = _sut.GenerateFullSql(CreateConfig(), results);

        sql.Should().Contain("BEGIN TRANSACTION");
    }

    [Fact]
    public void GenerateFullSql_跳過的步驟_不應產生SQL()
    {
        var results = CreateAllCheckResults("跳過");
        var sql = _sut.GenerateFullSql(CreateConfig(), results);

        sql.Should().NotContain("ALTER DATABASE");
        sql.Should().NotContain("CREATE LOGIN");
        sql.Should().NotContain("sp_add_job");
    }

    [Fact]
    public void GenerateFullSql_備份步驟_應合併還原為單一Job雙步驟且不含獨立還原排程()
    {
        var results = CreateAllCheckResults("執行");
        var sql = _sut.GenerateFullSql(CreateConfig(), results);

        sql.Should().NotContain("_FullRestore");
        sql.Should().NotContain("@active_start_time = @RestoreTime");
        // 不再產生獨立還原排程區塊（避免產出空的區段標頭）
        sql.Should().NotContain("建立還原排程");
        sql.Should().Contain("Restore Full");
        Regex.Matches(sql, "sp_add_jobstep").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    private static List<StepCheckResult> CreateAllCheckResults(string action)
    {
        return Enum.GetValues<MaintenancePlanStep>().Select(step => new StepCheckResult
        {
            Step = step,
            AlreadyExists = false,
            CurrentStatus = "未設定",
            AvailableActions = [action],
            SelectedAction = action
        }).ToList();
    }

    #region GeneratePreExpandDataFileSql

    [Fact]
    public void GeneratePreExpandDataFileSql_應湊整GB且只擴資料檔()
    {
        var gen = new MaintenancePlanSqlGenerator();
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
            TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
            BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
            PreExpandBufferGB = 5
        };
        // 25600 MB (25 GB) + 5 GB = 30 GB = 30720 MB
        var dataFiles = new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25600, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = "D", VolumeFreeGB = 100 }
        };

        var sql = gen.GeneratePreExpandDataFileSql(config, dataFiles);

        sql.Should().Contain("ALTER DATABASE [DB]");
        sql.Should().Contain("NAME = N'DB'").And.Contain("SIZE = 30720MB");
        sql.Should().NotContain("_log");
    }

    [Fact]
    public void GeneratePreExpandDataFileSql_當目前大小非整GB_應向上湊整再加緩衝()
    {
        var gen = new MaintenancePlanSqlGenerator();
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
            TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
            BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
            PreExpandBufferGB = 5
        };
        // 25700 MB ≈ 25.1 GB → 湊整 26 GB → +5 = 31 GB = 31744 MB
        var files = new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25700, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = "D", VolumeFreeGB = 100 }
        };

        var sql = gen.GeneratePreExpandDataFileSql(config, files);
        sql.Should().Contain("SIZE = 31744MB");
    }

    #endregion

    #region GenerateAdjustAutoGrowthSql

    [Fact]
    public void GenerateAdjustAutoGrowthSql_應產出每檔的MODIFY_FILE_語句()
    {
        var gen = new MaintenancePlanSqlGenerator();
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
            TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
            BackupTime = 2, RestoreTime = 3, SelectedSteps = []
        };
        var files = new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data, SizeMB = 1, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 1, VolumeMountPoint = "D", VolumeFreeGB = null },
            new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log, SizeMB = 1, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 1, VolumeMountPoint = "D", VolumeFreeGB = null }
        };

        var sql = gen.GenerateAdjustAutoGrowthSql(config, files);

        sql.Should().Contain("ALTER DATABASE [DB]");
        sql.Should().Contain("NAME = N'DB'").And.Contain("FILEGROWTH = 256MB");
        sql.Should().Contain("NAME = N'DB_log'").And.Contain("FILEGROWTH = 128MB");
    }

    #endregion

    #region GenerateCreateCheckDbJobSql

    [Fact]
    public void GenerateCreateCheckDbJobSql_應包含DBCC_CHECKDB與PHYSICAL_ONLY且每週執行()
    {
        var gen = new MaintenancePlanSqlGenerator();
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
            TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
            BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
            CheckDbHour = 3, CheckDbDayOfWeek = DayOfWeek.Sunday
        };

        var sql = gen.GenerateCreateCheckDbJobSql(config);

        sql.Should().Contain("DB_CheckDb");
        sql.Should().Contain("DBCC CHECKDB");
        sql.Should().Contain("PHYSICAL_ONLY");
        sql.Should().Contain("@freq_type         = 8");
        sql.Should().Contain("@freq_interval     = 1");
        sql.Should().Contain("@active_start_time = 30000");
        sql.Should().Contain("sp_add_job");
        sql.Should().Contain("sp_add_jobschedule");
    }

    [Fact]
    public void GenerateCreateCheckDbJobSql_當action為刪除重建_應先刪除再建立()
    {
        var gen = new MaintenancePlanSqlGenerator();
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
            TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
            BackupTime = 2, RestoreTime = 3, SelectedSteps = []
        };

        var sql = gen.GenerateCreateCheckDbJobSql(config, "刪除重建");

        sql.Should().Contain("sp_delete_job");
        sql.Should().Contain("sp_add_job");
    }

    #endregion
}
