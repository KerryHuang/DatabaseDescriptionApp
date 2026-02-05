using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

public class UsageComparisonTests
{
    [Fact]
    public void UsageScanResult_應正確計算未使用表數量()
    {
        var result = new UsageScanResult
        {
            Tables =
            [
                new TableUsageInfo { SchemaName = "dbo", TableName = "Used", UserSeeks = 10 },
                new TableUsageInfo { SchemaName = "dbo", TableName = "Unused1", UserSeeks = 0, UserScans = 0, UserLookups = 0 },
                new TableUsageInfo { SchemaName = "dbo", TableName = "Unused2", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
            ],
            Columns = [],
            YearsThreshold = 2
        };

        result.UnusedTableCount(years: 2).Should().Be(2);
        result.UsedTableCount(years: 2).Should().Be(1);
    }

    [Fact]
    public void UsageScanResult_應正確計算未使用欄位數量()
    {
        var result = new UsageScanResult
        {
            Tables = [],
            Columns =
            [
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Id", DataType = "int", IsPrimaryKey = true, IsAllNull = true },
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Remark", DataType = "nvarchar", IsAllNull = true },
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Status", DataType = "int", IsReferencedInQueries = true }
            ],
            YearsThreshold = 2
        };

        result.UnusedColumnCount.Should().Be(1);
        result.UsedColumnCount.Should().Be(2);
    }

    [Fact]
    public void TableUsageComparisonRow_應包含各環境狀態()
    {
        var row = new TableUsageComparisonRow
        {
            SchemaName = "dbo",
            TableName = "Orders",
            EnvironmentStatus = new Dictionary<string, TableUsageInfo?>
            {
                ["DEV"] = new TableUsageInfo { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
                ["PROD"] = null // 不存在
            }
        };

        row.EnvironmentStatus.Should().ContainKey("DEV");
        row.EnvironmentStatus["DEV"]!.HasQueryActivity.Should().BeTrue();
        row.EnvironmentStatus["PROD"].Should().BeNull();
    }

    [Fact]
    public void UsageComparison_應包含基準環境與目標環境()
    {
        var comparison = new UsageComparison
        {
            BaseEnvironment = "DEV",
            TargetEnvironments = ["STG", "PROD"],
            TableRows = [],
            ColumnRows = []
        };

        comparison.BaseEnvironment.Should().Be("DEV");
        comparison.TargetEnvironments.Should().HaveCount(2);
    }
}
