using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 索引結構實體測試
/// </summary>
public class SchemaIndexTests
{
    [Fact]
    public void SchemaIndex_應該能建立具有所有屬性的實例()
    {
        // Arrange & Act
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email",
            IsClustered = false,
            IsUnique = true,
            Columns = new List<string> { "Email" },
            IncludeColumns = new List<string> { "Name", "CreatedAt" },
            FilterDefinition = "[IsDeleted] = 0"
        };

        // Assert
        index.Name.Should().Be("IX_Users_Email");
        index.IsClustered.Should().BeFalse();
        index.IsUnique.Should().BeTrue();
        index.Columns.Should().ContainSingle().Which.Should().Be("Email");
        index.IncludeColumns.Should().HaveCount(2);
        index.FilterDefinition.Should().Be("[IsDeleted] = 0");
    }

    [Fact]
    public void IndexType_Clustered_應該傳回正確類型()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "PK_Users",
            IsClustered = true,
            IsUnique = true,
            Columns = new List<string> { "Id" }
        };

        // Act
        var indexType = index.GetIndexType();

        // Assert
        indexType.Should().Be("CLUSTERED UNIQUE");
    }

    [Fact]
    public void IndexType_NonClustered_應該傳回正確類型()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Users_Email",
            IsClustered = false,
            IsUnique = false,
            Columns = new List<string> { "Email" }
        };

        // Act
        var indexType = index.GetIndexType();

        // Assert
        indexType.Should().Be("NONCLUSTERED");
    }

    [Fact]
    public void GetColumnsSignature_應該傳回欄位組合字串()
    {
        // Arrange
        var index = new SchemaIndex
        {
            Name = "IX_Orders_CustomerDate",
            Columns = new List<string> { "CustomerId", "OrderDate" }
        };

        // Act
        var signature = index.GetColumnsSignature();

        // Assert
        signature.Should().Be("CustomerId, OrderDate");
    }

    [Fact]
    public void Equals_相同屬性的索引應該相等()
    {
        // Arrange
        var index1 = CreateTestIndex();
        var index2 = CreateTestIndex();

        // Act & Assert
        index1.Should().BeEquivalentTo(index2);
    }

    private static SchemaIndex CreateTestIndex()
    {
        return new SchemaIndex
        {
            Name = "IX_Users_Email",
            IsClustered = false,
            IsUnique = true,
            Columns = new List<string> { "Email" },
            IncludeColumns = new List<string>(),
            FilterDefinition = null
        };
    }
}
