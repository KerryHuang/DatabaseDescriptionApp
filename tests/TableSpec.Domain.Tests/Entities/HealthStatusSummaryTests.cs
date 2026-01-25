using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// HealthStatusSummary 實體測試
/// </summary>
public class HealthStatusSummaryTests
{
    [Fact]
    public void HealthStatusSummary_建立時_應正確設定所有屬性()
    {
        // Arrange
        var lastCheckTime = new DateTime(2026, 1, 25, 10, 30, 0);

        // Act
        var summary = new HealthStatusSummary
        {
            CheckType = "Memory",
            TotalCount = 5,
            OkCount = 4,
            WarningCount = 1,
            CriticalCount = 0,
            LastCheckTime = lastCheckTime
        };

        // Assert
        summary.CheckType.Should().Be("Memory");
        summary.TotalCount.Should().Be(5);
        summary.OkCount.Should().Be(4);
        summary.WarningCount.Should().Be(1);
        summary.CriticalCount.Should().Be(0);
        summary.LastCheckTime.Should().Be(lastCheckTime);
    }

    [Fact]
    public void OverallStatus_全部OK_應為OK()
    {
        // Act
        var summary = new HealthStatusSummary
        {
            CheckType = "CPU",
            TotalCount = 3,
            OkCount = 3,
            WarningCount = 0,
            CriticalCount = 0
        };

        // Assert
        summary.OverallStatus.Should().Be("OK");
    }

    [Fact]
    public void OverallStatus_有Warning無Critical_應為WARNING()
    {
        // Act
        var summary = new HealthStatusSummary
        {
            CheckType = "Disk",
            TotalCount = 4,
            OkCount = 3,
            WarningCount = 1,
            CriticalCount = 0
        };

        // Assert
        summary.OverallStatus.Should().Be("WARNING");
    }

    [Fact]
    public void OverallStatus_有Critical_應為CRITICAL()
    {
        // Act
        var summary = new HealthStatusSummary
        {
            CheckType = "Connections",
            TotalCount = 2,
            OkCount = 1,
            WarningCount = 0,
            CriticalCount = 1
        };

        // Assert
        summary.OverallStatus.Should().Be("CRITICAL");
    }

    [Fact]
    public void OverallStatus_有Warning和Critical_應為CRITICAL()
    {
        // Act - Critical 優先於 Warning
        var summary = new HealthStatusSummary
        {
            CheckType = "TempDB",
            TotalCount = 3,
            OkCount = 1,
            WarningCount = 1,
            CriticalCount = 1
        };

        // Assert
        summary.OverallStatus.Should().Be("CRITICAL");
    }

    [Fact]
    public void HealthStatusSummary_LastCheckTime可為Null()
    {
        // Act
        var summary = new HealthStatusSummary
        {
            CheckType = "TempDB",
            LastCheckTime = null
        };

        // Assert
        summary.LastCheckTime.Should().BeNull();
    }
}
