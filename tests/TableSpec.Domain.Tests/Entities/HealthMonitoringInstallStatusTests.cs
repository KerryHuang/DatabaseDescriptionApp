using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// HealthMonitoringInstallStatus 實體測試
/// </summary>
public class HealthMonitoringInstallStatusTests
{
    #region IsFullyInstalled 測試

    [Fact]
    public void IsFullyInstalled_所有元件都存在_應為True()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeTrue();
    }

    [Fact]
    public void IsFullyInstalled_缺少資料庫_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = false,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsFullyInstalled_缺少HealthLog資料表_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = false,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsFullyInstalled_缺少Categories資料表_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = false,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsFullyInstalled_缺少預存程序_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = false,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsFullyInstalled_缺少視圖_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = false,
            AgentJobsExist = true
        };

        // Assert
        status.IsFullyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsFullyInstalled_缺少AgentJobs仍應為True()
    {
        // Arrange - AgentJobs 不影響 IsFullyInstalled
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = false
        };

        // Assert
        status.IsFullyInstalled.Should().BeTrue();
    }

    #endregion

    #region IsPartiallyInstalled 測試

    [Fact]
    public void IsPartiallyInstalled_完全未安裝_應為False()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = false,
            HealthLogTableExists = false,
            CategoriesTableExists = false,
            MasterProcedureExists = false,
            ViewsExist = false,
            AgentJobsExist = false
        };

        // Assert
        status.IsPartiallyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsPartiallyInstalled_只有資料庫存在_應為False()
    {
        // Arrange - 只有資料庫存在但沒有其他元件
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = false,
            CategoriesTableExists = false,
            MasterProcedureExists = false,
            ViewsExist = false,
            AgentJobsExist = false
        };

        // Assert - 需要資料庫 AND 至少一個元件
        status.IsPartiallyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsPartiallyInstalled_資料庫和HealthLog資料表存在_應為True()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = false,
            MasterProcedureExists = false,
            ViewsExist = false,
            AgentJobsExist = false
        };

        // Assert
        status.IsPartiallyInstalled.Should().BeTrue();
    }

    [Fact]
    public void IsPartiallyInstalled_資料庫和預存程序存在_應為True()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = false,
            CategoriesTableExists = false,
            MasterProcedureExists = true,
            ViewsExist = false,
            AgentJobsExist = false
        };

        // Assert
        status.IsPartiallyInstalled.Should().BeTrue();
    }

    [Fact]
    public void IsPartiallyInstalled_無資料庫但有元件_應為False()
    {
        // Arrange - 資料庫不存在，其他元件存在
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = false,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = false,
            AgentJobsExist = false
        };

        // Assert - 必須有資料庫
        status.IsPartiallyInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsPartiallyInstalled_完全安裝_應為True()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.IsPartiallyInstalled.Should().BeTrue();
    }

    #endregion

    #region LogCount 測試

    [Fact]
    public void LogCount_預設為0()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true
        };

        // Assert
        status.LogCount.Should().Be(0);
    }

    [Fact]
    public void LogCount_設定值應正確()
    {
        // Arrange
        var status = new HealthMonitoringInstallStatus
        {
            DatabaseExists = true,
            HealthLogTableExists = true,
            CategoriesTableExists = true,
            MasterProcedureExists = true,
            ViewsExist = true,
            AgentJobsExist = true,
            LogCount = 12345
        };

        // Assert
        status.LogCount.Should().Be(12345);
    }

    #endregion
}
