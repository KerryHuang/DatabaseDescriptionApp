using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// MaintenancePlanDocumentViewModel 測試
/// </summary>
public class MaintenancePlanDocumentViewModelTests
{
    private readonly IAgentJobService _jobService;
    private readonly IMaintenancePlanService _planService;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;
    private readonly IConnectionManager _connectionManager;

    public MaintenancePlanDocumentViewModelTests()
    {
        _jobService = Substitute.For<IAgentJobService>();
        _planService = Substitute.For<IMaintenancePlanService>();
        _sqlGenerator = Substitute.For<IMaintenancePlanSqlGenerator>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new MaintenancePlanDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("維護計劃");
        vm.Icon.Should().Be("🔧");
        vm.DocumentType.Should().Be("MaintenancePlan");
        vm.IsWizardMode.Should().BeFalse();
        vm.Jobs.Should().BeEmpty();
        vm.SelectedJob.Should().BeNull();
        vm.StatusMessage.Should().BeEmpty();
        vm.IsLoading.Should().BeFalse();
        vm.CurrentStep.Should().Be(1);
    }

    [Fact]
    public void Constructor_DI_應從連線取得資料庫名稱()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "測試連線",
            Server = "localhost",
            Database = "TestDB"
        };
        _connectionManager.GetCurrentProfile().Returns(profile);

        // Act
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager);

        // Assert
        vm.DatabaseName.Should().Be("TestDB");
        vm.TestDatabaseName.Should().Be("TestDB-test");
    }

    [Fact]
    public void Constructor_DI_無連線時資料庫名稱為空()
    {
        // Arrange
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        // Act
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager);

        // Assert
        vm.DatabaseName.Should().BeEmpty();
    }

    #endregion

    #region 模式切換測試

    [Fact]
    public void OpenWizard_應切換到精靈模式()
    {
        // Arrange
        var vm = new MaintenancePlanDocumentViewModel();

        // Act
        vm.OpenWizardCommand.Execute(null);

        // Assert
        vm.IsWizardMode.Should().BeTrue();
        vm.CurrentStep.Should().Be(1);
    }

    [Fact]
    public void CancelWizard_應切回管理模式()
    {
        // Arrange
        var vm = new MaintenancePlanDocumentViewModel();
        vm.OpenWizardCommand.Execute(null);
        vm.IsWizardMode.Should().BeTrue();

        // Act
        vm.CancelWizardCommand.Execute(null);

        // Assert
        vm.IsWizardMode.Should().BeFalse();
    }

    #endregion

    #region LoadJobsAsync 測試

    [Fact]
    public async Task LoadJobsAsync_應載入Job清單()
    {
        // Arrange
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = Guid.NewGuid(), Name = "測試Job1", IsEnabled = true },
            new() { JobId = Guid.NewGuid(), Name = "測試Job2", IsEnabled = false }
        };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs);
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager);

        // Act
        await vm.LoadJobsCommand.ExecuteAsync(null);

        // Assert
        vm.Jobs.Should().HaveCount(2);
        vm.Jobs[0].Name.Should().Be("測試Job1");
        vm.StatusMessage.Should().Contain("2");
    }

    #endregion

    #region DeleteJobAsync 測試

    [Fact]
    public async Task DeleteJobAsync_應呼叫Service並重新載入()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = new AgentJobInfo { JobId = jobId, Name = "待刪除Job", IsEnabled = true };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(new List<AgentJobInfo>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager)
        {
            SelectedJob = job,
            ConfirmDeleteCallback = () => Task.FromResult(true)
        };

        // Act
        await vm.DeleteJobCommand.ExecuteAsync(null);

        // Assert
        await _jobService.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
        vm.Jobs.Should().BeEmpty();
    }

    #endregion

    #region ToggleJobAsync 測試

    [Fact]
    public async Task ToggleJobAsync_啟用變停用_應呼叫Service()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = new AgentJobInfo { JobId = jobId, Name = "啟用Job", IsEnabled = true };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(new List<AgentJobInfo> { job });
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager)
        {
            SelectedJob = job
        };

        // Act
        await vm.ToggleJobCommand.ExecuteAsync(null);

        // Assert
        await _jobService.Received(1).SetJobEnabledAsync(jobId, false, Arg.Any<CancellationToken>());
    }

    #endregion

    #region 精靈步驟測試

    [Fact]
    public void 初始狀態_精靈預設值應正確()
    {
        // Act
        var vm = new MaintenancePlanDocumentViewModel();

        // Assert
        vm.LoginName.Should().Be("mis");
        vm.BackupTime.Should().Be(new TimeSpan(2, 0, 0));
        vm.RestoreTime.Should().Be(new TimeSpan(3, 0, 0));
        vm.IsSetRecoveryModelSelected.Should().BeTrue();
        vm.IsRenameLogicalFilesSelected.Should().BeTrue();
        vm.IsCreateLoginAndUserSelected.Should().BeTrue();
        vm.IsAddToDbOwnerSelected.Should().BeTrue();
        vm.IsCreateBackupJobSelected.Should().BeTrue();
        vm.IsCreateRestoreJobSelected.Should().BeFalse();
    }

    [Fact]
    public void NextStep_從步驟1到步驟2_應切換顯示()
    {
        // Arrange
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager)
        {
            DatabaseName = "TestDB",
            BackupPath = @"C:\Backup\",
            RestorePath = @"C:\Restore\",
            LoginName = "mis",
            LoginPassword = "password123"
        };

        // Act
        vm.NextStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(2);
        vm.IsStep1Visible.Should().BeFalse();
        vm.IsStep2Visible.Should().BeTrue();
        vm.IsStep3Visible.Should().BeFalse();
    }

    [Fact]
    public void PreviousStep_從步驟2到步驟1_應切換顯示()
    {
        // Arrange
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager)
        {
            DatabaseName = "TestDB",
            BackupPath = @"C:\Backup\",
            RestorePath = @"C:\Restore\",
            LoginName = "mis",
            LoginPassword = "password123"
        };
        vm.NextStepCommand.Execute(null);
        vm.CurrentStep.Should().Be(2);

        // Act
        vm.PreviousStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1Visible.Should().BeTrue();
        vm.IsStep2Visible.Should().BeFalse();
    }

    [Fact]
    public void CanNextStep_步驟1欄位未填_應為False()
    {
        // Arrange
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = new MaintenancePlanDocumentViewModel(
            _jobService, _planService, _sqlGenerator, _connectionManager);

        // Assert
        vm.NextStepCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TestDatabaseName_應自動帶入()
    {
        // Arrange
        var vm = new MaintenancePlanDocumentViewModel();

        // Act
        vm.DatabaseName = "WayDoSoft01";

        // Assert
        vm.TestDatabaseName.Should().Be("WayDoSoft01-test");
    }

    #endregion
}
