using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 程式物件（View、SP、Function、Trigger）結構測試
/// </summary>
public class SchemaProgramObjectTests
{
    [Fact]
    public void SchemaProgramObject_應該能建立View()
    {
        // Arrange & Act
        var view = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_ActiveUsers",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_ActiveUsers] AS SELECT * FROM Users WHERE IsActive = 1"
        };

        // Assert
        view.Schema.Should().Be("dbo");
        view.Name.Should().Be("vw_ActiveUsers");
        view.ObjectType.Should().Be(ProgramObjectType.View);
        view.Definition.Should().Contain("CREATE VIEW");
    }

    [Fact]
    public void SchemaProgramObject_應該能建立StoredProcedure()
    {
        // Arrange & Act
        var sp = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "sp_GetUserById",
            ObjectType = ProgramObjectType.StoredProcedure,
            Definition = "CREATE PROCEDURE [dbo].[sp_GetUserById] @Id INT AS SELECT * FROM Users WHERE Id = @Id"
        };

        // Assert
        sp.ObjectType.Should().Be(ProgramObjectType.StoredProcedure);
    }

    [Fact]
    public void SchemaProgramObject_應該能建立Function()
    {
        // Arrange & Act
        var func = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "fn_GetFullName",
            ObjectType = ProgramObjectType.Function,
            Definition = "CREATE FUNCTION [dbo].[fn_GetFullName](@FirstName NVARCHAR(50), @LastName NVARCHAR(50)) RETURNS NVARCHAR(100) AS BEGIN RETURN @FirstName + ' ' + @LastName END"
        };

        // Assert
        func.ObjectType.Should().Be(ProgramObjectType.Function);
    }

    [Fact]
    public void SchemaProgramObject_應該能建立Trigger()
    {
        // Arrange & Act
        var trigger = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "tr_Users_Audit",
            ObjectType = ProgramObjectType.Trigger,
            Definition = "CREATE TRIGGER [dbo].[tr_Users_Audit] ON [dbo].[Users] AFTER INSERT, UPDATE AS BEGIN ... END"
        };

        // Assert
        trigger.ObjectType.Should().Be(ProgramObjectType.Trigger);
    }

    [Fact]
    public void FullName_應該傳回Schema和名稱的組合()
    {
        // Arrange
        var obj = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Report"
        };

        // Act
        var fullName = obj.FullName;

        // Assert
        fullName.Should().Be("[dbo].[vw_Report]");
    }

    [Fact]
    public void GetNormalizedDefinition_應該移除格式差異後比較()
    {
        // Arrange
        var obj1 = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Test",
            Definition = "CREATE VIEW [dbo].[vw_Test]\nAS\n  SELECT *   FROM Users"
        };

        var obj2 = new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Test",
            Definition = "CREATE VIEW [dbo].[vw_Test] AS SELECT * FROM Users"
        };

        // Act
        var norm1 = obj1.GetNormalizedDefinition();
        var norm2 = obj2.GetNormalizedDefinition();

        // Assert - 正規化後應該相等（忽略空白差異）
        norm1.Should().Be(norm2);
    }

    [Fact]
    public void ProgramObjectType_列舉應該有四種類型()
    {
        // Arrange & Act
        var values = Enum.GetValues<ProgramObjectType>();

        // Assert
        values.Should().HaveCount(4);
        values.Should().Contain(ProgramObjectType.View);
        values.Should().Contain(ProgramObjectType.StoredProcedure);
        values.Should().Contain(ProgramObjectType.Function);
        values.Should().Contain(ProgramObjectType.Trigger);
    }
}
