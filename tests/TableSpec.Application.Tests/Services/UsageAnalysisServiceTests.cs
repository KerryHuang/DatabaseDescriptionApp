using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// UsageAnalysisService 測試
/// </summary>
public class UsageAnalysisServiceTests
{
    private readonly IUsageAnalysisRepository _repository;
    private readonly UsageAnalysisService _service;

    public UsageAnalysisServiceTests()
    {
        _repository = Substitute.For<IUsageAnalysisRepository>();
        _service = new UsageAnalysisService(_repository);
    }

    [Fact]
    public async Task ScanAsync_應回傳表和欄位使用狀態()
    {
        // Arrange
        var tables = new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
            new() { SchemaName = "dbo", TableName = "OldTable", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
        };
        var columns = new List<ColumnUsageStatus>
        {
            new() { SchemaName = "dbo", TableName = "Orders", ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
            new() { SchemaName = "dbo", TableName = "Orders", ColumnName = "Remark", DataType = "nvarchar", IsAllNull = true }
        };

        _repository.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(tables);
        _repository.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>()).Returns(columns);

        // Act
        var result = await _service.ScanAsync(years: 2, progress: null, ct: CancellationToken.None);

        // Assert
        result.Tables.Should().HaveCount(2);
        result.Columns.Should().HaveCount(2);
        result.YearsThreshold.Should().Be(2);
    }

    [Fact]
    public async Task ScanAsync_Repository無資料_應回傳空結果()
    {
        // Arrange
        _repository.GetTableUsageAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TableUsageInfo>());
        _repository.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());

        // Act
        var result = await _service.ScanAsync(years: 2, progress: null, ct: CancellationToken.None);

        // Assert
        result.Tables.Should().BeEmpty();
        result.Columns.Should().BeEmpty();
    }

    [Fact]
    public void GenerateDropTableSql_應產生正確語法()
    {
        var sql = _service.GenerateDropTableSql("dbo", "OldTable");
        sql.Should().Be("DROP TABLE [dbo].[OldTable]");
    }

    [Fact]
    public void GenerateDropColumnSql_應產生正確語法()
    {
        var sql = _service.GenerateDropColumnSql("dbo", "Orders", "Remark");
        sql.Should().Be("ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]");
    }

    [Fact]
    public async Task DeleteTableAsync_應呼叫Repository執行SQL()
    {
        // Act
        await _service.DeleteTableAsync("dbo", "OldTable", CancellationToken.None);

        // Assert
        await _repository.Received(1).ExecuteSqlAsync(
            "DROP TABLE [dbo].[OldTable]",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteColumnAsync_應呼叫Repository執行SQL()
    {
        // Act
        await _service.DeleteColumnAsync("dbo", "Orders", "Remark", CancellationToken.None);

        // Assert
        await _repository.Received(1).ExecuteSqlAsync(
            "ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]",
            Arg.Any<CancellationToken>());
    }
}
