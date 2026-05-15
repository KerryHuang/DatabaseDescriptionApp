using FluentAssertions;
using NSubstitute;
using Specurai.Application.Models;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

/// <summary>
/// MaintenancePlanService 測試
/// </summary>
public class MaintenancePlanServiceTests
{
    private readonly IDatabaseInfoRepository _dbInfoRepo;
    private readonly IAgentJobRepository _agentJobRepo;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;
    private readonly MaintenancePlanService _sut;

    public MaintenancePlanServiceTests()
    {
        _dbInfoRepo = Substitute.For<IDatabaseInfoRepository>();
        _agentJobRepo = Substitute.For<IAgentJobRepository>();
        _sqlGenerator = Substitute.For<IMaintenancePlanSqlGenerator>();
        _sut = new MaintenancePlanService(_dbInfoRepo, _agentJobRepo, _sqlGenerator);
    }

    private static MaintenancePlanConfig CreateConfig(
        IReadOnlyList<MaintenancePlanStep>? steps = null,
        string dbName = "TestDB") => new()
    {
        DatabaseName = dbName,
        BackupPath = @"C:\Backup\",
        RestorePath = @"C:\Restore\",
        TestDatabaseName = "TestDB_Restore",
        LoginName = "shltw_user",
        LoginPassword = "P@ssw0rd",
        BackupTime = 230000,
        RestoreTime = 30000,
        SelectedSteps = steps ?? [MaintenancePlanStep.SetRecoveryModel]
    };

    #region CheckPrerequisitesAsync

