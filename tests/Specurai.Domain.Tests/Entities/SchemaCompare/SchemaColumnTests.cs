using FluentAssertions;
using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 欄位結構實體測試
/// </summary>
public class SchemaColumnTests
{
    [Fact]
    public void SchemaColumn_應該能建立具有所有屬性的實例()
    {
        // Arrange & Act
        var column = new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            Precision = null,
            Scale = null,
            IsNullable = false,
            DefaultValue = "N''",
            IsIdentity = false,
            Collation = "Chinese_Taiwan_Stroke_CI_AS"
        };

        // Assert
        column.Name.Should().Be("Email");
        column.DataType.Should().Be("NVARCHAR");
        column.MaxLength.Should().Be(200);
        column.IsNullable.Should().BeFalse();
        column.DefaultValue.Should().Be("N''");
        column.IsIdentity.Should().BeFalse();
        column.Collation.Should().Be("Chinese_Taiwan_Stroke_CI_AS");
    }

    [Theory]
    [InlineData("INT", null, null, "INT")]
    [InlineData("NVARCHAR", 100, null, "NVARCHAR(100)")]
    [InlineData("DECIMAL", null, 18, "DECIMAL(18)")] // 只有 Precision
    [InlineData("DECIMAL", null, null, "DECIMAL")] // 無精度
    public void GetFullDataType_應該傳回完整的資料型別字串(
        string dataType, int? maxLength, int? precision, string expected)
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Test",
            DataType = dataType,
            MaxLength = maxLength,
            Precision = precision,
            Scale = null
        };

        // Act
        var result = column.GetFullDataType();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("datetime", 23, 3)]      // sys.columns 回傳 23,3 但 datetime 不接受精度
    [InlineData("smalldatetime", 16, 0)]
    [InlineData("date", 10, 0)]
    [InlineData("timestamp", 0, 0)]      // rowversion 別名
    [InlineData("rowversion", 0, 0)]
    [InlineData("int", 10, 0)]
    [InlineData("bigint", 19, 0)]
    [InlineData("smallint", 5, 0)]
    [InlineData("tinyint", 3, 0)]
    [InlineData("bit", 1, 0)]
    [InlineData("money", 19, 4)]
    [InlineData("float", 53, null)]
    [InlineData("real", 24, null)]
    [InlineData("text", 0, 0)]
    [InlineData("ntext", 0, 0)]
    [InlineData("image", 0, 0)]
    [InlineData("xml", 0, 0)]
    [InlineData("uniqueidentifier", 0, 0)]
    public void GetFullDataType_固定精度型別_不應加上精度括號(
        string dataType, int? precision, int? scale)
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Test",
            DataType = dataType,
            Precision = precision,
            Scale = scale
        };

        // Act
        var result = column.GetFullDataType();

        // Assert
        result.Should().Be(dataType);
    }

    [Fact]
    public void GetFullDataType_Decimal_應該包含精度和小數位數()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Price",
            DataType = "DECIMAL",
            Precision = 18,
            Scale = 2
        };

        // Act
        var result = column.GetFullDataType();

        // Assert
        result.Should().Be("DECIMAL(18,2)");
    }

    [Fact]
    public void Equals_相同屬性的欄位應該相等()
    {
        // Arrange
        var column1 = CreateTestColumn();
        var column2 = CreateTestColumn();

        // Act & Assert
        column1.Should().BeEquivalentTo(column2);
    }

    [Fact]
    public void Equals_不同名稱的欄位不應該相等()
    {
        // Arrange
        var column1 = CreateTestColumn();
        var column2 = CreateTestColumn();
        column2.Name = "DifferentName";

        // Act & Assert
        column1.Should().NotBeEquivalentTo(column2);
    }

    private static SchemaColumn CreateTestColumn()
    {
        return new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            IsNullable = false,
            DefaultValue = null,
            IsIdentity = false,
            Collation = "Chinese_Taiwan_Stroke_CI_AS"
        };
    }
}
