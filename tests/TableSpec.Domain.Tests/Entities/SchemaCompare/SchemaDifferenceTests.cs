using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities.SchemaCompare;

/// <summary>
/// 單一差異項目實體測試
/// </summary>
public class SchemaDifferenceTests
{
    [Fact]
    public void SchemaDifference_應該能建立新增欄位的差異()
    {
        // Arrange & Act
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low,
            SourceValue = null,
            TargetValue = "NVARCHAR(200)",
            Description = "欄位不存在於目標環境"
        };

        // Assert
        diff.ObjectType.Should().Be(SchemaObjectType.Column);
        diff.DifferenceType.Should().Be(DifferenceType.Added);
        diff.RiskLevel.Should().Be(RiskLevel.Low);
        diff.SourceValue.Should().BeNull();
    }

    [Fact]
    public void SchemaDifference_應該能建立修改欄位的差異()
    {
        // Arrange & Act
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "200",
            TargetValue = "100",
            RiskLevel = RiskLevel.High,
            Description = "欄位長度縮短可能造成資料遺失"
        };

        // Assert
        diff.DifferenceType.Should().Be(DifferenceType.Modified);
        diff.PropertyName.Should().Be("MaxLength");
        diff.RiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void SchemaDifference_應該能設定同步動作()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[NewTable]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        diff.SyncAction = SyncAction.Execute;

        // Assert
        diff.SyncAction.Should().Be(SyncAction.Execute);
    }

    [Fact]
    public void SyncAction_預設值應該是Skip()
    {
        // Arrange & Act
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Test]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Assert
        diff.SyncAction.Should().Be(SyncAction.Skip);
    }

    [Fact]
    public void CanExecuteDirectly_低風險應該可以直接執行()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[NewColumn]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var canExecute = diff.CanExecuteDirectly;

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public void CanExecuteDirectly_中風險需要確認但可以執行()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[RequiredColumn]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Medium
        };

        // Act
        var canExecute = diff.CanExecuteDirectly;

        // Assert
        canExecute.Should().BeTrue();
    }

    [Theory]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Forbidden)]
    public void CanExecuteDirectly_高風險和禁止不能直接執行(RiskLevel riskLevel)
    {
        // Arrange
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[ShrinkColumn]",
            DifferenceType = DifferenceType.Modified,
            RiskLevel = riskLevel
        };

        // Act
        var canExecute = diff.CanExecuteDirectly;

        // Assert
        canExecute.Should().BeFalse();
    }

    [Fact]
    public void SchemaObjectType_應該有必要的物件類型()
    {
        // Arrange & Act
        var values = Enum.GetValues<SchemaObjectType>();

        // Assert
        values.Should().Contain(SchemaObjectType.Table);
        values.Should().Contain(SchemaObjectType.Column);
        values.Should().Contain(SchemaObjectType.Index);
        values.Should().Contain(SchemaObjectType.Constraint);
        values.Should().Contain(SchemaObjectType.View);
        values.Should().Contain(SchemaObjectType.StoredProcedure);
        values.Should().Contain(SchemaObjectType.Function);
        values.Should().Contain(SchemaObjectType.Trigger);
    }
}
