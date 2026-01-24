using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 表格結構實體測試
/// </summary>
public class SchemaTableTests
{
    [Fact]
    public void SchemaTable_應該能建立具有所有屬性的實例()
    {
        // Arrange & Act
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT", IsIdentity = true },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            },
            Indexes = new List<SchemaIndex>(),
            Constraints = new List<SchemaConstraint>()
        };

        // Assert
        table.Schema.Should().Be("dbo");
        table.Name.Should().Be("Users");
        table.Columns.Should().HaveCount(2);
    }

    [Fact]
    public void FullName_應該傳回Schema和表名的組合()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users"
        };

        // Act
        var fullName = table.FullName;

        // Assert
        fullName.Should().Be("[dbo].[Users]");
    }

    [Fact]
    public void GetColumn_存在的欄位應該傳回該欄位()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        };

        // Act
        var column = table.GetColumn("Email");

        // Assert
        column.Should().NotBeNull();
        column!.Name.Should().Be("Email");
    }

    [Fact]
    public void GetColumn_不存在的欄位應該傳回null()
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

        // Act
        var column = table.GetColumn("NonExistent");

        // Assert
        column.Should().BeNull();
    }

    [Fact]
    public void GetPrimaryKey_應該傳回主鍵約束()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Constraints = new List<SchemaConstraint>
            {
                new()
                {
                    Name = "PK_Users",
                    ConstraintType = ConstraintType.PrimaryKey,
                    Columns = new List<string> { "Id" }
                },
                new()
                {
                    Name = "FK_Users_Roles",
                    ConstraintType = ConstraintType.ForeignKey,
                    Columns = new List<string> { "RoleId" }
                }
            }
        };

        // Act
        var pk = table.GetPrimaryKey();

        // Assert
        pk.Should().NotBeNull();
        pk!.Name.Should().Be("PK_Users");
    }

    [Fact]
    public void GetForeignKeys_應該傳回所有外鍵約束()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "dbo",
            Name = "Orders",
            Constraints = new List<SchemaConstraint>
            {
                new()
                {
                    Name = "PK_Orders",
                    ConstraintType = ConstraintType.PrimaryKey,
                    Columns = new List<string> { "Id" }
                },
                new()
                {
                    Name = "FK_Orders_Users",
                    ConstraintType = ConstraintType.ForeignKey,
                    Columns = new List<string> { "UserId" }
                },
                new()
                {
                    Name = "FK_Orders_Products",
                    ConstraintType = ConstraintType.ForeignKey,
                    Columns = new List<string> { "ProductId" }
                }
            }
        };

        // Act
        var fks = table.GetForeignKeys();

        // Assert
        fks.Should().HaveCount(2);
        fks.Select(fk => fk.Name).Should().Contain("FK_Orders_Users", "FK_Orders_Products");
    }
}
