using System.Data;
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class DryRunResultTests
{
    [Fact(DisplayName = "DryRunResult: 集合屬性預設應為空集合")]
    public void DryRunResult_Default_CollectionsShouldBeEmpty()
    {
        var result = new DryRunResult { IsValid = true };

        result.SyntaxErrors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "DryRunResult: 預設值應為 Unknown 類型、無預覽、無錯誤")]
    public void DryRunResult_Default_ShouldHaveExpectedDefaults()
    {
        var result = new DryRunResult { IsValid = false };

        result.StatementType.Should().Be(DryRunStatementType.Unknown);
        result.AffectedRowCount.Should().Be(0);
        result.PreviewTable.Should().BeNull();
        result.PreviewTruncated.Should().BeFalse();
        result.RejectReason.Should().BeNull();
        result.ExecutionError.Should().BeNull();
    }

    [Fact(DisplayName = "DryRunResult: 所有屬性應可透過 init 設定")]
    public void DryRunResult_InitProperties_ShouldBeSettable()
    {
        var table = new DataTable();
        var result = new DryRunResult
        {
            IsValid = true,
            StatementType = DryRunStatementType.Update,
            SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 5, Message = "錯誤" }],
            AffectedRowCount = 3,
            PreviewTable = table,
            PreviewTruncated = true,
            Warnings = ["警告"],
            ExecutionError = "失敗",
            RejectReason = "原因"
        };

        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.SyntaxErrors.Should().ContainSingle().Which.Message.Should().Be("錯誤");
        result.SyntaxErrors[0].Line.Should().Be(1);
        result.SyntaxErrors[0].Column.Should().Be(5);
        result.AffectedRowCount.Should().Be(3);
        result.PreviewTable.Should().BeSameAs(table);
        result.PreviewTruncated.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be("警告");
        result.ExecutionError.Should().Be("失敗");
        result.RejectReason.Should().Be("原因");
    }
}
