using FluentAssertions;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// ConnectionManager 單次呼叫連線 scope 測試
/// </summary>
public class ConnectionManagerCallScopeTests : IDisposable
{
    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(), $"specurai-test-connections-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private static ConnectionProfile CreateProfile(string name, string server, string database) => new()
    {
        Name = name,
        Server = server,
        Database = database,
        AuthType = AuthenticationType.WindowsAuthentication
    };

    private (ConnectionManager Manager, ConnectionProfile Current, ConnectionProfile Other) Setup()
    {
        var manager = new ConnectionManager(_configPath);
        var current = CreateProfile("目前", "srv-a", "DbA");
        var other = CreateProfile("其他", "srv-b", "DbB");
        manager.AddProfile(current);
        manager.AddProfile(other);
        manager.SetCurrentProfile(current.Id);
        manager.SetCurrentDatabase("SwitchedDb");
        return (manager, current, other);
    }

    private static string? CatalogOf(string? connectionString) =>
        connectionString == null ? null : new SqlConnectionStringBuilder(connectionString).InitialCatalog;

    private static string? ServerOf(string? connectionString) =>
        connectionString == null ? null : new SqlConnectionStringBuilder(connectionString).DataSource;

    [Fact]
    public void BeginCallScope_未指定資料庫_應使用該設定檔預設資料庫()
    {
        var (manager, _, other) = Setup();

        using (manager.BeginCallScope(other, null))
        {
            manager.GetCurrentProfile()!.Id.Should().Be(other.Id);
            manager.GetCurrentDatabase().Should().Be("DbB");
            ServerOf(manager.GetCurrentConnectionString()).Should().Be("srv-b");
            CatalogOf(manager.GetCurrentConnectionString()).Should().Be("DbB");
        }
    }

    [Fact]
    public void BeginCallScope_指定資料庫_應覆寫連線字串的資料庫()
    {
        var (manager, _, other) = Setup();

        using (manager.BeginCallScope(other, "OtherDb"))
        {
            manager.GetCurrentDatabase().Should().Be("OtherDb");
            CatalogOf(manager.GetCurrentConnectionString()).Should().Be("OtherDb");
        }
    }

    [Fact]
    public void BeginCallScope_結束後_應還原目前連線與資料庫()
    {
        var (manager, current, other) = Setup();

        using (manager.BeginCallScope(other, "OtherDb"))
        {
        }

        manager.GetCurrentProfile()!.Id.Should().Be(current.Id);
        manager.GetCurrentDatabase().Should().Be("SwitchedDb");
        CatalogOf(manager.GetCurrentConnectionString()).Should().Be("SwitchedDb");
    }

    [Fact]
    public void BeginCallScope_不應觸發連線或資料庫變更事件()
    {
        var (manager, _, other) = Setup();
        var raised = 0;
        manager.CurrentProfileChanged += (_, _) => raised++;
        manager.CurrentDatabaseChanged += (_, _) => raised++;

        using (manager.BeginCallScope(other, "OtherDb"))
        {
        }

        raised.Should().Be(0);
    }

    [Fact]
    public async Task BeginCallScope_並行的非同步流程_應各自看到自己的連線()
    {
        var (manager, current, other) = Setup();
        var gate = new TaskCompletionSource();

        async Task<string?> RunIn(ConnectionProfile? profile, string? database)
        {
            using var scope = profile == null ? null : manager.BeginCallScope(profile, database);
            await gate.Task;
            return CatalogOf(manager.GetCurrentConnectionString());
        }

        var inOther = RunIn(other, null);
        var inOtherDb = RunIn(current, "Db2");
        var noScope = RunIn(null, null);
        gate.SetResult();

        (await inOther).Should().Be("DbB");
        (await inOtherDb).Should().Be("Db2");
        (await noScope).Should().Be("SwitchedDb");
    }
}
