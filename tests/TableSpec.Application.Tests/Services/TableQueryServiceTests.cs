using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// TableQueryService 測試
/// </summary>
public class TableQueryServiceTests
{
    private readonly ITableRepository _tableRepository;
    private readonly IColumnRepository _columnRepository;
    private readonly IIndexRepository _indexRepository;
    private readonly IRelationRepository _relationRepository;
    private readonly IParameterRepository _parameterRepository;
    private readonly TableQueryService _service;

    public TableQueryServiceTests()
    {
        _tableRepository = Substitute.For<ITableRepository>();
        _columnRepository = Substitute.For<IColumnRepository>();
        _indexRepository = Substitute.For<IIndexRepository>();
        _relationRepository = Substitute.For<IRelationRepository>();
        _parameterRepository = Substitute.For<IParameterRepository>();

        _service = new TableQueryService(
            _tableRepository,
            _columnRepository,
            _indexRepository,
            _relationRepository,
            _parameterRepository);
    }

    #region GetAllTablesAsync 測試

    [Fact]
    public async Task GetAllTablesAsync_應委派給TableRepository()
    {
        // Arrange
        var expectedTables = new List<TableInfo>
        {
            new() { Type = "TABLE", Schema = "dbo", Name = "Users" },
            new() { Type = "VIEW", Schema = "dbo", Name = "vw_Users" }
        };
        _tableRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(expectedTables);

        // Act
        var result = await _service.GetAllTablesAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedTables);
        await _tableRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllTablesAsync_應傳遞CancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _tableRepository.GetAllAsync(cts.Token)
            .Returns(new List<TableInfo>());

        // Act
        await _service.GetAllTablesAsync(cts.Token);

