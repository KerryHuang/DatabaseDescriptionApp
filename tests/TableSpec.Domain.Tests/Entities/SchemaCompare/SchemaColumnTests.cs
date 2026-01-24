using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

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
