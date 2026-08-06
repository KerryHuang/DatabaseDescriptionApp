using FluentAssertions;
using Specurai.Infrastructure.Repositories;
using Xunit;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlDdlExecuteRepositoryTests
{
    [Fact(DisplayName = "ExecuteAsync_驗證不過_應直接回拒絕不連線")]
    public async Task ExecuteAsync_驗證不過_應直接回拒絕不連線()
    {
        var repository = new SqlDdlExecuteRepository();

        // 連線字串指向不存在的主機：驗證若有連線會逾時失敗，藉此證明拒絕發生在離線階段
        var result = await repository.ExecuteAsync(
            "TRUNCATE TABLE dbo.T1",
            "Server=unreachable.invalid;Database=x;Connect Timeout=1;TrustServerCertificate=True",
            commit: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("白名單");
        result.Committed.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_語法錯誤_應回報明細不連線")]
    public async Task ExecuteAsync_語法錯誤_應回報明細不連線()
    {
        var repository = new SqlDdlExecuteRepository();

        var result = await repository.ExecuteAsync(
            "CREATE TABBLE dbo.T1 (Id INT)",
            "Server=unreachable.invalid;Database=x;Connect Timeout=1;TrustServerCertificate=True",
            commit: false);

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }
}
