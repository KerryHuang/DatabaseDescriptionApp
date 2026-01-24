using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// ColumnTypeInfo 實體測試
/// </summary>
public class ColumnTypeInfoTests
{
    #region 基本屬性測試

    [Fact]
    public void ColumnTypeInfo_建立字串欄位_屬性應正確設定()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo
        {
            ColumnName = "Email",
            SchemaName = "dbo",
            TableName = "Users",
            DataType = "nvarchar(200)",
            BaseType = "nvarchar",
            MaxLength = 200,
            IsNullable = false
        };

        // Assert
        columnType.ColumnName.Should().Be("Email");
        columnType.SchemaName.Should().Be("dbo");
        columnType.TableName.Should().Be("Users");
        columnType.DataType.Should().Be("nvarchar(200)");
        columnType.BaseType.Should().Be("nvarchar");
        columnType.MaxLength.Should().Be(200);
        columnType.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void ColumnTypeInfo_建立Decimal欄位_應包含精確度和小數位數()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo
        {
            ColumnName = "Price",
            SchemaName = "dbo",
            TableName = "Products",
            DataType = "decimal(18,2)",
            BaseType = "decimal",
            Precision = 18,
            Scale = 2
        };

        // Assert
        columnType.Precision.Should().Be(18);
        columnType.Scale.Should().Be(2);
    }

    #endregion

    #region FullTableName 測試

    [Fact]
    public void FullTableName_應回傳Schema點TableName()
    {
        // Arrange
        var columnType = new ColumnTypeInfo
        {
            SchemaName = "dbo",
            TableName = "Users"
        };

        // Assert
        columnType.FullTableName.Should().Be("dbo.Users");
    }

    [Fact]
    public void FullTableName_不同Schema_應正確組合()
    {
        // Arrange
        var columnType = new ColumnTypeInfo
        {
            SchemaName = "sales",
            TableName = "Orders"
        };

        // Assert
        columnType.FullTableName.Should().Be("sales.Orders");
    }

    #endregion

    #region IsLengthChangeable 測試

    [Theory]
    [InlineData("varchar")]
    [InlineData("nvarchar")]
    [InlineData("char")]
    [InlineData("nchar")]
    [InlineData("varbinary")]
    [InlineData("binary")]
    public void IsLengthChangeable_字串型態_應為True(string baseType)
    {
        // Arrange
        var columnType = new ColumnTypeInfo { BaseType = baseType };

        // Assert
        columnType.IsLengthChangeable.Should().BeTrue();
    }

    [Theory]
    [InlineData("VARCHAR")]
    [InlineData("NVARCHAR")]
    [InlineData("VarChar")]
    public void IsLengthChangeable_不區分大小寫_應為True(string baseType)
    {
        // Arrange
        var columnType = new ColumnTypeInfo { BaseType = baseType };

        // Assert
        columnType.IsLengthChangeable.Should().BeTrue();
    }

    [Theory]
    [InlineData("int")]
    [InlineData("bigint")]
    [InlineData("decimal")]
    [InlineData("datetime")]
    [InlineData("bit")]
    [InlineData("uniqueidentifier")]
    public void IsLengthChangeable_非字串型態_應為False(string baseType)
    {
        // Arrange
        var columnType = new ColumnTypeInfo { BaseType = baseType };

        // Assert
        columnType.IsLengthChangeable.Should().BeFalse();
    }

    #endregion

    #region IsConsistent 和 StatusText 測試

    [Fact]
    public void IsConsistent_預設值_應為True()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo();

        // Assert
        columnType.IsConsistent.Should().BeTrue();
    }

    [Fact]
    public void StatusText_一致時_應顯示一致()
    {
        // Arrange
        var columnType = new ColumnTypeInfo { IsConsistent = true };

        // Assert
        columnType.StatusText.Should().Contain("一致");
    }

    [Fact]
    public void StatusText_不一致時_應顯示不一致()
    {
        // Arrange
        var columnType = new ColumnTypeInfo { IsConsistent = false };

        // Assert
        columnType.StatusText.Should().Contain("不一致");
    }

    #endregion

    #region Constraints 測試

    [Fact]
    public void Constraints_預設值_應為空集合()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo();

        // Assert
        columnType.Constraints.Should().BeEmpty();
    }

    [Fact]
    public void Constraints_可新增約束()
    {
        // Arrange
        var columnType = new ColumnTypeInfo
        {
            ColumnName = "Id",
            SchemaName = "dbo",
            TableName = "Users"
        };

        var constraint = new ConstraintInfo
        {
            ConstraintName = "PK_Users",
            Type = ConstraintType.PrimaryKey,
            Columns = ["Id"]
        };

        // Act
        columnType.Constraints.Add(constraint);

        // Assert
        columnType.Constraints.Should().ContainSingle();
        columnType.Constraints.First().ConstraintName.Should().Be("PK_Users");
    }

    #endregion

    #region 預設值測試

    [Fact]
    public void ColumnTypeInfo_預設值_字串屬性應為空字串()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo();

        // Assert
        columnType.ColumnName.Should().BeEmpty();
        columnType.SchemaName.Should().BeEmpty();
        columnType.TableName.Should().BeEmpty();
        columnType.DataType.Should().BeEmpty();
        columnType.BaseType.Should().BeEmpty();
    }

    [Fact]
    public void ColumnTypeInfo_預設值_數值屬性應為0()
    {
        // Arrange & Act
        var columnType = new ColumnTypeInfo();

        // Assert
        columnType.MaxLength.Should().Be(0);
        columnType.Precision.Should().Be(0);
        columnType.Scale.Should().Be(0);
    }

    #endregion
}
