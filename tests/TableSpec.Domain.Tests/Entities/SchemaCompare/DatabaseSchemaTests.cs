using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 資料庫 Schema 快照實體測試
/// </summary>
public class DatabaseSchemaTests
{
    [Fact]
    public void DatabaseSchema_應該能建立具有所有屬性的實例()
    {
        // Arrange & Act
        var schema = new DatabaseSchema
        {
            ConnectionName = "開發環境",
            DatabaseName = "MyAppDb",
            ServerName = "localhost",
            CollectedAt = new DateTime(2026, 1, 24, 10, 30, 0),
            Tables = new List<SchemaTable>(),
            Views = new List<SchemaProgramObject>(),
            StoredProcedures = new List<SchemaProgramObject>(),
            Functions = new List<SchemaProgramObject>(),
            Triggers = new List<SchemaProgramObject>()
        };

        // Assert
        schema.ConnectionName.Should().Be("開發環境");
        schema.DatabaseName.Should().Be("MyAppDb");
        schema.ServerName.Should().Be("localhost");
        schema.CollectedAt.Should().Be(new DateTime(2026, 1, 24, 10, 30, 0));
    }

    [Fact]
    public void GetTable_存在的表格應該傳回該表格()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            ConnectionName = "測試",
            Tables = new List<SchemaTable>
            {
                new() { Schema = "dbo", Name = "Users" },
                new() { Schema = "dbo", Name = "Orders" }
            }
        };

        // Act
        var table = schema.GetTable("dbo", "Users");

        // Assert
        table.Should().NotBeNull();
        table!.Name.Should().Be("Users");
    }

    [Fact]
    public void GetTable_不存在的表格應該傳回null()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            ConnectionName = "測試",
            Tables = new List<SchemaTable>
            {
                new() { Schema = "dbo", Name = "Users" }
            }
        };

        // Act
        var table = schema.GetTable("dbo", "NonExistent");

        // Assert
        table.Should().BeNull();
    }

    [Fact]
    public void GetAllTableNames_應該傳回所有表格全名()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            ConnectionName = "測試",
            Tables = new List<SchemaTable>
            {
                new() { Schema = "dbo", Name = "Users" },
                new() { Schema = "sales", Name = "Orders" }
            }
        };

        // Act
        var names = schema.GetAllTableNames();

        // Assert
        names.Should().HaveCount(2);
        names.Should().Contain("[dbo].[Users]");
        names.Should().Contain("[sales].[Orders]");
    }

    [Fact]
    public void TotalObjectCount_應該計算所有物件數量()
    {
        // Arrange
        var schema = new DatabaseSchema
        {
            ConnectionName = "測試",
            Tables = new List<SchemaTable>
            {
                new() { Schema = "dbo", Name = "Users" },
                new() { Schema = "dbo", Name = "Orders" }
            },
            Views = new List<SchemaProgramObject>
            {
                new() { Schema = "dbo", Name = "vw_Report" }
            },
            StoredProcedures = new List<SchemaProgramObject>
            {
                new() { Schema = "dbo", Name = "sp_GetUsers" },
                new() { Schema = "dbo", Name = "sp_UpdateOrder" }
            },
            Functions = new List<SchemaProgramObject>(),
            Triggers = new List<SchemaProgramObject>()
        };

        // Act
        var count = schema.TotalObjectCount;

        // Assert
        count.Should().Be(5); // 2 Tables + 1 View + 2 SP + 0 Functions + 0 Triggers
    }
}
