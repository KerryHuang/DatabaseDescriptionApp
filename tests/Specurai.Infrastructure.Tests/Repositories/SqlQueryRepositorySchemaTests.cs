using System.Data;
using FluentAssertions;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlQueryRepositorySchemaTests
{
    /// <summary>建立模擬 GetSchemaTable() 回傳形狀的 schema 資料表</summary>
    private static DataTable BuildSchemaTable(params object?[][] rows)
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("BaseSchemaName", typeof(string));
        table.Columns.Add("BaseTableName", typeof(string));
        table.Columns.Add("BaseColumnName", typeof(string));
        table.Columns.Add("IsKey", typeof(bool));
        table.Columns.Add("IsAutoIncrement", typeof(bool));
        table.Columns.Add("IsReadOnly", typeof(bool));
        table.Columns.Add("IsExpression", typeof(bool));
        table.Columns.Add("DataType", typeof(Type));
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }

    [Fact(DisplayName = "一般欄位：來源表/欄與主鍵旗標正確對映")]
    public void MapColumnMetadata_一般欄位_應正確對映()
    {
        var schema = BuildSchemaTable(
            ["EMP_ID", "dbo", "SYS010", "EMP_ID", true, false, false, false, typeof(string)],
            ["EMP_NAME", "dbo", "SYS010", "EMP_NAME", false, false, false, false, typeof(string)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result.Should().HaveCount(2);
        result[0].ColumnName.Should().Be("EMP_ID");
        result[0].BaseSchema.Should().Be("dbo");
        result[0].BaseTable.Should().Be("SYS010");
        result[0].BaseColumn.Should().Be("EMP_ID");
        result[0].IsKey.Should().BeTrue();
        result[0].IsReadOnly.Should().BeFalse();
        result[0].ClrType.Should().Be(typeof(string));
        result[1].IsKey.Should().BeFalse();
    }

    [Fact(DisplayName = "identity 欄位應標記唯讀")]
    public void MapColumnMetadata_Identity欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["Id", "dbo", "T", "Id", true, true, false, false, typeof(int)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "運算式欄位（無 BaseColumn）應標記唯讀且來源欄為 null")]
    public void MapColumnMetadata_運算式欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["Total", null, null, null, false, false, false, true, typeof(decimal)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].BaseColumn.Should().BeNull();
        result[0].BaseTable.Should().BeNull();
        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "byte[]（timestamp/rowversion）欄位應標記唯讀")]
    public void MapColumnMetadata_ByteArray欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["TIMESTAMP", "dbo", "SYS010", "TIMESTAMP", false, false, false, false, typeof(byte[])]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "IsKey 為 DBNull 應視為 false")]
    public void MapColumnMetadata_IsKey為DBNull_應視為False()
    {
        var schema = BuildSchemaTable(
            ["A", "dbo", "T", "A", null, false, false, false, typeof(string)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsKey.Should().BeFalse();
    }

    [Fact(DisplayName = "schema 表為 null 應回傳空清單")]
    public void MapColumnMetadata_Null_應回傳空清單()
    {
        SqlQueryRepository.MapColumnMetadata(null).Should().BeEmpty();
    }
}
