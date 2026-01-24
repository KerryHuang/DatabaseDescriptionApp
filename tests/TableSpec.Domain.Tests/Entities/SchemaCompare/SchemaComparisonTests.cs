using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 比對結果實體測試
/// </summary>
public class SchemaComparisonTests
{
    [Fact]
    public void SchemaComparison_應該能建立比對結果()
    {
        // Arrange & Act
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發環境",
            TargetEnvironment = "測試環境",
            ComparedAt = new DateTime(2026, 1, 24, 15, 30, 0),
            Differences = new List<SchemaDifference>()
        };

        // Assert
        comparison.BaseEnvironment.Should().Be("開發環境");
        comparison.TargetEnvironment.Should().Be("測試環境");
        comparison.ComparedAt.Should().Be(new DateTime(2026, 1, 24, 15, 30, 0));
    }

    [Fact]
    public void HasDifferences_沒有差異時應該傳回false()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>()
        };

        // Act & Assert
        comparison.HasDifferences.Should().BeFalse();
    }

    [Fact]
    public void HasDifferences_有差異時應該傳回true()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                new()
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Email]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                }
            }
        };

        // Act & Assert
        comparison.HasDifferences.Should().BeTrue();
    }

    [Fact]
    public void GetDifferencesByRiskLevel_應該正確分組()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                CreateDiff("Col1", RiskLevel.Low),
                CreateDiff("Col2", RiskLevel.Low),
                CreateDiff("Col3", RiskLevel.Medium),
                CreateDiff("Col4", RiskLevel.High),
                CreateDiff("Col5", RiskLevel.Forbidden)
            }
        };

        // Act
        var lowRisk = comparison.GetDifferencesByRiskLevel(RiskLevel.Low);
        var mediumRisk = comparison.GetDifferencesByRiskLevel(RiskLevel.Medium);
        var highRisk = comparison.GetDifferencesByRiskLevel(RiskLevel.High);
        var forbidden = comparison.GetDifferencesByRiskLevel(RiskLevel.Forbidden);

        // Assert
        lowRisk.Should().HaveCount(2);
        mediumRisk.Should().HaveCount(1);
        highRisk.Should().HaveCount(1);
        forbidden.Should().HaveCount(1);
    }

    [Fact]
    public void GetDifferencesByObjectType_應該正確分組()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Table, ObjectName = "T1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C2", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.Medium },
                new() { ObjectType = SchemaObjectType.Index, ObjectName = "I1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low }
            }
        };

        // Act
        var tables = comparison.GetDifferencesByObjectType(SchemaObjectType.Table);
        var columns = comparison.GetDifferencesByObjectType(SchemaObjectType.Column);
        var indexes = comparison.GetDifferencesByObjectType(SchemaObjectType.Index);

        // Assert
        tables.Should().HaveCount(1);
        columns.Should().HaveCount(2);
        indexes.Should().HaveCount(1);
    }

    [Fact]
    public void RiskSummary_應該計算各風險等級數量()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                CreateDiff("C1", RiskLevel.Low),
                CreateDiff("C2", RiskLevel.Low),
                CreateDiff("C3", RiskLevel.Low),
                CreateDiff("C4", RiskLevel.Medium),
                CreateDiff("C5", RiskLevel.High)
            }
        };

        // Act
        var summary = comparison.RiskSummary;

        // Assert
        summary.LowCount.Should().Be(3);
        summary.MediumCount.Should().Be(1);
        summary.HighCount.Should().Be(1);
        summary.ForbiddenCount.Should().Be(0);
        summary.TotalCount.Should().Be(5);
    }

    [Fact]
    public void GetExecutableDifferences_應該只傳回可執行的差異()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                CreateDiff("C1", RiskLevel.Low),
                CreateDiff("C2", RiskLevel.Medium),
                CreateDiff("C3", RiskLevel.High),
                CreateDiff("C4", RiskLevel.Forbidden)
            }
        };

        // Act
        var executable = comparison.GetExecutableDifferences();

        // Assert
        executable.Should().HaveCount(2); // Low 和 Medium
        executable.Should().OnlyContain(d => d.RiskLevel == RiskLevel.Low || d.RiskLevel == RiskLevel.Medium);
    }

    [Fact]
    public void GetScriptOnlyDifferences_應該只傳回只能匯出腳本的差異()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                CreateDiff("C1", RiskLevel.Low),
                CreateDiff("C2", RiskLevel.Medium),
                CreateDiff("C3", RiskLevel.High),
                CreateDiff("C4", RiskLevel.Forbidden)
            }
        };

        // Act
        var scriptOnly = comparison.GetScriptOnlyDifferences();

        // Assert
        scriptOnly.Should().HaveCount(2); // High 和 Forbidden
        scriptOnly.Should().OnlyContain(d => d.RiskLevel == RiskLevel.High || d.RiskLevel == RiskLevel.Forbidden);
    }

    private static SchemaDifference CreateDiff(string name, RiskLevel riskLevel)
    {
        return new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = name,
            DifferenceType = DifferenceType.Added,
            RiskLevel = riskLevel
        };
    }
}
