using FluentAssertions;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests;

public class SqlDmlExecuteRepositoryTests
{
    private const string FakeConnectionString =
        "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=True";

    [Fact(DisplayName = "SqlDryRunRepository_應同時實作執行介面")]
    public void SqlDryRunRepository_ShouldImplementExecuteInterface()
    {
        var repo = new SqlDryRunRepository(() => FakeConnectionString);
        repo.Should().BeAssignableTo<ISqlDmlExecuteRepository>();
    }

    [Theory(DisplayName = "ExecuteAsync_非單一DML_應離線拒絕且未Commit")]
    [InlineData("SELECT * FROM T")]
    [InlineData("DELETE FROM A; DELETE FROM B")]
    [InlineData("DROP TABLE T")]
    public async Task ExecuteAsync_NotSingleDml_ShouldRejectOfflineWithoutCommit(string sql)
    {
        ISqlDmlExecuteRepository repo = new SqlDryRunRepository(() => FakeConnectionString);

        // 離線拒絕：不會嘗試連線（假連線字串連不上，若嘗試連線會丟 SqlException）
        var result = await repo.ExecuteAsync(sql, FakeConnectionString);

        result.IsValid.Should().BeFalse();
        result.Committed.Should().BeFalse();
        result.CommitUncertain.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_語法錯誤_應回傳錯誤明細且未Commit")]
    public async Task ExecuteAsync_SyntaxError_ShouldReturnErrorsWithoutCommit()
    {
        ISqlDmlExecuteRepository repo = new SqlDryRunRepository(() => FakeConnectionString);

        var result = await repo.ExecuteAsync("UPDATE T SET WHERE", FakeConnectionString);

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
        result.Committed.Should().BeFalse();
    }
}
