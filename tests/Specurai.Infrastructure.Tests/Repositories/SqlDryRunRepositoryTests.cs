using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlDryRunRepositoryTests
{
    [Fact(DisplayName = "DryRunAsync: 未設定連線字串應擲出例外")]
    public async Task DryRunAsync_NoConnectionString_ShouldThrow()
    {
        var repo = new SqlDryRunRepository(() => null);

        var act = () => repo.DryRunAsync("DELETE FROM T WHERE Id = 1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未設定資料庫連線*");
    }

    [Fact(DisplayName = "DryRunAsync: 語法錯誤應直接回報，不嘗試連線")]
    public async Task DryRunAsync_SyntaxError_ShouldReturnWithoutConnecting()
    {
        // 連線字串指向不存在的主機：若有嘗試連線會逾時或擲例外，
        // 此測試同時驗證「語法錯誤在連線前短路」
        var repo = new SqlDryRunRepository(() => "Server=invalid-host;Database=x;Connect Timeout=1;Encrypt=False");

        var result = await repo.DryRunAsync("UPDATE Users SET WHERE Id = 1");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "DryRunAsync: 非 DML 應直接拒絕，不嘗試連線")]
    public async Task DryRunAsync_NonDml_ShouldRejectWithoutConnecting()
    {
        var repo = new SqlDryRunRepository(() => "Server=invalid-host;Database=x;Connect Timeout=1;Encrypt=False");

        var result = await repo.DryRunAsync("DROP TABLE Users");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }
}
