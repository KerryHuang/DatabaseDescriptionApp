using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// 欄位使用狀態實體測試
/// </summary>
public class ColumnUsageStatusTests
{
    [Fact]
    public void IsUsed_PK欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Id",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = true,
            IsForeignKey = false,
            IsIdentity = true
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_FK欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "CustomerId",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = true,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_Identity欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Id",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = true
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_被查詢引用_應為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Status",
            DataType = "int",
            IsReferencedInQueries = true,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_未引用且全NULL_應為False()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_未引用且全為預設值_應為False()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Status",
            DataType = "int",
            DefaultValue = "0",
            IsReferencedInQueries = false,
            IsAllNull = false,
            IsAllDefault = true,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_未引用但有實際資料_應為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)",
            IsReferencedInQueries = false,
            IsAllNull = false,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void DropColumnStatement_應產生正確SQL()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)"
        };

        status.DropColumnStatement.Should().Be("ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]");
    }
}
