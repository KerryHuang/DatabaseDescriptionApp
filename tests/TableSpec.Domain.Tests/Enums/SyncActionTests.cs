using FluentAssertions;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Enums;

/// <summary>
/// 同步動作列舉測試
/// </summary>
public class SyncActionTests
{
    [Fact]
    public void SyncAction_應該包含必要的同步動作()
    {
        // Arrange & Act
        var values = Enum.GetValues<SyncAction>();

        // Assert - 應該有：略過、執行、僅匯出腳本
        values.Should().Contain(SyncAction.Skip);
        values.Should().Contain(SyncAction.Execute);
        values.Should().Contain(SyncAction.ExportScriptOnly);
    }

    [Fact]
    public void Skip_應該是預設值()
    {
        // Arrange & Act
        var defaultValue = default(SyncAction);

        // Assert
        defaultValue.Should().Be(SyncAction.Skip);
    }
}
