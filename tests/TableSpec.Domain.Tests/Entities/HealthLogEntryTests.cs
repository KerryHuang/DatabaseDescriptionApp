using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// HealthLogEntry 實體測試
/// </summary>
public class HealthLogEntryTests
{
    [Fact]
    public void HealthLogEntry_建立時_應正確設定所有屬性()
    {
        // Arrange
        var checkTime = new DateTime(2026, 1, 25, 10, 30, 0);

        // Act
        var entry = new HealthLogEntry
        {
            LogId = 1,
            CheckTime = checkTime,
            CheckType = "Memory",
            MetricName = "Memory Usage %",
            MetricValue = 75.5m,
            ThresholdValue = 80m,
            Status = "OK",
            AlertMessage = null,
            ServerName = "SQL-PROD",
            DatabaseName = "master",
            AdditionalInfo = "測試資訊"
        };

        // Assert
        entry.LogId.Should().Be(1);
        entry.CheckTime.Should().Be(checkTime);
        entry.CheckType.Should().Be("Memory");
        entry.MetricName.Should().Be("Memory Usage %");
        entry.MetricValue.Should().Be(75.5m);
        entry.ThresholdValue.Should().Be(80m);
        entry.Status.Should().Be("OK");
        entry.AlertMessage.Should().BeNull();
        entry.ServerName.Should().Be("SQL-PROD");
        entry.DatabaseName.Should().Be("master");
        entry.AdditionalInfo.Should().Be("測試資訊");
    }

    [Fact]
    public void HealthLogEntry_Warning狀態_應有告警訊息()
    {
        // Act
        var entry = new HealthLogEntry
        {
            LogId = 2,
            CheckTime = DateTime.Now,
            CheckType = "CPU",
            MetricName = "SQL Server CPU %",
            MetricValue = 85m,
            ThresholdValue = 80m,
            Status = "WARNING",
            AlertMessage = "CPU 使用率超過警告閾值"
        };

        // Assert
        entry.Status.Should().Be("WARNING");
        entry.AlertMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HealthLogEntry_Critical狀態_應有告警訊息()
    {
        // Act
        var entry = new HealthLogEntry
        {
            LogId = 3,
            CheckTime = DateTime.Now,
            CheckType = "Disk",
            MetricName = "Free Space MB",
            MetricValue = 500m,
            ThresholdValue = 1000m,
            Status = "CRITICAL",
            AlertMessage = "磁碟空間不足"
        };

        // Assert
        entry.Status.Should().Be("CRITICAL");
        entry.AlertMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HealthLogEntry_MetricValue可為Null()
    {
        // Act
        var entry = new HealthLogEntry
        {
            LogId = 4,
            CheckTime = DateTime.Now,
            CheckType = "Blocking",
            MetricName = "Blocked Sessions",
            MetricValue = null,
            Status = "OK"
        };

        // Assert
        entry.MetricValue.Should().BeNull();
    }
}
