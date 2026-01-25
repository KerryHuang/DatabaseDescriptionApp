using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// HealthMonitoringService 測試
/// </summary>
public class HealthMonitoringServiceTests
{
    private readonly IHealthMonitoringRepository _repository;
    private readonly IHealthMonitoringInstaller _installer;
    private readonly IHealthMonitoringService _service;

    public HealthMonitoringServiceTests()
    {
        _repository = Substitute.For<IHealthMonitoringRepository>();
        _installer = Substitute.For<IHealthMonitoringInstaller>();
        _service = new HealthMonitoringService(_repository, _installer);
    }

    #region GetInstallStatusAsync 測試

    [Fact]
    public async Task GetInstallStatusAsync_應委派給Repository()
    {
        // Arrange
        var expectedStatus = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };
        _repository.GetInstallStatusAsync(Arg.Any<CancellationToken>())
            .Returns(expectedStatus);

        // Act
        var result = await _service.GetInstallStatusAsync();

        // Assert
        result.Should().Be(expectedStatus);
        await _repository.Received(1).GetInstallStatusAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region InstallAsync 測試

    [Fact]
    public async Task InstallAsync_應委派給Installer()
    {
        // Arrange
        var expectedResult = new InstallResult(true);
        _installer.InstallAsync(Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result = await _service.InstallAsync();

        // Assert
        result.Should().Be(expectedResult);
        await _installer.Received(1).InstallAsync(Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_應傳遞Progress參數()
    {
        // Arrange
        var progress = new Progress<InstallProgress>();
        _installer.InstallAsync(progress, Arg.Any<CancellationToken>())
            .Returns(new InstallResult(true));

        // Act
        await _service.InstallAsync(progress);

        // Assert
        await _installer.Received(1).InstallAsync(progress, Arg.Any<CancellationToken>());
    }

    #endregion

    #region UninstallAsync 測試

    [Fact]
    public async Task UninstallAsync_應委派給Installer()
    {
        // Arrange
        var options = new UninstallOptions(KeepHistoryData: false, RemoveJobsOnly: false);
        var expectedResult = new UninstallResult(true);
        _installer.UninstallAsync(options, Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result = await _service.UninstallAsync(options);

        // Assert
        result.Should().Be(expectedResult);
        await _installer.Received(1).UninstallAsync(options, Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UninstallAsync_KeepHistoryData選項_應正確傳遞()
    {
        // Arrange
        var options = new UninstallOptions(KeepHistoryData: true, RemoveJobsOnly: false);
        _installer.UninstallAsync(options, Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new UninstallResult(true));

        // Act
        await _service.UninstallAsync(options);

        // Assert
        await _installer.Received(1).UninstallAsync(
            Arg.Is<UninstallOptions>(o => o.KeepHistoryData == true),
            Arg.Any<IProgress<InstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UninstallAsync_RemoveJobsOnly選項_應正確傳遞()
    {
        // Arrange
        var options = new UninstallOptions(KeepHistoryData: false, RemoveJobsOnly: true);
        _installer.UninstallAsync(options, Arg.Any<IProgress<InstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new UninstallResult(true));

        // Act
        await _service.UninstallAsync(options);

        // Assert
        await _installer.Received(1).UninstallAsync(
            Arg.Is<UninstallOptions>(o => o.RemoveJobsOnly == true),
            Arg.Any<IProgress<InstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetStatusSummaryAsync 測試

    [Fact]
    public async Task GetStatusSummaryAsync_應委派給Repository()
    {
        // Arrange
        var expectedSummaries = new List<HealthStatusSummary>
        {
            new() { CheckType = "Memory", OkCount = 5, WarningCount = 0, CriticalCount = 0 },
            new() { CheckType = "CPU", OkCount = 3, WarningCount = 1, CriticalCount = 0 }
        };
        _repository.GetStatusSummaryAsync(Arg.Any<CancellationToken>())
            .Returns(expectedSummaries);

        // Act
        var result = await _service.GetStatusSummaryAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedSummaries);
        await _repository.Received(1).GetStatusSummaryAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetCurrentMetricsAsync 測試

    [Fact]
    public async Task GetCurrentMetricsAsync_應委派給Repository()
    {
        // Arrange
        var expectedMetrics = new List<HealthMetric>
        {
            new() { CheckType = "Memory", MetricName = "Memory Usage %", MetricValue = 75m }
        };
        _repository.GetCurrentMetricsAsync(Arg.Any<CancellationToken>())
            .Returns(expectedMetrics);

        // Act
        var result = await _service.GetCurrentMetricsAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedMetrics);
        await _repository.Received(1).GetCurrentMetricsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetRecentAlertsAsync 測試

    [Fact]
    public async Task GetRecentAlertsAsync_應委派給Repository()
    {
        // Arrange
        var expectedAlerts = new List<HealthLogEntry>
        {
            new() { LogId = 1, CheckTime = DateTime.Now, CheckType = "CPU", MetricName = "CPU %", Status = "WARNING" }
        };
        _repository.GetRecentAlertsAsync(7, Arg.Any<CancellationToken>())
            .Returns(expectedAlerts);

        // Act
        var result = await _service.GetRecentAlertsAsync(7);

        // Assert
        result.Should().BeEquivalentTo(expectedAlerts);
        await _repository.Received(1).GetRecentAlertsAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecentAlertsAsync_預設天數應為7()
    {
        // Arrange
        _repository.GetRecentAlertsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<HealthLogEntry>());

        // Act
        await _service.GetRecentAlertsAsync();

        // Assert
        await _repository.Received(1).GetRecentAlertsAsync(7, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetTrendDataAsync 測試

    [Fact]
    public async Task GetTrendDataAsync_應委派給Repository()
    {
        // Arrange
        var expectedData = new List<TrendDataPoint>
        {
            new() { CheckTime = DateTime.Now, CheckType = "Memory", MetricName = "Memory Usage %", MetricValue = 75m }
        };
        _repository.GetTrendDataAsync("Memory", "Memory Usage %", 30, Arg.Any<CancellationToken>())
            .Returns(expectedData);

        // Act
        var result = await _service.GetTrendDataAsync("Memory", "Memory Usage %", 30);

        // Assert
        result.Should().BeEquivalentTo(expectedData);
        await _repository.Received(1).GetTrendDataAsync("Memory", "Memory Usage %", 30, Arg.Any<CancellationToken>());
    }

    #endregion

    #region ExecuteHealthCheckAsync 測試

    [Fact]
    public async Task ExecuteHealthCheckAsync_應委派給Repository()
    {
        // Act
        await _service.ExecuteHealthCheckAsync();

        // Assert
        await _repository.Received(1).ExecuteHealthCheckAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetCategoriesAsync 測試

    [Fact]
    public async Task GetCategoriesAsync_應委派給Repository()
    {
        // Arrange
        var expectedCategories = new List<MonitoringCategory>
        {
            new() { CategoryId = 1, CategoryName = "Memory", IsEnabled = true }
        };
        _repository.GetCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns(expectedCategories);

        // Act
        var result = await _service.GetCategoriesAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedCategories);
        await _repository.Received(1).GetCategoriesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region UpdateCategoryAsync 測試

    [Fact]
    public async Task UpdateCategoryAsync_應委派給Repository()
    {
        // Act
        await _service.UpdateCategoryAsync(1, true, 10);

        // Assert
        await _repository.Received(1).UpdateCategoryAsync(1, true, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCategoryAsync_應正確傳遞參數()
    {
        // Arrange
        var categoryId = 5;
        var isEnabled = false;
        var intervalMinutes = 30;

        // Act
        await _service.UpdateCategoryAsync(categoryId, isEnabled, intervalMinutes);

        // Assert
        await _repository.Received(1).UpdateCategoryAsync(
            categoryId,
            isEnabled,
            intervalMinutes,
            Arg.Any<CancellationToken>());
    }

    #endregion
}
