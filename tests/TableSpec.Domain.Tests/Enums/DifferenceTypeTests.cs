using FluentAssertions;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Enums;

/// <summary>
/// 差異類型列舉測試
/// </summary>
public class DifferenceTypeTests
{
    [Fact]
    public void DifferenceType_應該有新增和修改兩種類型()
    {
        // Arrange & Act
        var values = Enum.GetValues<DifferenceType>();

        // Assert
        values.Should().HaveCount(2);
        values.Should().Contain(DifferenceType.Added);
        values.Should().Contain(DifferenceType.Modified);
    }

    [Fact]
    public void DifferenceType_不應該有刪除類型_因為採用最大化原則()
    {
        // Arrange & Act
        var values = Enum.GetValues<DifferenceType>();

        // Assert - 根據設計文件，不刪除任何物件
        values.Should().NotContain(v => v.ToString().Contains("Delete", StringComparison.OrdinalIgnoreCase));
        values.Should().NotContain(v => v.ToString().Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
