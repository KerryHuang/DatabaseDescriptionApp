using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

/// <summary>
/// SchemaMigrationService 單元測試
/// </summary>
public class SchemaMigrationServiceTests
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _schemaCompareService;
    private readonly ISchemaMigrationService _service;

    public SchemaMigrationServiceTests()
    {
        _schemaCollector = Substitute.For<ISchemaCollector>();
        _schemaCompareService = Substitute.For<ISchemaCompareService>();
        _service = new SchemaMigrationService(_schemaCollector, _schemaCompareService);
    }

    [Fact]
    public async Task AnalyzeAsync_正常呼叫_應回傳MigrationAnalysis()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "基準",
            TargetEnvironment = "目標"
        };

        _schemaCollector.CollectAsync("base-conn", "基準", Arg.Any<CancellationToken>())
            .Returns(baseSchema);
        _schemaCollector.CollectAsync("target-conn", "目標", Arg.Any<CancellationToken>())
            .Returns(targetSchema);
        _schemaCompareService.CompareAsync(baseSchema, targetSchema)
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.Should().NotBeNull();
        result.BaseSchema.Should().Be(baseSchema);
        result.TargetSchema.Should().Be(targetSchema);
        result.Comparison.Should().Be(comparison);
    }

    [Fact]
    public async Task AnalyzeAsync_含高風險差異_應分類到BlockedDifferences()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var highRiskDiff = new SchemaDifference
        {
            RiskLevel = RiskLevel.High,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]"
        };
        var comparison = new SchemaComparison
        {
            Differences = new List<SchemaDifference> { highRiskDiff }
        };

        _schemaCollector.CollectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(baseSchema, targetSchema);
        _schemaCompareService.CompareAsync(Arg.Any<DatabaseSchema>(), Arg.Any<DatabaseSchema>())
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.BlockedDifferences.Should().ContainSingle();
        result.WarnDifferences.Should().BeEmpty();
        result.SafeDifferences.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_含低風險差異_應分類到SafeDifferences()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var lowRiskDiff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Low,
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]"
        };
        var comparison = new SchemaComparison
        {
            Differences = new List<SchemaDifference> { lowRiskDiff }
        };

        _schemaCollector.CollectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(baseSchema, targetSchema);
        _schemaCompareService.CompareAsync(Arg.Any<DatabaseSchema>(), Arg.Any<DatabaseSchema>())
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.SafeDifferences.Should().ContainSingle();
        result.BlockedDifferences.Should().BeEmpty();
    }
}
