using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

public class UsageAnalysisServiceCompareTests
{
    [Fact]
    public async Task CompareAsync_應合併各環境的表使用狀態()
    {
        // Arrange
        var baseRepo = Substitute.For<IUsageAnalysisRepository>();
        var targetRepo = Substitute.For<IUsageAnalysisRepository>();
        var connectionManager = Substitute.For<IConnectionManager>();

        var baseProfileId = Guid.NewGuid();
        var targetProfileId = Guid.NewGuid();

        connectionManager.GetConnectionString(baseProfileId).Returns("Server=base;Database=db");
        connectionManager.GetConnectionString(targetProfileId).Returns("Server=target;Database=db");
        connectionManager.GetProfileName(baseProfileId).Returns("DEV");
        connectionManager.GetProfileName(targetProfileId).Returns("PROD");

        IUsageAnalysisRepository RepoFactory(string? connStr) =>
            connStr == "Server=base;Database=db" ? baseRepo : targetRepo;

        var service = new UsageAnalysisService(baseRepo, connectionManager, RepoFactory);

        baseRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
            new() { SchemaName = "dbo", TableName = "DevOnly", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
        });

        targetRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 5000 }
        });

        baseRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());
        targetRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());

        // Act
        var result = await service.CompareAsync(
            baseProfileId, [targetProfileId], 2, null, CancellationToken.None);

        // Assert
        result.BaseEnvironment.Should().Be("DEV");
        result.TargetEnvironments.Should().Contain("PROD");
        result.TableRows.Should().HaveCount(2); // Orders + DevOnly

        var ordersRow = result.TableRows.First(r => r.TableName == "Orders");
        ordersRow.EnvironmentStatus["DEV"]!.UserSeeks.Should().Be(100);
        ordersRow.EnvironmentStatus["PROD"]!.UserSeeks.Should().Be(5000);

        var devOnlyRow = result.TableRows.First(r => r.TableName == "DevOnly");
        devOnlyRow.EnvironmentStatus["PROD"].Should().BeNull();
    }

    [Fact]
    public async Task CompareAsync_無ConnectionManager_應拋出例外()
    {
        // Arrange - 使用單參數建構函式（無 ConnectionManager）
        var repo = Substitute.For<IUsageAnalysisRepository>();
        var service = new UsageAnalysisService(repo);

        // Act & Assert
        var act = () => service.CompareAsync(Guid.NewGuid(), [Guid.NewGuid()], 2, null, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompareAsync_應合併欄位使用狀態()
    {
        // Arrange
        var baseRepo = Substitute.For<IUsageAnalysisRepository>();
        var targetRepo = Substitute.For<IUsageAnalysisRepository>();
        var connectionManager = Substitute.For<IConnectionManager>();

        var baseProfileId = Guid.NewGuid();
        var targetProfileId = Guid.NewGuid();

        connectionManager.GetConnectionString(baseProfileId).Returns("Server=base;Database=db");
        connectionManager.GetConnectionString(targetProfileId).Returns("Server=target;Database=db");
        connectionManager.GetProfileName(baseProfileId).Returns("DEV");
        connectionManager.GetProfileName(targetProfileId).Returns("PROD");

        IUsageAnalysisRepository RepoFactory(string? connStr) =>
            connStr == "Server=base;Database=db" ? baseRepo : targetRepo;

        var service = new UsageAnalysisService(baseRepo, connectionManager, RepoFactory);

        baseRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<TableUsageInfo>());
        targetRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<TableUsageInfo>());

        baseRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ColumnUsageStatus>
            {
                new() { SchemaName = "dbo", TableName = "T1", ColumnName = "Col1", DataType = "int", IsReferencedInQueries = true },
                new() { SchemaName = "dbo", TableName = "T1", ColumnName = "Col2", DataType = "nvarchar", IsAllNull = true }
            });

        targetRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ColumnUsageStatus>
            {
                new() { SchemaName = "dbo", TableName = "T1", ColumnName = "Col1", DataType = "int", IsAllNull = true }
            });

        // Act
        var result = await service.CompareAsync(
            baseProfileId, [targetProfileId], 2, null, CancellationToken.None);

        // Assert
        result.ColumnRows.Should().HaveCount(2); // Col1 + Col2
        var col1Row = result.ColumnRows.First(r => r.ColumnName == "Col1");
        col1Row.EnvironmentStatus["DEV"]!.IsReferencedInQueries.Should().BeTrue();
        col1Row.EnvironmentStatus["PROD"]!.IsAllNull.Should().BeTrue();

        var col2Row = result.ColumnRows.First(r => r.ColumnName == "Col2");
        col2Row.EnvironmentStatus["PROD"].Should().BeNull(); // 不存在
    }
}
