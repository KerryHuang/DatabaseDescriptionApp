using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// MonitoringCategory 實體測試
/// </summary>
public class MonitoringCategoryTests
{
    [Fact]
    public void MonitoringCategory_建立時_應正確設定所有屬性()
    {
        // Arrange
        var lastCheckTime = new DateTime(2026, 1, 25, 10, 30, 0);
        var createdDate = new DateTime(2026, 1, 1, 0, 0, 0);

        // Act
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "Memory",
            Description = "記憶體監控",
            IsEnabled = true,
            CheckIntervalMinutes = 5,
            LastCheckTime = lastCheckTime,
            CreatedDate = createdDate
        };

        // Assert
        category.CategoryId.Should().Be(1);
        category.CategoryName.Should().Be("Memory");
        category.Description.Should().Be("記憶體監控");
        category.IsEnabled.Should().BeTrue();
        category.CheckIntervalMinutes.Should().Be(5);
        category.LastCheckTime.Should().Be(lastCheckTime);
        category.CreatedDate.Should().Be(createdDate);
    }

    [Fact]
    public void MonitoringCategory_IsEnabled_應可設定()
    {
        // Arrange
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "CPU",
            IsEnabled = true
        };

        // Act
        category.IsEnabled = false;

        // Assert
        category.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void MonitoringCategory_CheckIntervalMinutes_應可設定()
    {
        // Arrange
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "Disk",
            CheckIntervalMinutes = 5
        };

        // Act
        category.CheckIntervalMinutes = 15;

        // Assert
        category.CheckIntervalMinutes.Should().Be(15);
    }

    [Fact]
    public void MonitoringCategory_Description可為Null()
    {
        // Act
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "Blocking",
            Description = null
        };

        // Assert
        category.Description.Should().BeNull();
    }

    [Fact]
    public void MonitoringCategory_LastCheckTime可為Null()
    {
        // Act
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "Deadlocks",
            LastCheckTime = null
        };

        // Assert
        category.LastCheckTime.Should().BeNull();
    }

    [Fact]
    public void MonitoringCategory_CreatedDate_應可設定()
    {
        // Arrange
        var expectedDate = new DateTime(2026, 1, 15);

        // Act
        var category = new MonitoringCategory
        {
            CategoryId = 1,
            CategoryName = "TempDB",
            CreatedDate = expectedDate
        };

        // Assert
        category.CreatedDate.Should().Be(expectedDate);
    }
}
