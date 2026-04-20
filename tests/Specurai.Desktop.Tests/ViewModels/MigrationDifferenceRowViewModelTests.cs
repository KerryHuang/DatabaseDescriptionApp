using FluentAssertions;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// MigrationDifferenceRowViewModel 單元測試
/// </summary>
public class MigrationDifferenceRowViewModelTests
{
    [Fact]
    public void Constructor_低風險差異_IsExecutable應為True且預設勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Low,
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]",
            DifferenceType = DifferenceType.Added
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeTrue();
        vm.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Constructor_中風險差異_IsExecutable應為True且預設勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Medium,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Phone]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeTrue();
        vm.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Constructor_高風險差異_IsExecutable應為False且不可勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.High,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Orders].[Amount]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeFalse();
        vm.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void Constructor_禁止差異_IsExecutable應為False()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Forbidden,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Orders].[Id]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeFalse();
    }

    [Fact]
    public void RiskLevelText_各風險等級_應回傳對應中文文字()
    {
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Low })
            .RiskLevelText.Should().Be("🟢 低風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Medium })
            .RiskLevelText.Should().Be("🟡 中風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.High })
            .RiskLevelText.Should().Be("🔴 高風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Forbidden })
            .RiskLevelText.Should().Be("🔴 禁止");
    }
}
