using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// ConstraintInfo 實體測試
/// </summary>
public class ConstraintInfoTests
{
    #region 基本屬性測試

    [Fact]
    public void ConstraintInfo_建立主鍵約束_屬性應正確設定()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "PK_Users",
            Type = ConstraintType.PrimaryKey,
            SchemaName = "dbo",
            TableName = "Users",
            Columns = ["Id"],
            IsClustered = true
        };

        // Assert
        constraint.ConstraintName.Should().Be("PK_Users");
        constraint.Type.Should().Be(ConstraintType.PrimaryKey);
        constraint.SchemaName.Should().Be("dbo");
        constraint.TableName.Should().Be("Users");
        constraint.Columns.Should().ContainSingle().Which.Should().Be("Id");
        constraint.IsClustered.Should().BeTrue();
    }

    [Fact]
    public void ConstraintInfo_建立外鍵約束_應包含參考資訊()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "FK_Orders_Users",
            Type = ConstraintType.ForeignKey,
            SchemaName = "dbo",
            TableName = "Orders",
            Columns = ["UserId"],
            ReferencedSchema = "dbo",
            ReferencedTable = "Users",
            ReferencedColumn = "Id"
        };

        // Assert
        constraint.Type.Should().Be(ConstraintType.ForeignKey);
        constraint.ReferencedSchema.Should().Be("dbo");
        constraint.ReferencedTable.Should().Be("Users");
        constraint.ReferencedColumn.Should().Be("Id");
    }

    [Fact]
    public void ConstraintInfo_建立唯一索引_屬性應正確設定()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "UQ_Users_Email",
            Type = ConstraintType.UniqueIndex,
            SchemaName = "dbo",
            TableName = "Users",
            Columns = ["Email"],
            IsUnique = true,
            IsClustered = false
        };

        // Assert
        constraint.Type.Should().Be(ConstraintType.UniqueIndex);
        constraint.IsUnique.Should().BeTrue();
        constraint.IsClustered.Should().BeFalse();
    }

    [Fact]
    public void ConstraintInfo_建立非叢集索引_屬性應正確設定()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "IX_Orders_OrderDate",
            Type = ConstraintType.NonClusteredIndex,
            SchemaName = "dbo",
            TableName = "Orders",
            Columns = ["OrderDate"],
            IsUnique = false,
            IsClustered = false
        };

        // Assert
        constraint.Type.Should().Be(ConstraintType.NonClusteredIndex);
        constraint.IsUnique.Should().BeFalse();
        constraint.IsClustered.Should().BeFalse();
    }

    #endregion

    #region 複合索引測試

    [Fact]
    public void ConstraintInfo_複合索引_應包含多個欄位()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "IX_Orders_Customer_Date",
            Type = ConstraintType.NonClusteredIndex,
            SchemaName = "dbo",
            TableName = "Orders",
            Columns = ["CustomerId", "OrderDate"]
        };

        // Assert
        constraint.Columns.Should().HaveCount(2);
        constraint.Columns.Should().ContainInOrder("CustomerId", "OrderDate");
    }

    [Fact]
    public void ConstraintInfo_複合主鍵_應包含多個欄位()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "PK_OrderDetails",
            Type = ConstraintType.PrimaryKey,
            SchemaName = "dbo",
            TableName = "OrderDetails",
            Columns = ["OrderId", "ProductId"]
        };

        // Assert
        constraint.Type.Should().Be(ConstraintType.PrimaryKey);
        constraint.Columns.Should().HaveCount(2);
    }

    #endregion

    #region SQL 腳本測試

    [Fact]
    public void ConstraintInfo_應包含DropSql()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "PK_Users",
            Type = ConstraintType.PrimaryKey,
            SchemaName = "dbo",
            TableName = "Users",
            Columns = ["Id"],
            DropSql = "ALTER TABLE [dbo].[Users] DROP CONSTRAINT [PK_Users]"
        };

        // Assert
        constraint.DropSql.Should().Contain("DROP CONSTRAINT");
        constraint.DropSql.Should().Contain("PK_Users");
    }

    [Fact]
    public void ConstraintInfo_應包含CreateSql()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            ConstraintName = "PK_Users",
            Type = ConstraintType.PrimaryKey,
            SchemaName = "dbo",
            TableName = "Users",
            Columns = ["Id"],
            CreateSql = "ALTER TABLE [dbo].[Users] ADD CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id])"
        };

        // Assert
        constraint.CreateSql.Should().Contain("ADD CONSTRAINT");
        constraint.CreateSql.Should().Contain("PRIMARY KEY");
    }

    #endregion

    #region ConstraintType 枚舉測試

    [Fact]
    public void ConstraintType_PrimaryKey_值應為0()
    {
        ((int)ConstraintType.PrimaryKey).Should().Be(0);
    }

    [Fact]
    public void ConstraintType_ForeignKey_值應為1()
    {
        ((int)ConstraintType.ForeignKey).Should().Be(1);
    }

    [Fact]
    public void ConstraintType_UniqueIndex_值應為2()
    {
        ((int)ConstraintType.UniqueIndex).Should().Be(2);
    }

    [Fact]
    public void ConstraintType_NonClusteredIndex_值應為3()
    {
        ((int)ConstraintType.NonClusteredIndex).Should().Be(3);
    }

    #endregion

    #region 預設值測試

    [Fact]
    public void ConstraintInfo_預設值_Columns應為空集合()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo();

        // Assert
        constraint.Columns.Should().BeEmpty();
    }

    [Fact]
    public void ConstraintInfo_預設值_字串屬性應為空字串()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo();

        // Assert
        constraint.ConstraintName.Should().BeEmpty();
        constraint.SchemaName.Should().BeEmpty();
        constraint.TableName.Should().BeEmpty();
        constraint.DropSql.Should().BeEmpty();
        constraint.CreateSql.Should().BeEmpty();
    }

    [Fact]
    public void ConstraintInfo_FK參考屬性_可為Null()
    {
        // Arrange & Act
        var constraint = new ConstraintInfo
        {
            Type = ConstraintType.PrimaryKey
        };

        // Assert
        constraint.ReferencedSchema.Should().BeNull();
        constraint.ReferencedTable.Should().BeNull();
        constraint.ReferencedColumn.Should().BeNull();
    }

    #endregion
}
