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
        int retentionDays = 7) => new()
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

    #endregion

    #region CreateRestoreJob

    [Fact]
    public void GenerateStepSql_CreateRestoreJob_應包含RESTORE_DATABASE()
    {
        var sql = _sut.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, CreateConfig());

        sql.Should().Contain("RESTORE DATABASE");
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
}
