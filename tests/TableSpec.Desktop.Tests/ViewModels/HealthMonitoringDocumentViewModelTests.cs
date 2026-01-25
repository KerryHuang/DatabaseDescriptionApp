using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// HealthMonitoringDocumentViewModel 測試
/// </summary>
public class HealthMonitoringDocumentViewModelTests
{
    private readonly IHealthMonitoringService _healthMonitoringService;
    private readonly IConnectionManager _connectionManager;

    public HealthMonitoringDocumentViewModelTests()
    {
        _healthMonitoringService = Substitute.For<IHealthMonitoringService>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("健康監控");
    }

    [Fact]
    public void Constructor_無參數_Icon應為醫療圖示()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.Icon.Should().Be("🩺");
    }

    [Fact]
    public void Constructor_無參數_CanClose應為True()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.CanClose.Should().BeTrue();
    }

    #endregion

    #region DocumentType 測試

    [Fact]
    public void DocumentType_應為HealthMonitoring()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("HealthMonitoring");
    }

    [Fact]
    public void DocumentKey_應與DocumentType相同()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.DocumentKey.Should().Be(vm.DocumentType);
    }

    #endregion

    #region 初始狀態測試

    [Fact]
    public void 初始狀態_StatusMessage應為就緒()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.StatusMessage.Should().Be("就緒");
    }

    [Fact]
    public void 初始狀態_IsProcessing應為False()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_ShowSetupPanel應為True()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.ShowSetupPanel.Should().BeTrue();
    }

    [Fact]
    public void 初始狀態_ShowDashboard應為False()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.ShowDashboard.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsInstalled應為False()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.IsInstalled.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_ProgressPercentage應為0()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.ProgressPercentage.Should().Be(0);
    }

    [Fact]
    public void 初始狀態_ProgressMessage應為空()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.ProgressMessage.Should().BeEmpty();
    }

    #endregion

    #region 移除選項測試

    [Fact]
    public void 初始狀態_KeepHistoryData應為False()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.KeepHistoryData.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_RemoveJobsOnly應為False()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.RemoveJobsOnly.Should().BeFalse();
    }

    [Fact]
    public void KeepHistoryData_應可設定()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();

        // Act
        vm.KeepHistoryData = true;

        // Assert
        vm.KeepHistoryData.Should().BeTrue();
    }

    [Fact]
    public void RemoveJobsOnly_應可設定()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();

        // Act
        vm.RemoveJobsOnly = true;

        // Assert
        vm.RemoveJobsOnly.Should().BeTrue();
    }

    #endregion

    #region 集合初始化測試

    [Fact]
    public void 初始狀態_StatusSummaries應為空集合()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.StatusSummaries.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_CurrentMetrics應為空集合()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.CurrentMetrics.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_RecentAlerts應為空集合()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.RecentAlerts.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_Categories應為空集合()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.Categories.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_TrendSeries應為空集合()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.TrendSeries.Should().BeEmpty();
    }

    #endregion

    #region 趨勢選項測試

    [Fact]
    public void 初始狀態_SelectedTrendCheckType應為Memory()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.SelectedTrendCheckType.Should().Be("Memory");
    }

    [Fact]
    public void 初始狀態_SelectedTrendMetricName應為MemoryUsage()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.SelectedTrendMetricName.Should().Be("Memory Usage %");
    }

    [Fact]
    public void TrendCheckTypes_應包含五種類型()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.TrendCheckTypes.Should().HaveCount(5);
        vm.TrendCheckTypes.Should().Contain(new[] { "Memory", "CPU", "Disk", "Connections", "TempDB" });
    }

    #endregion

    #region 告警篩選測試

    [Fact]
    public void 初始狀態_AlertDays應為7()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.AlertDays.Should().Be(7);
    }

    [Fact]
    public void AlertDaysOptions_應包含五個選項()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.AlertDaysOptions.Should().HaveCount(5);
        vm.AlertDaysOptions.Should().Contain(new[] { 1, 3, 7, 14, 30 });
    }

    #endregion

    #region 面板切換測試

    [Fact]
    public void SwitchToSetupCommand_執行後_ShowSetupPanel應為True()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.ShowSetupPanel = false;
        vm.ShowDashboard = true;

        // Act
        vm.SwitchToSetupCommand.Execute(null);

        // Assert
        vm.ShowSetupPanel.Should().BeTrue();
        vm.ShowDashboard.Should().BeFalse();
    }

    [Fact]
    public void SwitchToDashboardCommand_未安裝時_不應切換()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsInstalled = false;
        vm.ShowSetupPanel = true;

        // Act
        vm.SwitchToDashboardCommand.Execute(null);

        // Assert
        vm.ShowSetupPanel.Should().BeTrue();
        vm.ShowDashboard.Should().BeFalse();
    }

    [Fact]
    public void SwitchToDashboardCommand_已安裝時_應切換到看板()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsInstalled = true;
        vm.ShowSetupPanel = true;

        // Act
        vm.SwitchToDashboardCommand.Execute(null);

        // Assert
        vm.ShowSetupPanel.Should().BeFalse();
        vm.ShowDashboard.Should().BeTrue();
    }

    #endregion

    #region CancelOperationCommand 測試

    [Fact]
    public void CancelOperationCommand_執行後_StatusMessage應更新()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();

        // Act
        vm.CancelOperationCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("取消");
    }

    #endregion

    #region 命令 CanExecute 測試

    [Fact]
    public void InstallCommand_IsProcessing為True時_CanExecute應為False()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = true;

        // Assert
        vm.InstallCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void InstallCommand_IsProcessing為False時_CanExecute應為True()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = false;

        // Assert
        vm.InstallCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RefreshCommand_IsProcessing為True時_CanExecute應為False()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = true;

        // Assert
        vm.RefreshCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExecuteHealthCheckCommand_IsProcessing為True時_CanExecute應為False()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = true;

        // Assert
        vm.ExecuteHealthCheckCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExecuteHealthCheckCommand_未安裝時_CanExecute應為False()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = false;
        vm.IsInstalled = false;

        // Assert
        vm.ExecuteHealthCheckCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExecuteHealthCheckCommand_已安裝且未處理時_CanExecute應為True()
    {
        // Arrange
        var vm = new HealthMonitoringDocumentViewModel();
        vm.IsProcessing = false;
        vm.IsInstalled = true;

        // Assert
        vm.ExecuteHealthCheckCommand.CanExecute(null).Should().BeTrue();
    }

    #endregion

    #region TrendXAxes 和 TrendYAxes 測試

    [Fact]
    public void TrendXAxes_應有一個軸()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.TrendXAxes.Should().HaveCount(1);
    }

    [Fact]
    public void TrendYAxes_應有一個軸()
    {
        // Act
        var vm = new HealthMonitoringDocumentViewModel();

        // Assert
        vm.TrendYAxes.Should().HaveCount(1);
    }

    #endregion
}
