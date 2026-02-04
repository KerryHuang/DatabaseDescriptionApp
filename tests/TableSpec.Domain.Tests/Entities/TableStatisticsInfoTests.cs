using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// TableStatisticsInfo 實體測試
/// </summary>
public class TableStatisticsInfoTests
{
    #region 基本屬性測試

    [Fact]
    public void TableStatisticsInfo_建立資料表統計_屬性應正確設定()
    {
        // Arrange & Act
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Users",
            ObjectType = "TABLE",
            ApproximateRowCount = 10000,
            ColumnCount = 15,
            IndexCount = 3,
            ForeignKeyCount = 2,
            DataSizeMB = 50.5m,
            IndexSizeMB = 10.2m,
            TotalSizeMB = 60.7m
        };

        // Assert
        info.SchemaName.Should().Be("dbo");
        info.TableName.Should().Be("Users");
        info.ObjectType.Should().Be("TABLE");
        info.ApproximateRowCount.Should().Be(10000);
        info.ColumnCount.Should().Be(15);
        info.IndexCount.Should().Be(3);
        info.ForeignKeyCount.Should().Be(2);
        info.DataSizeMB.Should().Be(50.5m);
        info.IndexSizeMB.Should().Be(10.2m);
        info.TotalSizeMB.Should().Be(60.7m);
    }

    [Fact]
    public void TableStatisticsInfo_建立檢視表統計_應支援VIEW類型()
    {
        // Arrange & Act
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "vw_Users",
            ObjectType = "VIEW",
            ApproximateRowCount = 0,
            ColumnCount = 5,
            IndexCount = 0,
            ForeignKeyCount = 0,
            DataSizeMB = 0,
            IndexSizeMB = 0,
            TotalSizeMB = 0
        };

        // Assert
        info.ObjectType.Should().Be("VIEW");
        info.ApproximateRowCount.Should().Be(0);
    }

    #endregion

    #region DisplayRowCount 測試

    [Fact]
    public void DisplayRowCount_未設定精確列數_應回傳概估列數()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ObjectType = "TABLE",
            ApproximateRowCount = 50000
        };

        // Act & Assert
        info.DisplayRowCount.Should().Be(50000);
    }

    [Fact]
    public void DisplayRowCount_已設定精確列數_應回傳精確列數()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ObjectType = "TABLE",
            ApproximateRowCount = 50000,
            ExactRowCount = 49876
        };

        // Act & Assert
        info.DisplayRowCount.Should().Be(49876);
    }

    [Fact]
    public void DisplayRowCount_精確列數為零_應回傳零而非概估值()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "EmptyTable",
            ObjectType = "TABLE",
            ApproximateRowCount = 100,
            ExactRowCount = 0
        };

        // Act & Assert
        info.DisplayRowCount.Should().Be(0);
    }

    #endregion

    #region HasExactRowCount 測試

    [Fact]
    public void HasExactRowCount_未設定精確列數_應回傳False()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Products",
            ObjectType = "TABLE",
            ApproximateRowCount = 1000
        };

        // Act & Assert
        info.HasExactRowCount.Should().BeFalse();
    }

    [Fact]
    public void HasExactRowCount_已設定精確列數_應回傳True()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Products",
            ObjectType = "TABLE",
            ApproximateRowCount = 1000,
            ExactRowCount = 998
        };

        // Act & Assert
        info.HasExactRowCount.Should().BeTrue();
    }

    #endregion

    #region ExactRowCount 可變性測試

    [Fact]
    public void ExactRowCount_應可在建立後設定()
    {
        // Arrange
        var info = new TableStatisticsInfo
        {
            SchemaName = "dbo",
            TableName = "Logs",
            ObjectType = "TABLE",
            ApproximateRowCount = 100000
        };

        // Act
        info.ExactRowCount = 99500;

        // Assert
        info.ExactRowCount.Should().Be(99500);
        info.HasExactRowCount.Should().BeTrue();
        info.DisplayRowCount.Should().Be(99500);
    }

    #endregion
}
