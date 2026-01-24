using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Infrastructure.Scripts;

namespace TableSpec.Infrastructure.Tests.Scripts;

/// <summary>
/// 同步腳本產生器測試
/// </summary>
public class SyncScriptGeneratorTests
{
    private readonly SyncScriptGenerator _generator;

    public SyncScriptGeneratorTests()
    {
        _generator = new SyncScriptGenerator();
    }

    #region 新增表格腳本測試

    [Fact]
    public void GenerateScript_新增表格_應該產生CREATE_TABLE語法()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT", IsNullable = false, IsIdentity = true },
                new() { Name = "Name", DataType = "NVARCHAR", MaxLength = 100, IsNullable = false },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200, IsNullable = true }
            }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Users]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, table);

        // Assert
        script.ApplyScript.Should().Contain("CREATE TABLE [dbo].[Users]");
        script.ApplyScript.Should().Contain("[Id] INT");
        script.ApplyScript.Should().Contain("IDENTITY");
        script.ApplyScript.Should().Contain("[Name] NVARCHAR(100) NOT NULL");
        script.ApplyScript.Should().Contain("[Email] NVARCHAR(200) NULL");
    }

    [Fact]
    public void GenerateScript_新增表格_回滾腳本應該是DROP_TABLE()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" }
            }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Users]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, table);

        // Assert
        script.RollbackScript.Should().Contain("DROP TABLE [dbo].[Users]");
    }

    #endregion

    #region 新增欄位腳本測試

    [Fact]
    public void GenerateScript_新增Nullable欄位_應該產生ALTER_TABLE_ADD語法()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            IsNullable = true
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, column, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Users]");
        script.ApplyScript.Should().Contain("ADD [Email] NVARCHAR(200) NULL");
    }

    [Fact]
    public void GenerateScript_新增NotNull欄位有Default_應該包含DEFAULT約束()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Status",
            DataType = "INT",
            IsNullable = false,
            DefaultValue = "0"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Status]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Medium
        };

        // Act
        var script = _generator.GenerateScript(diff, column, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("ADD [Status] INT NOT NULL");
        script.ApplyScript.Should().Contain("DEFAULT 0");
    }

    [Fact]
    public void GenerateScript_新增欄位_回滾腳本應該是DROP_COLUMN()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            IsNullable = true
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, column, "[dbo].[Users]");

        // Assert
        script.RollbackScript.Should().Contain("ALTER TABLE [dbo].[Users]");
        script.RollbackScript.Should().Contain("DROP COLUMN [Email]");
    }

    #endregion

    #region 修改欄位腳本測試

    [Fact]
    public void GenerateScript_修改欄位長度_應該產生ALTER_COLUMN語法()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            IsNullable = true
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "200",
            TargetValue = "100",
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, column, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Users]");
        script.ApplyScript.Should().Contain("ALTER COLUMN [Email] NVARCHAR(200)");
    }

    [Fact]
    public void GenerateScript_修改欄位長度_回滾腳本應該還原原本長度()
    {
        // Arrange
        var column = new SchemaColumn
        {
            Name = "Email",
            DataType = "NVARCHAR",
            MaxLength = 200,
            IsNullable = true
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "200",
            TargetValue = "100",
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, column, "[dbo].[Users]");

        // Assert
        script.RollbackScript.Should().Contain("ALTER COLUMN [Email] NVARCHAR(100)");
    }

    #endregion

    #region 新增索引腳本測試

    [Fact]
    public void GenerateScript_新增索引_應該產生CREATE_INDEX語法()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email",
            IsClustered = false,
            IsUnique = false,
            Columns = new List<string> { "Email" }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Index,
            ObjectName = "[dbo].[Users].[IX_Users_Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, index, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("CREATE NONCLUSTERED INDEX [IX_Users_Email]");
        script.ApplyScript.Should().Contain("ON [dbo].[Users]");
        script.ApplyScript.Should().Contain("([Email])");
    }

    [Fact]
    public void GenerateScript_新增唯一索引_應該包含UNIQUE關鍵字()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "UX_Users_Email",
            IsClustered = false,
            IsUnique = true,
            Columns = new List<string> { "Email" }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Index,
            ObjectName = "[dbo].[Users].[UX_Users_Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, index, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("CREATE UNIQUE NONCLUSTERED INDEX");
    }

    [Fact]
    public void GenerateScript_新增索引含Include欄位_應該包含INCLUDE子句()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email",
            IsClustered = false,
            IsUnique = false,
            Columns = new List<string> { "Email" },
            IncludeColumns = new List<string> { "Name", "CreatedAt" }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Index,
            ObjectName = "[dbo].[Users].[IX_Users_Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, index, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("INCLUDE ([Name], [CreatedAt])");
    }

    [Fact]
    public void GenerateScript_新增篩選索引_應該包含WHERE子句()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email_Active",
            IsClustered = false,
            IsUnique = false,
            Columns = new List<string> { "Email" },
            FilterDefinition = "[IsDeleted] = 0"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Index,
            ObjectName = "[dbo].[Users].[IX_Users_Email_Active]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, index, "[dbo].[Users]");

        // Assert
        script.ApplyScript.Should().Contain("WHERE [IsDeleted] = 0");
    }

    [Fact]
    public void GenerateScript_新增索引_回滾腳本應該是DROP_INDEX()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email",
            Columns = new List<string> { "Email" }
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Index,
            ObjectName = "[dbo].[Users].[IX_Users_Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, index, "[dbo].[Users]");

        // Assert
        script.RollbackScript.Should().Contain("DROP INDEX [IX_Users_Email] ON [dbo].[Users]");
    }

    #endregion

    #region 程式物件腳本測試

    [Fact]
    public void GenerateScript_新增View_應該產生CREATE_VIEW語法()
    {
        // Arrange
        var view = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_ActiveUsers",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_ActiveUsers] AS SELECT * FROM Users WHERE IsActive = 1"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.View,
            ObjectName = "[dbo].[vw_ActiveUsers]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, view);

        // Assert
        script.ApplyScript.Should().Contain("CREATE VIEW [dbo].[vw_ActiveUsers]");
    }

    [Fact]
    public void GenerateScript_修改View_應該產生ALTER_VIEW語法()
    {
        // Arrange
        var view = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_ActiveUsers",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_ActiveUsers] AS SELECT Id, Name FROM Users WHERE IsActive = 1"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.View,
            ObjectName = "[dbo].[vw_ActiveUsers]",
            DifferenceType = DifferenceType.Modified,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, view);

        // Assert
        script.ApplyScript.Should().Contain("ALTER VIEW [dbo].[vw_ActiveUsers]");
    }

    [Fact]
    public void GenerateScript_新增SP_應該產生CREATE_PROCEDURE語法()
    {
        // Arrange
        var sp = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "sp_GetUsers",
            ObjectType = ProgramObjectType.StoredProcedure,
            Definition = "CREATE PROCEDURE [dbo].[sp_GetUsers] AS SELECT * FROM Users"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.StoredProcedure,
            ObjectName = "[dbo].[sp_GetUsers]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, sp);

        // Assert
        script.ApplyScript.Should().Contain("CREATE PROCEDURE [dbo].[sp_GetUsers]");
    }

    [Fact]
    public void GenerateScript_新增View_回滾腳本應該是DROP_VIEW()
    {
        // Arrange
        var view = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_ActiveUsers",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_ActiveUsers] AS SELECT * FROM Users"
        };

        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.View,
            ObjectName = "[dbo].[vw_ActiveUsers]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.GenerateScript(diff, view);

        // Assert
        script.RollbackScript.Should().Contain("DROP VIEW [dbo].[vw_ActiveUsers]");
    }

    #endregion

    #region 批次產生腳本測試

    [Fact]
    public void GenerateBatchScript_多個差異_應該合併為單一腳本()
    {
        // Arrange
        var differences = new List<(SchemaDifference Diff, object Context, string? TableName)>
        {
            (
                new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Email]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                },
                new SchemaColumn { Name = "Email", DataType = "NVARCHAR", MaxLength = 200, IsNullable = true },
                "[dbo].[Users]"
            ),
            (
                new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Index,
                    ObjectName = "[dbo].[Users].[IX_Users_Email]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                },
                new SchemaIndex { Name = "IX_Users_Email", Columns = new List<string> { "Email" } },
                "[dbo].[Users]"
            )
        };

        // Act
        var script = _generator.GenerateBatchScript("測試環境", differences);

        // Assert
        script.TargetEnvironment.Should().Be("測試環境");
        script.ApplyScript.Should().Contain("ADD [Email]");
        script.ApplyScript.Should().Contain("CREATE");
        script.ApplyScript.Should().Contain("INDEX");
        script.Differences.Should().HaveCount(2);
    }

    [Fact]
    public void GenerateBatchScript_應該包含GO分隔符()
    {
        // Arrange
        var differences = new List<(SchemaDifference Diff, object Context, string? TableName)>
        {
            (
                new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Col1]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                },
                new SchemaColumn { Name = "Col1", DataType = "INT", IsNullable = true },
                "[dbo].[Users]"
            ),
            (
                new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Col2]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                },
                new SchemaColumn { Name = "Col2", DataType = "INT", IsNullable = true },
                "[dbo].[Users]"
            )
        };

        // Act
        var script = _generator.GenerateBatchScript("測試環境", differences);

        // Assert
        script.ApplyScript.Should().Contain("GO");
    }

    #endregion
}