    [Fact]
    public async Task CheckPrerequisitesAsync_Azure環境_應回傳錯誤()
    {
        // Arrange
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var (isReady, errorMessage) = await _sut.CheckPrerequisitesAsync();

        // Assert
        isReady.Should().BeFalse();
        errorMessage.Should().Contain("Azure");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_Agent未執行_應回傳錯誤()
    {
        // Arrange
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var (isReady, errorMessage) = await _sut.CheckPrerequisitesAsync();

        // Assert
        isReady.Should().BeFalse();
        errorMessage.Should().Contain("Agent");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_無權限_應回傳錯誤()
    {
        // Arrange
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(true);
        _agentJobRepo.HasAgentPermissionAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var (isReady, errorMessage) = await _sut.CheckPrerequisitesAsync();

        // Assert
        isReady.Should().BeFalse();
        errorMessage.Should().Contain("權限");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_全部通過_應回傳就緒()
    {
        // Arrange
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(true);
        _agentJobRepo.HasAgentPermissionAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var (isReady, errorMessage) = await _sut.CheckPrerequisitesAsync();

        // Assert
        isReady.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    #endregion

    #region CheckStepsAsync

    [Fact]
    public async Task CheckStepsAsync_RecoveryModel已為SIMPLE_應標記AlreadyExists()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.SetRecoveryModel]);
        _dbInfoRepo.GetRecoveryModelAsync("TestDB", Arg.Any<CancellationToken>()).Returns("SIMPLE");

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results.Should().HaveCount(1);
        results[0].AlreadyExists.Should().BeTrue();
        results[0].CurrentStatus.Should().Contain("SIMPLE");
    }

    [Fact]
    public async Task CheckStepsAsync_RecoveryModel為FULL_應標記未存在()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.SetRecoveryModel]);
        _dbInfoRepo.GetRecoveryModelAsync("TestDB", Arg.Any<CancellationToken>()).Returns("FULL");

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results.Should().HaveCount(1);
        results[0].AlreadyExists.Should().BeFalse();
        results[0].CurrentStatus.Should().Contain("FULL");
    }

    [Fact]
    public async Task CheckStepsAsync_登入帳號已存在_應標記AlreadyExists()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.CreateLoginAndUser]);
        _dbInfoRepo.LoginExistsAsync("shltw_user", Arg.Any<CancellationToken>()).Returns(true);
        _dbInfoRepo.DatabaseUserExistsAsync("TestDB", "shltw_user", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStepsAsync_登入帳號不存在_應標記未存在()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.CreateLoginAndUser]);
        _dbInfoRepo.LoginExistsAsync("shltw_user", Arg.Any<CancellationToken>()).Returns(false);
        _dbInfoRepo.DatabaseUserExistsAsync("TestDB", "shltw_user", Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStepsAsync_備份Job已存在_應標記AlreadyExists()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.CreateBackupJob]);
        _dbInfoRepo.GetRecoveryModelAsync("TestDB", Arg.Any<CancellationToken>()).Returns("FULL");
        _dbInfoRepo.AgentJobExistsAsync("TestDB_FULLBackup", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStepsAsync_DbOwner已為成員_應標記AlreadyExists()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.AddToDbOwner]);
        _dbInfoRepo.IsDbOwnerMemberAsync("TestDB", "shltw_user", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeTrue();
    }

    [Fact]
    public async Task CheckStepsAsync_重新命名邏輯檔名_前綴不是資料庫名稱_應標記需要重命名()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.RenameLogicalFiles]);
        _dbInfoRepo.GetLogicalFileNamesAsync("TestDB", Arg.Any<CancellationToken>())
            .Returns(new List<(string, string)> { ("shltw_Data", @"C:\Data\test.mdf"), ("shltw_Log", @"C:\Data\test.ldf") });

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeFalse();
        results[0].CurrentStatus.Should().Contain("shltw_Data");
    }

    [Fact]
    public async Task CheckStepsAsync_重新命名邏輯檔名_前綴為資料庫名稱_應標記已完成()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.RenameLogicalFiles]);
        _dbInfoRepo.GetLogicalFileNamesAsync("TestDB", Arg.Any<CancellationToken>())
            .Returns(new List<(string, string)> { ("TestDB", @"C:\Data\test.mdf"), ("TestDB_log", @"C:\Data\test.ldf") });

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeTrue();
        results[0].CurrentStatus.Should().Contain("TestDB");
    }

    [Fact]
    public async Task CheckStepsAsync_還原Job不存在_應標記未存在()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.CreateRestoreJob]);
        _dbInfoRepo.AgentJobExistsAsync("TestDB_FullRestore", Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var results = await _sut.CheckStepsAsync(config);

        // Assert
        results[0].AlreadyExists.Should().BeFalse();
    }

    #endregion

    #region ExecutePlanAsync

    [Fact]
    public async Task ExecutePlanAsync_應依序執行三個交易群組()
    {
        // Arrange
        var config = CreateConfig([
            MaintenancePlanStep.SetRecoveryModel,
            MaintenancePlanStep.CreateBackupJob,
            MaintenancePlanStep.CreateRestoreJob
        ]);
        var checkResults = new List<StepCheckResult>
        {
            new() { Step = MaintenancePlanStep.SetRecoveryModel, AlreadyExists = false, CurrentStatus = "FULL", AvailableActions = ["執行"], SelectedAction = "執行" },
            new() { Step = MaintenancePlanStep.CreateBackupJob, AlreadyExists = false, CurrentStatus = "不存在", AvailableActions = ["建立"], SelectedAction = "建立" },
            new() { Step = MaintenancePlanStep.CreateRestoreJob, AlreadyExists = false, CurrentStatus = "不存在", AvailableActions = ["建立"], SelectedAction = "建立" }
        };

        _sqlGenerator.GenerateStepSql(MaintenancePlanStep.SetRecoveryModel, config, "執行").Returns("SQL1");
        _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, "建立").Returns("SQL2");
        _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, config, "建立").Returns("SQL3");

        var progressMessages = new List<string>();
        var progress = Substitute.For<IProgress<string>>();
        progress.When(p => p.Report(Arg.Any<string>())).Do(ci => progressMessages.Add(ci.Arg<string>()));

        // Act
        await _sut.ExecutePlanAsync(config, checkResults, progress);

        // Assert
        await _dbInfoRepo.Received(1).ExecuteSqlWithTransactionAsync("SQL1", Arg.Any<CancellationToken>());
        await _dbInfoRepo.Received(1).ExecuteSqlAsync("SQL2", Arg.Any<CancellationToken>());
        await _dbInfoRepo.Received(1).ExecuteSqlAsync("SQL3", Arg.Any<CancellationToken>());
        progressMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecutePlanAsync_已存在的步驟_應跳過()
    {
        // Arrange
        var config = CreateConfig([MaintenancePlanStep.SetRecoveryModel]);
        var checkResults = new List<StepCheckResult>
        {
            new() { Step = MaintenancePlanStep.SetRecoveryModel, AlreadyExists = true, CurrentStatus = "SIMPLE", AvailableActions = ["跳過"], SelectedAction = "跳過" }
        };

        // Act
        await _sut.ExecutePlanAsync(config, checkResults);

        // Assert
        await _dbInfoRepo.DidNotReceive().ExecuteSqlWithTransactionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dbInfoRepo.DidNotReceive().ExecuteSqlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckSteps_AutoGrowth_當mdf小於64MB或ldf為百分比_應標記需調整()
    {
        var dbRepo = Substitute.For<IDatabaseInfoRepository>();
        var jobRepo = Substitute.For<IAgentJobRepository>();
        var sqlGen = Substitute.For<IMaintenancePlanSqlGenerator>();

        dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>())
            .Returns(new List<DatabaseFileInfo>
            {
                new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                        SizeMB = 25600, FreeMB = 1280, IsPercentGrowth = false, GrowthMB = 1,
                        VolumeMountPoint = @"D:\", VolumeFreeGB = 100 },
                new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log,
                        SizeMB = 1024, FreeMB = 512, IsPercentGrowth = true, GrowthMB = 10,
                        VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
            });

        var svc = new MaintenancePlanService(dbRepo, jobRepo, sqlGen);
        var config = MakeAutoGrowthTestConfig(MaintenancePlanStep.AdjustAutoGrowth);
        var results = await svc.CheckStepsAsync(config);

        var r = results.Single();
        r.Step.Should().Be(MaintenancePlanStep.AdjustAutoGrowth);
        r.AlreadyExists.Should().BeFalse();
        r.CurrentStatus.Should().Contain("需調整");
    }

    [Fact]
    public async Task CheckSteps_AutoGrowth_當所有檔案皆為固定且至少64MB_應標記已最佳化()
    {
        var dbRepo = Substitute.For<IDatabaseInfoRepository>();
        var jobRepo = Substitute.For<IAgentJobRepository>();
        var sqlGen = Substitute.For<IMaintenancePlanSqlGenerator>();

        dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>())
            .Returns(new List<DatabaseFileInfo>
            {
                new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                        SizeMB = 25600, FreeMB = 1280, IsPercentGrowth = false, GrowthMB = 256,
                        VolumeMountPoint = @"D:\", VolumeFreeGB = 100 },
                new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log,
                        SizeMB = 1024, FreeMB = 512, IsPercentGrowth = false, GrowthMB = 128,
                        VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
            });

        var svc = new MaintenancePlanService(dbRepo, jobRepo, sqlGen);
        var config = MakeAutoGrowthTestConfig(MaintenancePlanStep.AdjustAutoGrowth);
        var results = await svc.CheckStepsAsync(config);

        results.Single().AlreadyExists.Should().BeTrue();
        results.Single().CurrentStatus.Should().Contain("已最佳化");
    }

    private static MaintenancePlanConfig MakeAutoGrowthTestConfig(params MaintenancePlanStep[] steps) => new()
    {
        DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = steps
    };

    #endregion

    #region CheckSteps_PreExpandDataFile

    [Fact]
    public async Task CheckSteps_PreExpand_當資料檔可用率小於20pct_應建議預擴()
    {
        var dbRepo = Substitute.For<IDatabaseInfoRepository>();
        dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25600, FreeMB = 1024,
                    IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
        });
        var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

        var r = (await svc.CheckStepsAsync(MakeAutoGrowthTestConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

        r.AlreadyExists.Should().BeFalse();
        r.CurrentStatus.Should().Contain("建議預擴");
    }

    [Fact]
    public async Task CheckSteps_PreExpand_當磁碟空間不足_應標記跳過且不可執行()
    {
        var dbRepo = Substitute.For<IDatabaseInfoRepository>();
        // 預擴緩衝預設 5 GB；護欄為 5*1.5=7.5 GB；磁碟僅 4 GB
        dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25600, FreeMB = 100,
                    IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 4 }
        });
        var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

        var r = (await svc.CheckStepsAsync(MakeAutoGrowthTestConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

        r.AlreadyExists.Should().BeTrue();
        r.CurrentStatus.Should().Contain("磁碟空間不足");
        r.AvailableActions.Should().BeEquivalentTo(new[] { "跳過" });
    }

    [Fact]
    public async Task CheckSteps_PreExpand_當可用率大於等於20pct_應標記空間充足()
    {
        var dbRepo = Substitute.For<IDatabaseInfoRepository>();
        dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 10240, FreeMB = 5120,
                    IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
        });
        var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

        var r = (await svc.CheckStepsAsync(MakeAutoGrowthTestConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

        r.AlreadyExists.Should().BeTrue();
        r.CurrentStatus.Should().Contain("空間充足");
    }

    #endregion

    #region GeneratePreviewSqlAsync

    [Fact]
    public async Task GeneratePreviewSqlAsync_應委派至SqlGenerator()
    {
        // Arrange
        var config = CreateConfig();
        var checkResults = new List<StepCheckResult>();
        _sqlGenerator.GenerateFullSql(config, checkResults).Returns("-- 預覽 SQL");

        // Act
        var result = await _sut.GeneratePreviewSqlAsync(config, checkResults);

        // Assert
        result.Should().Be("-- 預覽 SQL");
    }

    #endregion
}
