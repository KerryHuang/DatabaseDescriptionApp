using FluentAssertions;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests;

public class SqlQueryRepositoryReadOnlyTests
{
    // 連不上的假連線字串：若 SQL 在連線前就被擋，測試不需要真資料庫
    private const string FakeConnectionString =
        "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=True";

    [Theory(DisplayName = "ExecuteQueryAsync_非唯讀SQL_應在連線前丟InvalidOperationException")]
    [InlineData("DELETE FROM T")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) DELETE FROM cte")]
    [InlineData("SELECT 1; DELETE FROM T")]
    [InlineData("SELECT * INTO T2 FROM T")]
    [InlineData("EXEC sp_who")]
    public async Task ExecuteQueryAsync_NonReadOnly_ShouldThrowBeforeConnecting(string sql)
    {
        var repo = new SqlQueryRepository(() => FakeConnectionString);

        var act = () => repo.ExecuteQueryAsync(sql, FakeConnectionString);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("唯讀") || e.Message.Contains("SELECT"));
    }

    [Fact(DisplayName = "ExecuteQueryWithSchemaAsync_非唯讀SQL_應在連線前丟InvalidOperationException")]
    public async Task ExecuteQueryWithSchemaAsync_NonReadOnly_ShouldThrowBeforeConnecting()
    {
        var repo = new SqlQueryRepository(() => FakeConnectionString);

        var act = () => repo.ExecuteQueryWithSchemaAsync("UPDATE T SET A = 1", FakeConnectionString);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