        // Assert
        await _tableRepository.Received(1).GetAllAsync(cts.Token);
    }

    #endregion

    #region GetTablesByTypeAsync 測試

    [Fact]
    public async Task GetTablesByTypeAsync_應委派給TableRepository()
    {
        // Arrange
        var expectedTables = new List<TableInfo>
        {
            new() { Type = "TABLE", Schema = "dbo", Name = "Orders" }
        };
        _tableRepository.GetByTypeAsync("TABLE", Arg.Any<CancellationToken>())
            .Returns(expectedTables);

        // Act
        var result = await _service.GetTablesByTypeAsync("TABLE");

        // Assert
        result.Should().BeEquivalentTo(expectedTables);
        await _tableRepository.Received(1).GetByTypeAsync("TABLE", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetColumnsAsync 測試

    [Fact]
    public async Task GetColumnsAsync_應委派給ColumnRepository()
    {
        // Arrange
        var expectedColumns = new List<ColumnInfo>
        {
            new() { Schema = "dbo", TableName = "Users", ColumnName = "Id", DataType = "int" },
            new() { Schema = "dbo", TableName = "Users", ColumnName = "Name", DataType = "nvarchar" }
        };
        _columnRepository.GetColumnsAsync("TABLE", "dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(expectedColumns);

        // Act
        var result = await _service.GetColumnsAsync("TABLE", "dbo", "Users");

        // Assert
        result.Should().BeEquivalentTo(expectedColumns);
        await _columnRepository.Received(1)
            .GetColumnsAsync("TABLE", "dbo", "Users", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetColumnsAsync_View類型_應正確傳遞()
    {
        // Arrange
        _columnRepository.GetColumnsAsync("VIEW", "dbo", "vw_Users", Arg.Any<CancellationToken>())
            .Returns(new List<ColumnInfo>());

        // Act
        await _service.GetColumnsAsync("VIEW", "dbo", "vw_Users");

        // Assert
        await _columnRepository.Received(1)
            .GetColumnsAsync("VIEW", "dbo", "vw_Users", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetIndexesAsync 測試

    [Fact]
    public async Task GetIndexesAsync_應委派給IndexRepository()
    {
        // Arrange
        var expectedIndexes = new List<IndexInfo>
        {
            new() { Name = "PK_Users", Type = "Clustered", Columns = new[] { "Id" }, IsPrimaryKey = true }
        };
        _indexRepository.GetIndexesAsync("dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(expectedIndexes);

        // Act
        var result = await _service.GetIndexesAsync("dbo", "Users");

        // Assert
        result.Should().BeEquivalentTo(expectedIndexes);
        await _indexRepository.Received(1)
            .GetIndexesAsync("dbo", "Users", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetRelationsAsync 測試

    [Fact]
    public async Task GetRelationsAsync_應委派給RelationRepository()
    {
        // Arrange
        var expectedRelations = new List<RelationInfo>
        {
            new()
            {
                ConstraintName = "FK_Orders_Users",
                FromTable = "Orders",
                FromColumn = "UserId",
                ToTable = "Users",
                ToColumn = "Id",
                Type = RelationType.Outgoing
            }
        };
        _relationRepository.GetRelationsAsync("dbo", "Orders", Arg.Any<CancellationToken>())
            .Returns(expectedRelations);

        // Act
        var result = await _service.GetRelationsAsync("dbo", "Orders");

        // Assert
        result.Should().BeEquivalentTo(expectedRelations);
        await _relationRepository.Received(1)
            .GetRelationsAsync("dbo", "Orders", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetParametersAsync 測試

    [Fact]
    public async Task GetParametersAsync_應委派給ParameterRepository()
    {
        // Arrange
        var expectedParameters = new List<ParameterInfo>
        {
            new() { Name = "@Id", DataType = "int", IsOutput = false, Ordinal = 1 },
            new() { Name = "@Name", DataType = "nvarchar", Length = 100, IsOutput = false, Ordinal = 2 }
        };
        _parameterRepository.GetParametersAsync("dbo", "sp_GetUser", Arg.Any<CancellationToken>())
            .Returns(expectedParameters);

        // Act
        var result = await _service.GetParametersAsync("dbo", "sp_GetUser");

        // Assert
        result.Should().BeEquivalentTo(expectedParameters);
        await _parameterRepository.Received(1)
            .GetParametersAsync("dbo", "sp_GetUser", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetDefinitionAsync 測試

    [Fact]
    public async Task GetDefinitionAsync_應委派給ParameterRepository()
    {
        // Arrange
        var expectedDefinition = "CREATE PROCEDURE [dbo].[sp_GetUser] @Id INT AS SELECT * FROM Users WHERE Id = @Id";
        _parameterRepository.GetDefinitionAsync("dbo", "sp_GetUser", Arg.Any<CancellationToken>())
            .Returns(expectedDefinition);

        // Act
        var result = await _service.GetDefinitionAsync("dbo", "sp_GetUser");

        // Assert
        result.Should().Be(expectedDefinition);
        await _parameterRepository.Received(1)
            .GetDefinitionAsync("dbo", "sp_GetUser", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDefinitionAsync_物件不存在_應回傳Null()
    {
        // Arrange
        _parameterRepository.GetDefinitionAsync("dbo", "nonexistent", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        var result = await _service.GetDefinitionAsync("dbo", "nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region UpdateColumnDescriptionAsync 測試

    [Fact]
    public async Task UpdateColumnDescriptionAsync_應委派給ColumnRepository()
    {
        // Arrange & Act
        await _service.UpdateColumnDescriptionAsync("dbo", "Users", "Name", "使用者名稱");

        // Assert
        await _columnRepository.Received(1)
            .UpdateColumnDescriptionAsync("dbo", "Users", "Name", "使用者名稱", "TABLE", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateColumnDescriptionAsync_View類型_應正確傳遞()
    {
        // Arrange & Act
        await _service.UpdateColumnDescriptionAsync("dbo", "vw_Users", "Email", "電子郵件", "VIEW");

        // Assert
        await _columnRepository.Received(1)
            .UpdateColumnDescriptionAsync("dbo", "vw_Users", "Email", "電子郵件", "VIEW", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateColumnDescriptionAsync_Description為Null_應允許()
    {
        // Arrange & Act
        await _service.UpdateColumnDescriptionAsync("dbo", "Users", "TempField", null);

        // Assert
        await _columnRepository.Received(1)
            .UpdateColumnDescriptionAsync("dbo", "Users", "TempField", null, "TABLE", Arg.Any<CancellationToken>());
    }

    #endregion
}
