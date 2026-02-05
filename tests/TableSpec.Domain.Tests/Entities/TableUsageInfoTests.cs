using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// 資料表使用狀態資訊實體測試
/// </summary>
public class TableUsageInfoTests
{
    [Fact]
    public void HasQueryActivity_有查詢活動_應為True()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            UserSeeks = 100,
            UserScans = 0,
            UserLookups = 0,
            UserUpdates = 0,
            LastUserSeek = DateTime.Now.AddDays(-30),
            LastUserScan = null,
            LastUserUpdate = null
        };

        info.HasQueryActivity.Should().BeTrue();
    }

    [Fact]
    public void HasQueryActivity_無查詢活動_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            UserSeeks = 0,
            UserScans = 0,
            UserLookups = 0,
            UserUpdates = 0,
            LastUserSeek = null,
            LastUserScan = null,
            LastUserUpdate = null
        };

        info.HasQueryActivity.Should().BeFalse();
    }

    [Fact]
    public void HasRecentUpdate_最後更新在期限內_應為True()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            LastUserUpdate = DateTime.Now.AddMonths(-6)
        };

        info.HasRecentUpdate(years: 2).Should().BeTrue();
    }

    [Fact]
    public void HasRecentUpdate_最後更新超過期限_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            LastUserUpdate = DateTime.Now.AddYears(-3)
        };

        info.HasRecentUpdate(years: 2).Should().BeFalse();
    }

    [Fact]
    public void HasRecentUpdate_無更新紀錄_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            LastUserUpdate = null
        };

        info.HasRecentUpdate(years: 2).Should().BeFalse();
    }

    [Fact]
    public void HasQueryActivity_僅有Scans無Seeks和Lookups_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "ScannedOnly",
            UserSeeks = 0,
            UserScans = 5,
            UserLookups = 0,
            UserUpdates = 0,
            LastUserSeek = null,
            LastUserScan = DateTime.Now.AddDays(-1),
            LastUserUpdate = null
        };

        info.HasQueryActivity.Should().BeFalse();
    }

    [Fact]
    public void FullTableName_應回傳Schema加表名()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders"
        };

        info.FullTableName.Should().Be("[dbo].[Orders]");
    }

    [Fact]
    public void DropTableStatement_應產生正確SQL()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders"
        };

        info.DropTableStatement.Should().Be("DROP TABLE [dbo].[Orders]");
    }
}
