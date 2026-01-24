using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 約束結構實體測試
/// </summary>
public class SchemaConstraintTests
{
    [Fact]
    public void PrimaryKey_應該能建立正確的主鍵約束()
    {
        // Arrange & Act
        var pk = new SchemaConstraint
        {
            Name = "PK_Users",
            ConstraintType = ConstraintType.PrimaryKey,
            Columns = new List<string> { "Id" }
        };

        // Assert
        pk.Name.Should().Be("PK_Users");
        pk.ConstraintType.Should().Be(ConstraintType.PrimaryKey);
        pk.Columns.Should().ContainSingle().Which.Should().Be("Id");
    }

    [Fact]
    public void ForeignKey_應該包含參照資訊()
    {
        // Arrange & Act
        var fk = new SchemaConstraint
        {
            Name = "FK_Orders_Users",
            ConstraintType = ConstraintType.ForeignKey,
            Columns = new List<string> { "UserId" },
            ReferencedTable = "Users",
            ReferencedColumns = new List<string> { "Id" },
            OnDeleteAction = "CASCADE",
            OnUpdateAction = "NO ACTION"
        };

        // Assert
        fk.ConstraintType.Should().Be(ConstraintType.ForeignKey);
        fk.ReferencedTable.Should().Be("Users");
        fk.ReferencedColumns.Should().ContainSingle().Which.Should().Be("Id");
        fk.OnDeleteAction.Should().Be("CASCADE");
        fk.OnUpdateAction.Should().Be("NO ACTION");
    }

    [Fact]
    public void UniqueConstraint_應該能建立唯一約束()
    {
        // Arrange & Act
        var unique = new SchemaConstraint
        {
            Name = "UQ_Users_Email",
            ConstraintType = ConstraintType.Unique,
            Columns = new List<string> { "Email" }
        };

        // Assert
        unique.ConstraintType.Should().Be(ConstraintType.Unique);
    }

    [Fact]
    public void CheckConstraint_應該包含定義內容()
    {
        // Arrange & Act
        var check = new SchemaConstraint
        {
            Name = "CK_Users_Age",
            ConstraintType = ConstraintType.Check,
            Definition = "[Age] >= 0 AND [Age] <= 150"
        };

        // Assert
        check.ConstraintType.Should().Be(ConstraintType.Check);
        check.Definition.Should().Be("[Age] >= 0 AND [Age] <= 150");
    }

    [Fact]
    public void Default_應該包含預設值定義()
    {
        // Arrange & Act
        var defaultConstraint = new SchemaConstraint
        {
            Name = "DF_Users_CreatedAt",
            ConstraintType = ConstraintType.Default,
            Columns = new List<string> { "CreatedAt" },
            Definition = "GETDATE()"
        };

        // Assert
        defaultConstraint.ConstraintType.Should().Be(ConstraintType.Default);
        defaultConstraint.Definition.Should().Be("GETDATE()");
    }

    [Fact]
    public void ConstraintType_列舉應該有五種類型()
    {
        // Arrange & Act
        var values = Enum.GetValues<ConstraintType>();

        // Assert
        values.Should().HaveCount(5);
        values.Should().Contain(ConstraintType.PrimaryKey);
        values.Should().Contain(ConstraintType.ForeignKey);
        values.Should().Contain(ConstraintType.Unique);
        values.Should().Contain(ConstraintType.Check);
        values.Should().Contain(ConstraintType.Default);
    }
}
