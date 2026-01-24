using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// TableInfo 實體測試
/// </summary>
public class TableInfoTests
{
    [Fact]
    public void TableInfo_建立Table類型_屬性應正確設定()
    {
        // Arrange & Act
        var table = new TableInfo
        {
            Type = "TABLE",
            Schema = "dbo",
            Name = "Users",
            Description = "使用者資料表"
        };

        // Assert
        table.Type.Should().Be("TABLE");
        table.Schema.Should().Be("dbo");
        table.Name.Should().Be("Users");
        table.Description.Should().Be("使用者資料表");
    }

    [Fact]
    public void TableInfo_建立View類型_屬性應正確設定()
    {
        // Arrange & Act
        var view = new TableInfo
        {
            Type = "VIEW",
            Schema = "dbo",
            Name = "vw_ActiveUsers",
            Description = "活躍使用者檢視"
        };

        // Assert
        view.Type.Should().Be("VIEW");
        view.Schema.Should().Be("dbo");
        view.Name.Should().Be("vw_ActiveUsers");
        view.Description.Should().Be("活躍使用者檢視");
    }

    [Fact]
    public void TableInfo_建立StoredProcedure類型_屬性應正確設定()
    {
        // Arrange & Act
        var sp = new TableInfo
        {
            Type = "PROCEDURE",
            Schema = "dbo",
            Name = "sp_GetUsers",
            Description = "取得使用者的預存程序"
        };

        // Assert
        sp.Type.Should().Be("PROCEDURE");
        sp.Schema.Should().Be("dbo");
        sp.Name.Should().Be("sp_GetUsers");
        sp.Description.Should().Be("取得使用者的預存程序");
    }

    [Fact]
    public void TableInfo_建立Function類型_屬性應正確設定()
    {
        // Arrange & Act
        var func = new TableInfo
        {
            Type = "FUNCTION",
            Schema = "dbo",
            Name = "fn_CalculateTotal",
            Description = "計算總額的函數"
        };

        // Assert
        func.Type.Should().Be("FUNCTION");
        func.Schema.Should().Be("dbo");
        func.Name.Should().Be("fn_CalculateTotal");
        func.Description.Should().Be("計算總額的函數");
    }

    [Fact]
    public void TableInfo_Description為Null_應允許()
    {
        // Arrange & Act
        var table = new TableInfo
        {
            Type = "TABLE",
            Schema = "dbo",
            Name = "TempTable",
            Description = null
        };

        // Assert
        table.Description.Should().BeNull();
    }

    [Fact]
    public void TableInfo_不同Schema_應正確區分()
    {
        // Arrange & Act
        var table1 = new TableInfo
        {
            Type = "TABLE",
            Schema = "dbo",
            Name = "Orders"
        };

        var table2 = new TableInfo
        {
            Type = "TABLE",
            Schema = "sales",
            Name = "Orders"
        };

        // Assert
        table1.Schema.Should().NotBe(table2.Schema);
        table1.Name.Should().Be(table2.Name);
    }
}
