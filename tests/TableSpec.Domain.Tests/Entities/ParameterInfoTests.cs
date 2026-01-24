using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// ParameterInfo 實體測試
/// </summary>
public class ParameterInfoTests
{
    [Fact]
    public void ParameterInfo_建立輸入參數_屬性應正確設定()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Id",
            DataType = "int",
            Length = null,
            IsOutput = false,
            DefaultValue = null,
            Ordinal = 1
        };

        // Assert
        param.Name.Should().Be("@Id");
        param.DataType.Should().Be("int");
        param.IsOutput.Should().BeFalse();
        param.Ordinal.Should().Be(1);
    }

    [Fact]
    public void ParameterInfo_建立輸出參數_IsOutput應為True()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Result",
            DataType = "int",
            IsOutput = true,
            Ordinal = 2
        };

        // Assert
        param.IsOutput.Should().BeTrue();
    }

    [Fact]
    public void ParameterInfo_字串參數_應包含長度()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Name",
            DataType = "nvarchar",
            Length = 100,
            IsOutput = false,
            Ordinal = 1
        };

        // Assert
        param.DataType.Should().Be("nvarchar");
        param.Length.Should().Be(100);
    }

    [Fact]
    public void ParameterInfo_有預設值_應正確設定()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Status",
            DataType = "int",
            DefaultValue = "1",
            IsOutput = false,
            Ordinal = 3
        };

        // Assert
        param.DefaultValue.Should().Be("1");
    }

    [Fact]
    public void ParameterInfo_Ordinal_應表示參數順序()
    {
        // Arrange & Act
        var param1 = new ParameterInfo { Name = "@Id", DataType = "int", Ordinal = 1 };
        var param2 = new ParameterInfo { Name = "@Name", DataType = "nvarchar", Ordinal = 2 };
        var param3 = new ParameterInfo { Name = "@Status", DataType = "int", Ordinal = 3 };

        // Assert
        param1.Ordinal.Should().BeLessThan(param2.Ordinal);
        param2.Ordinal.Should().BeLessThan(param3.Ordinal);
    }

    [Fact]
    public void ParameterInfo_Length可為Null()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Value",
            DataType = "int",
            Length = null,
            Ordinal = 1
        };

        // Assert
        param.Length.Should().BeNull();
    }

    [Fact]
    public void ParameterInfo_MaxLength參數_應正確處理()
    {
        // Arrange & Act
        var param = new ParameterInfo
        {
            Name = "@Description",
            DataType = "nvarchar",
            Length = -1, // SQL Server 中 -1 表示 MAX
            IsOutput = false,
            Ordinal = 1
        };

        // Assert
        param.Length.Should().Be(-1);
    }
}
