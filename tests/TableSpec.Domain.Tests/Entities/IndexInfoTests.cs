using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// IndexInfo 實體測試
/// </summary>
public class IndexInfoTests
{
    [Fact]
    public void IndexInfo_建立主鍵索引_屬性應正確設定()
    {
        // Arrange & Act
        var index = new IndexInfo
        {
            Name = "PK_Users",
            Type = "Clustered",
            Columns = new List<string> { "Id" },
            IsUnique = true,
            IsPrimaryKey = true
        };

        // Assert
        index.Name.Should().Be("PK_Users");
        index.Type.Should().Be("Clustered");
        index.Columns.Should().ContainSingle().Which.Should().Be("Id");
        index.IsUnique.Should().BeTrue();
        index.IsPrimaryKey.Should().BeTrue();
    }

    [Fact]
    public void IndexInfo_建立非叢集索引_屬性應正確設定()
    {
        // Arrange & Act
        var index = new IndexInfo
        {
            Name = "IX_Users_Email",
            Type = "NonClustered",
            Columns = new List<string> { "Email" },
            IsUnique = true,
            IsPrimaryKey = false
        };

        // Assert
        index.Name.Should().Be("IX_Users_Email");
        index.Type.Should().Be("NonClustered");
        index.IsUnique.Should().BeTrue();
        index.IsPrimaryKey.Should().BeFalse();
    }

    [Fact]
    public void IndexInfo_複合索引_應包含多個欄位()
    {
        // Arrange & Act
        var index = new IndexInfo
        {
            Name = "IX_Orders_CustomerDate",
            Type = "NonClustered",
            Columns = new List<string> { "CustomerId", "OrderDate" },
            IsUnique = false,
            IsPrimaryKey = false
        };

        // Assert
        index.Columns.Should().HaveCount(2);
        index.Columns.Should().ContainInOrder("CustomerId", "OrderDate");
    }

    [Fact]
    public void IndexInfo_非唯一索引_IsUnique應為False()
    {
        // Arrange & Act
        var index = new IndexInfo
        {
            Name = "IX_Products_Category",
            Type = "NonClustered",
            Columns = new List<string> { "CategoryId" },
            IsUnique = false,
            IsPrimaryKey = false
        };

        // Assert
        index.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void IndexInfo_Columns應為唯讀集合()
    {
        // Arrange
        var columns = new List<string> { "Col1", "Col2" };

        // Act
        var index = new IndexInfo
        {
            Name = "IX_Test",
            Type = "NonClustered",
            Columns = columns
        };

        // Assert
        index.Columns.Should().BeAssignableTo<IReadOnlyList<string>>();
        index.Columns.Should().HaveCount(2);
    }

    [Fact]
    public void IndexInfo_叢集與非叢集類型_應正確區分()
    {
        // Arrange & Act
        var clustered = new IndexInfo
        {
            Name = "PK_Table",
            Type = "Clustered",
            Columns = new List<string> { "Id" }
        };

        var nonClustered = new IndexInfo
        {
            Name = "IX_Table",
            Type = "NonClustered",
            Columns = new List<string> { "Name" }
        };

        // Assert
        clustered.Type.Should().Be("Clustered");
        nonClustered.Type.Should().Be("NonClustered");
        clustered.Type.Should().NotBe(nonClustered.Type);
    }
}
