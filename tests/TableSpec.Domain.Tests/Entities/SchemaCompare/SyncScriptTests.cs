using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 同步腳本實體測試
/// </summary>
public class SyncScriptTests
{
    [Fact]
    public void SyncScript_應該能建立同步腳本()
    {
        // Arrange & Act
        var script = new SyncScript
        {
            TargetEnvironment = "測試環境",
            GeneratedAt = new DateTime(2026, 1, 24, 16, 0, 0),
            ApplyScript = "ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(200) NULL;",
            RollbackScript = "ALTER TABLE [dbo].[Users] DROP COLUMN [Email];",
            Differences = new List<SchemaDifference>()
        };

        // Assert
        script.TargetEnvironment.Should().Be("測試環境");
        script.ApplyScript.Should().Contain("ALTER TABLE");
        script.RollbackScript.Should().Contain("DROP COLUMN");
    }

    [Fact]
    public void SyncScript_應該包含相關的差異清單()
    {
        // Arrange & Act
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        var script = new SyncScript
        {
            TargetEnvironment = "測試環境",
            ApplyScript = "ALTER TABLE [dbo].[Users] ADD [Email] NVARCHAR(200) NULL;",
            RollbackScript = "ALTER TABLE [dbo].[Users] DROP COLUMN [Email];",
            Differences = new List<SchemaDifference> { diff }
        };

        // Assert
        script.Differences.Should().HaveCount(1);
        script.Differences.First().ObjectName.Should().Be("[dbo].[Users].[Email]");
    }

    [Fact]
    public void HasRollbackScript_有回滾腳本時應該傳回true()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "ALTER TABLE...",
            RollbackScript = "ALTER TABLE..."
        };

        // Act & Assert
        script.HasRollbackScript.Should().BeTrue();
    }

    [Fact]
    public void HasRollbackScript_無回滾腳本時應該傳回false()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "ALTER TABLE...",
            RollbackScript = null
        };

        // Act & Assert
        script.HasRollbackScript.Should().BeFalse();
    }

    [Fact]
    public void HasRollbackScript_空字串回滾腳本應該傳回false()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "ALTER TABLE...",
            RollbackScript = "   "
        };

        // Act & Assert
        script.HasRollbackScript.Should().BeFalse();
    }

    [Fact]
    public void MaxRiskLevel_應該傳回最高風險等級()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "...",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C2", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.High },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C3", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Medium }
            }
        };

        // Act
        var maxRisk = script.MaxRiskLevel;

        // Assert
        maxRisk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void MaxRiskLevel_無差異時應該傳回Low()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "...",
            Differences = new List<SchemaDifference>()
        };

        // Act
        var maxRisk = script.MaxRiskLevel;

        // Assert
        maxRisk.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void CanExecute_最高風險為Low或Medium時應該可執行()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "...",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C2", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Medium }
            }
        };

        // Act & Assert
        script.CanExecute.Should().BeTrue();
    }

    [Fact]
    public void CanExecute_包含High風險時不應該可執行()
    {
        // Arrange
        var script = new SyncScript
        {
            TargetEnvironment = "測試",
            ApplyScript = "...",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "C2", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.High }
            }
        };

        // Act & Assert
        script.CanExecute.Should().BeFalse();
    }
}
