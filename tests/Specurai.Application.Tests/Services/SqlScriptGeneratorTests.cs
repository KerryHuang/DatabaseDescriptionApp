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
            Schema = "dbo",
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
            Schema = "dbo",
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
            Schema = "dbo",
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

    [Fact]
    public void Generate_非dbo表格_腳本應使用正確Schema()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準環境" };
        var table = new SchemaTable { Schema = "Sales", Name = "Orders" };
        table.Columns.Add(new SchemaColumn { Name = "Id", DataType = "INT", IsNullable = false });
        baseSchema.Tables.Add(table);

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[Sales].[Orders]",
            Schema = "Sales",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("CREATE TABLE [Sales].[Orders]");
        // 非 dbo Schema 必須先確保 Schema 存在
        script.ApplyScript.Should().Contain("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'Sales')");
        script.ApplyScript.Should().Contain("EXEC(N'CREATE SCHEMA [Sales]')");
    }

    [Fact]
    public void Generate_dbo表格_腳本不應包含CREATE_SCHEMA()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]",
            Schema = "dbo",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert — dbo 一定存在，不需要 CREATE SCHEMA
        script.ApplyScript.Should().NotContain("CREATE SCHEMA [dbo]");
    }

    [Fact]
    public void Generate_非dbo欄位_腳本應使用正確Schema()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準環境" };
        var table = new SchemaTable { Schema = "Sales", Name = "Orders" };
        table.Columns.Add(new SchemaColumn { Name = "Qty", DataType = "INT", IsNullable = false });
        baseSchema.Tables.Add(table);

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[Sales].[Orders].[Qty]",
            Schema = "Sales",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [Sales].[Orders] ADD [Qty]");
    }

    [Fact]
    public void Generate_新增檢視表_腳本應使用EXEC動態執行()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準環境" };
        baseSchema.Views.Add(new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Orders",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_Orders] AS SELECT 1 AS Id"
        });

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.View,
            ObjectName = "[dbo].[vw_Orders]",
            Schema = "dbo",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert — 必須用 EXEC() 包裝，否則在 BEGIN TRY 內 CREATE VIEW 會語法錯誤
        script.ApplyScript.Should().Contain("EXEC(N'CREATE VIEW");
        script.ApplyScript.Should().NotContain("\nCREATE VIEW");
    }

    [Fact]
    public void Generate_修改預存程序_腳本應使用EXEC動態執行()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準環境" };
        baseSchema.StoredProcedures.Add(new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "sp_GetOrders",
            ObjectType = ProgramObjectType.StoredProcedure,
            Definition = "CREATE PROCEDURE [dbo].[sp_GetOrders] AS SELECT 1"
        });

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.StoredProcedure,
            ObjectName = "[dbo].[sp_GetOrders]",
            Schema = "dbo",
            DifferenceType = DifferenceType.Modified,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert — Modified 應改為 ALTER PROCEDURE 並用 EXEC() 包裝
        script.ApplyScript.Should().Contain("EXEC(N'ALTER PROCEDURE");
    }

    [Fact]
    public void Generate_修改有索引的欄位_應先DROP索引再ALTER再RECREATE()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準環境" };
        var table = new SchemaTable { Schema = "dbo", Name = "Orders" };
        table.Columns.Add(new SchemaColumn { Name = "CAL_NAME", DataType = "NVARCHAR", MaxLength = 50, IsNullable = true });
        table.Indexes.Add(new SchemaIndex
        {
            Name = "IX_CAL_NAME",
            IsClustered = false,
            IsUnique = false,
            Columns = ["CAL_NAME"]
        });
        baseSchema.Tables.Add(table);

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Orders].[CAL_NAME]",
            Schema = "dbo",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "100",
            RiskLevel = RiskLevel.Medium
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        var apply = script.ApplyScript;
        var dropPos = apply.IndexOf("DROP INDEX [IX_CAL_NAME]", StringComparison.Ordinal);
        var alterPos = apply.IndexOf("ALTER TABLE [dbo].[Orders] ALTER COLUMN", StringComparison.Ordinal);
        var createPos = apply.IndexOf("CREATE NONCLUSTERED INDEX [IX_CAL_NAME]", StringComparison.Ordinal);

        dropPos.Should().BeGreaterThan(0);
        alterPos.Should().BeGreaterThan(dropPos);   // DROP 在 ALTER 前
        createPos.Should().BeGreaterThan(alterPos); // ALTER 在 RECREATE 前
    }
}
