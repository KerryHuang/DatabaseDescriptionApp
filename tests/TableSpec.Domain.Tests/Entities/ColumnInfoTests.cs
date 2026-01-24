using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// ColumnInfo 實體測試
/// </summary>
public class ColumnInfoTests
{
    [Fact]
    public void ColumnInfo_建立主鍵欄位_屬性應正確設定()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Id",
            DataType = "int",
            Length = null,
            DefaultValue = null,
            IsPrimaryKey = true,
            IsUniqueKey = false,
            IsIndexed = true,
            IsNullable = false,
            Description = "主鍵識別碼"
        };

        // Assert
        column.Schema.Should().Be("dbo");
        column.TableName.Should().Be("Users");
        column.ColumnName.Should().Be("Id");
        column.DataType.Should().Be("int");
        column.IsPrimaryKey.Should().BeTrue();
        column.IsNullable.Should().BeFalse();
        column.IsIndexed.Should().BeTrue();
    }

    [Fact]
    public void ColumnInfo_建立字串欄位_應包含長度()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Email",
            DataType = "nvarchar",
            Length = 200,
            DefaultValue = null,
            IsPrimaryKey = false,
            IsUniqueKey = true,
            IsIndexed = true,
            IsNullable = false,
            Description = "電子郵件"
        };

        // Assert
        column.DataType.Should().Be("nvarchar");
        column.Length.Should().Be(200);
        column.IsUniqueKey.Should().BeTrue();
    }

    [Fact]
    public void ColumnInfo_建立有預設值的欄位_應正確設定()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Status",
            DataType = "int",
            Length = null,
            DefaultValue = "1",
            IsPrimaryKey = false,
            IsUniqueKey = false,
            IsIndexed = false,
            IsNullable = false,
            Description = "狀態"
        };

        // Assert
        column.DefaultValue.Should().Be("1");
        column.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void ColumnInfo_建立可為Null的欄位_IsNullable應為True()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "MiddleName",
            DataType = "nvarchar",
            Length = 50,
            DefaultValue = null,
            IsPrimaryKey = false,
            IsUniqueKey = false,
            IsIndexed = false,
            IsNullable = true,
            Description = "中間名"
        };

        // Assert
        column.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void ColumnInfo_Description和OriginalDescription不同_表示已變更()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Name",
            DataType = "nvarchar",
            Length = 100,
            Description = "使用者名稱（已修改）",
            OriginalDescription = "使用者名稱"
        };

        // Assert
        column.Description.Should().NotBe(column.OriginalDescription);
    }

    [Fact]
    public void ColumnInfo_Description可設定為Null()
    {
        // Arrange & Act
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "TempField",
            DataType = "int",
            Description = null
        };

        // Assert
        column.Description.Should().BeNull();
    }

    [Fact]
    public void ColumnInfo_各種資料型別_應正確處理()
    {
        // Arrange & Act
        var intColumn = new ColumnInfo { Schema = "dbo", TableName = "T", ColumnName = "C1", DataType = "int" };
        var bigintColumn = new ColumnInfo { Schema = "dbo", TableName = "T", ColumnName = "C2", DataType = "bigint" };
        var decimalColumn = new ColumnInfo { Schema = "dbo", TableName = "T", ColumnName = "C3", DataType = "decimal" };
        var datetimeColumn = new ColumnInfo { Schema = "dbo", TableName = "T", ColumnName = "C4", DataType = "datetime2" };
        var bitColumn = new ColumnInfo { Schema = "dbo", TableName = "T", ColumnName = "C5", DataType = "bit" };

        // Assert
        intColumn.DataType.Should().Be("int");
        bigintColumn.DataType.Should().Be("bigint");
        decimalColumn.DataType.Should().Be("decimal");
        datetimeColumn.DataType.Should().Be("datetime2");
        bitColumn.DataType.Should().Be("bit");
    }
}
