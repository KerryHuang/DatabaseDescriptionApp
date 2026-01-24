using FluentAssertions;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Enums;

/// <summary>
/// 風險等級列舉測試
/// </summary>
public class RiskLevelTests
{
    [Fact]
    public void RiskLevel_應該有四個等級()
    {
        // Arrange & Act
        var values = Enum.GetValues<RiskLevel>();

        // Assert
        values.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(RiskLevel.Low, 0)]
    [InlineData(RiskLevel.Medium, 1)]
    [InlineData(RiskLevel.High, 2)]
    [InlineData(RiskLevel.Forbidden, 3)]
    public void RiskLevel_數值應該由低到高排序(RiskLevel level, int expectedValue)
    {
        // Assert
        ((int)level).Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    public void 低風險和中風險_應該允許直接執行(RiskLevel level)
    {
        // Assert - Low 和 Medium 風險可執行（Medium 需確認但可執行）
        ((int)level).Should().BeLessThan((int)RiskLevel.High);
    }

    [Theory]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Forbidden)]
    public void 高風險和禁止_應該只能匯出腳本(RiskLevel level)
    {
        // Assert - High 和 Forbidden 只能匯出腳本
        ((int)level).Should().BeGreaterThanOrEqualTo((int)RiskLevel.High);
    }
}
