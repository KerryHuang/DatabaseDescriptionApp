using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// RelationInfo 實體測試
/// </summary>
public class RelationInfoTests
{
    [Fact]
    public void RelationInfo_建立Outgoing關聯_屬性應正確設定()
    {
        // Arrange & Act
        var relation = new RelationInfo
        {
            ConstraintName = "FK_Orders_Customers",
            FromTable = "Orders",
            FromColumn = "CustomerId",
            ToTable = "Customers",
            ToColumn = "Id",
            Type = RelationType.Outgoing
        };

        // Assert
        relation.ConstraintName.Should().Be("FK_Orders_Customers");
        relation.FromTable.Should().Be("Orders");
        relation.FromColumn.Should().Be("CustomerId");
        relation.ToTable.Should().Be("Customers");
        relation.ToColumn.Should().Be("Id");
        relation.Type.Should().Be(RelationType.Outgoing);
    }

    [Fact]
    public void RelationInfo_建立Incoming關聯_屬性應正確設定()
    {
        // Arrange & Act
        var relation = new RelationInfo
        {
            ConstraintName = "FK_Orders_Customers",
            FromTable = "Orders",
            FromColumn = "CustomerId",
            ToTable = "Customers",
            ToColumn = "Id",
            Type = RelationType.Incoming
        };

        // Assert
        relation.Type.Should().Be(RelationType.Incoming);
    }

    [Fact]
    public void RelationType_Outgoing_表示本表參考其他表()
    {
        // Arrange & Act
        var relationType = RelationType.Outgoing;

        // Assert
        relationType.Should().Be(RelationType.Outgoing);
        ((int)relationType).Should().Be(0);
    }

    [Fact]
    public void RelationType_Incoming_表示其他表參考本表()
    {
        // Arrange & Act
        var relationType = RelationType.Incoming;

        // Assert
        relationType.Should().Be(RelationType.Incoming);
        ((int)relationType).Should().Be(1);
    }

    [Fact]
    public void RelationInfo_同一約束名稱不同方向_應正確區分()
    {
        // Arrange & Act
        // 從 Orders 表的角度看，這是 Outgoing
        var outgoingRelation = new RelationInfo
        {
            ConstraintName = "FK_Orders_Products",
            FromTable = "Orders",
            FromColumn = "ProductId",
            ToTable = "Products",
            ToColumn = "Id",
            Type = RelationType.Outgoing
        };

        // 從 Products 表的角度看，同一個 FK 是 Incoming
        var incomingRelation = new RelationInfo
        {
            ConstraintName = "FK_Orders_Products",
            FromTable = "Orders",
            FromColumn = "ProductId",
            ToTable = "Products",
            ToColumn = "Id",
            Type = RelationType.Incoming
        };

        // Assert
        outgoingRelation.ConstraintName.Should().Be(incomingRelation.ConstraintName);
        outgoingRelation.Type.Should().NotBe(incomingRelation.Type);
    }

    [Fact]
    public void RelationInfo_自我參考_FromTable和ToTable可相同()
    {
        // Arrange & Act
        var selfReference = new RelationInfo
        {
            ConstraintName = "FK_Employees_Manager",
            FromTable = "Employees",
            FromColumn = "ManagerId",
            ToTable = "Employees",
            ToColumn = "Id",
            Type = RelationType.Outgoing
        };

        // Assert
        selfReference.FromTable.Should().Be(selfReference.ToTable);
        selfReference.FromColumn.Should().NotBe(selfReference.ToColumn);
    }
}
