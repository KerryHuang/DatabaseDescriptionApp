using System.Collections.Generic;
using FluentAssertions;
using Specurai.Desktop.Behaviors;
using Xunit;

namespace Specurai.Desktop.Tests.Behaviors;

public class DataGridCellCopyBehaviorTests
{
    // --- NormalizeBindingPath ---

    [Fact]
    public void NormalizeBindingPath_對_null_回傳_null()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeBindingPath_對空字串_回傳_null()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("").Should().BeNull();
    }

    [Fact]
    public void NormalizeBindingPath_對普通屬性名_原樣回傳()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("Name").Should().Be("Name");
    }

    [Fact]
    public void NormalizeBindingPath_對中括號路徑_去除括號()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("[Name]").Should().Be("Name");
    }

    [Fact]
    public void NormalizeBindingPath_對只有左括號_只去左側()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("[Name").Should().Be("Name");
    }

    // --- GetCellValue（強型別反射）---

    private sealed record SampleRow(string Name, int Age, bool IsActive);

    [Fact]
    public void GetCellValue_對強型別實體_用反射取字串屬性()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "Name").Should().Be("Alice");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取整數屬性以_ToString_輸出()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "Age").Should().Be("30");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取布林屬性以_True_False_輸出()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "IsActive").Should().Be("True");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取不存在屬性_回傳_null()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "DoesNotExist").Should().BeNull();
    }

    // --- GetCellValue（Dictionary 動態欄位）---

    [Fact]
    public void GetCellValue_對_Dictionary_用_key_取值()
    {
        var row = new Dictionary<string, object?> { ["X"] = 42 };
        DataGridCellCopyBehavior.GetCellValue(row, "X").Should().Be("42");
    }

    [Fact]
    public void GetCellValue_對_Dictionary_不存在的_key_回傳_null()
    {
        var row = new Dictionary<string, object?> { ["X"] = 42 };
        DataGridCellCopyBehavior.GetCellValue(row, "Y").Should().BeNull();
    }

    [Fact]
    public void GetCellValue_對_Dictionary_null_值_回傳_null()
    {
        var row = new Dictionary<string, object?> { ["X"] = null };
        DataGridCellCopyBehavior.GetCellValue(row, "X").Should().BeNull();
    }

    [Fact]
    public void GetCellValue_對強型別_null_屬性值_回傳_null()
    {
        var row = new { Description = (string?)null };
        DataGridCellCopyBehavior.GetCellValue(row, "Description").Should().BeNull();
    }
}
