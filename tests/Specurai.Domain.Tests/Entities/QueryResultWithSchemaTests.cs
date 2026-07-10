using System.Data;
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class QueryResultWithSchemaTests
{
    private static QueryColumnMetadata Col(string name, string? table = "Users", string? schema = "dbo",
        string? baseColumn = null, bool isKey = false, bool isReadOnly = false, Type? clrType = null) => new()
    {
        ColumnName = name,
        BaseSchema = schema,
        BaseTable = table,
        BaseColumn = baseColumn ?? name,
        IsKey = isKey,
        IsReadOnly = isReadOnly,
        ClrType = clrType ?? typeof(string)
    };

    [Fact(DisplayName = "單一來源表：IsSingleTable 為 true 且 TargetTable/TargetSchema 正確")]
    public void 單一來源表_應判定為單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", isKey: true), Col("Name")]
        };

        result.IsSingleTable.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "多來源表（JOIN）：IsSingleTable 為 false 且 TargetTable 為 null")]
    public void 多來源表_應判定為非單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", table: "Users"), Col("OrderNo", table: "Orders")]
        };

        result.IsSingleTable.Should().BeFalse();
        result.TargetTable.Should().BeNull();
    }

    [Fact(DisplayName = "含運算式欄位（BaseTable 為 null）不影響單表判定")]
    public void 運算式欄位_不影響單表判定()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id"), Col("Total", table: null, baseColumn: null)]
        };

        result.IsSingleTable.Should().BeTrue();
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "全部都是運算式欄位：非單表")]
    public void 全運算式欄位_應判定為非單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("A", table: null, baseColumn: null)]
        };

        result.IsSingleTable.Should().BeFalse();
    }

    [Fact(DisplayName = "同表不同大小寫視為同一來源表")]
    public void 同表不同大小寫_應視為同一來源表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", table: "Users"), Col("Name", table: "USERS")]
        };

        result.IsSingleTable.Should().BeTrue();
    }
}
