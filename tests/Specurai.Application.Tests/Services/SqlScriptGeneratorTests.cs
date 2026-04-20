using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Application.Tests.Services;

/// <summary>
/// SqlScriptGenerator 單元測試
/// </summary>
public class SqlScriptGeneratorTests
{
    private readonly ISqlScriptGenerator _generator = new SqlScriptGenerator();

    private static DatabaseSchema CreateBaseSchema(string name = "基準環境")
    {
        var schema = new DatabaseSchema { ConnectionName = name };
        var table = new SchemaTable { Schema = "dbo", Name = "Products" };
        table.Columns.Add(new SchemaColumn
        {
            Name = "Id", DataType = "INT", IsNullable = false, IsIdentity = true
        });
        table.Columns.Add(new SchemaColumn
        {
            Name = "Name", DataType = "NVARCHAR", MaxLength = 200, IsNullable = false
        });
        schema.Tables.Add(table);
        return schema;
    }

    [Fact]
    public void Generate_新增表格差異_腳本應包含CREATE_TABLE()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("CREATE TABLE [dbo].[Products]");
        script.ApplyScript.Should().Contain("BEGIN TRANSACTION");
        script.ApplyScript.Should().Contain("COMMIT TRANSACTION");
        script.ApplyScript.Should().Contain("ROLLBACK TRANSACTION");
    }

    [Fact]
    public void Generate_新增欄位差異_腳本應包含ALTER_TABLE_ADD()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Products].[Name]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Products] ADD [Name]");
    }

    [Fact]
    public void Generate_修改欄位長度差異_腳本應包含ALTER_COLUMN()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Products].[Name]",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "500",
            RiskLevel = RiskLevel.Medium
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Products] ALTER COLUMN [Name]");
        script.ApplyScript.Should().Contain("NVARCHAR(500)");
    }

    [Fact]
    public void Generate_差異清單為空_應產生空腳本結構()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();

        // Act
        var script = _generator.Generate([], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().NotBeNullOrEmpty();
        script.Differences.Should().BeEmpty();
    }

    [Fact]
    public void Generate_腳本應包含標頭註解()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();

        // Act
        var script = _generator.Generate([], baseSchema, "Production", "Staging");

        // Assert
        script.ApplyScript.Should().Contain("基準環境：Production");
        script.ApplyScript.Should().Contain("目標環境：Staging");
    }
}
