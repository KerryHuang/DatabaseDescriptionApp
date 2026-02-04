using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// TableStatisticsService 測試
/// </summary>
public class TableStatisticsServiceTests
{
    private readonly ITableStatisticsRepository _repository;
    private readonly TableStatisticsService _service;

    public TableStatisticsServiceTests()
    {
        _repository = Substitute.For<ITableStatisticsRepository>();
        _service = new TableStatisticsService(_repository);
    }

    #region GetAllTableStatisticsAsync 測試

    [Fact]
    public async Task GetAllTableStatisticsAsync_應委派給Repository()
    {
        // Arrange
        var expectedStatistics = new List<TableStatisticsInfo>
        {
            new()
            {
                SchemaName = "dbo",
                TableName = "Users",
                ObjectType = "TABLE",
                ApproximateRowCount = 10000,
                ColumnCount = 15,
                IndexCount = 3,
                ForeignKeyCount = 2,
                DataSizeMB = 50.5m,
                IndexSizeMB = 10.2m,
                TotalSizeMB = 60.7m
            },
            new()
            {
                SchemaName = "dbo",
                TableName = "vw_Users",
                ObjectType = "VIEW",
                ApproximateRowCount = 0,
                ColumnCount = 5,
                IndexCount = 0,
                ForeignKeyCount = 0,
                DataSizeMB = 0,
                IndexSizeMB = 0,
                TotalSizeMB = 0
            }
        };

        _repository.GetAllTableStatisticsAsync(Arg.Any<CancellationToken>())
            .Returns(expectedStatistics);

        // Act
        var result = await _service.GetAllTableStatisticsAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedStatistics);
        await _repository.Received(1).GetAllTableStatisticsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllTableStatisticsAsync_應傳遞CancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _repository.GetAllTableStatisticsAsync(cts.Token)
            .Returns(new List<TableStatisticsInfo>());

        // Act
        await _service.GetAllTableStatisticsAsync(cts.Token);

        // Assert
        await _repository.Received(1).GetAllTableStatisticsAsync(cts.Token);
    }

    [Fact]
    public async Task GetAllTableStatisticsAsync_無資料時_應回傳空集合()
    {
        // Arrange
        _repository.GetAllTableStatisticsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableStatisticsInfo>());

        // Act
        var result = await _service.GetAllTableStatisticsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetExactRowCountAsync 測試

    [Fact]
    public async Task GetExactRowCountAsync_應委派給Repository()
    {
        // Arrange
        _repository.GetExactRowCountAsync("dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(49876L);

        // Act
        var result = await _service.GetExactRowCountAsync("dbo", "Users");

        // Assert
        result.Should().Be(49876L);
        await _repository.Received(1).GetExactRowCountAsync("dbo", "Users", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactRowCountAsync_應傳遞CancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _repository.GetExactRowCountAsync("sales", "Orders", cts.Token)
            .Returns(100000L);

        // Act
        await _service.GetExactRowCountAsync("sales", "Orders", cts.Token);

        // Assert
        await _repository.Received(1).GetExactRowCountAsync("sales", "Orders", cts.Token);
    }

    [Fact]
    public async Task GetExactRowCountAsync_空資料表_應回傳零()
    {
        // Arrange
        _repository.GetExactRowCountAsync("dbo", "EmptyTable", Arg.Any<CancellationToken>())
            .Returns(0L);

        // Act
        var result = await _service.GetExactRowCountAsync("dbo", "EmptyTable");

        // Assert
        result.Should().Be(0L);
    }

    #endregion
}
