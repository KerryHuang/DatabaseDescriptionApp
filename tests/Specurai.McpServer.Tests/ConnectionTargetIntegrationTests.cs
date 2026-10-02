using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Infrastructure;
using Specurai.Infrastructure.Services;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

/// <summary>
/// 單次呼叫連線參數（connection／database）整合測試：
/// 以 in-process MCP server + client 驗證 filter 接線、tools/list schema、錯誤不退回目前連線。
/// 測試送出會被唯讀 validator 離線拒絕的 SQL，工具不會真的連線，不需要資料庫。
/// </summary>
public class ConnectionTargetIntegrationTests : IAsyncLifetime
{
    private const string UnreachableServer = "127.0.0.1,1";

    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(), $"specurai-test-connections-{Guid.NewGuid():N}.json");

    private ConnectionManager _cm = null!;
    private ConnectionProfile _current = null!;
    private ConnectionProfile _target = null!;
    private IHost _host = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        _cm = new ConnectionManager(_configPath);
        _current = Profile("目前連線", "CurrentDb");
        _target = Profile("目標連線", "TargetDb");
        _cm.AddProfile(_current);
        _cm.AddProfile(_target);
        _cm.AddProfile(new ConnectionProfile
        {
            Name = "停用連線", Server = UnreachableServer, Database = "Db", IsEnabled = false,
            AuthType = AuthenticationType.SqlServerAuthentication, Username = "u", Password = "p"
        });
        _cm.SetCurrentProfile(_current.Id);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSpecuraiCore();
        builder.Services.AddSingleton<IConnectionManager>(_cm);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithToolsFromAssembly(typeof(SqlTools).Assembly)
            .WithTools<ProbeTools>()
            .AddConnectionTargetFilter();
        _host = builder.Build();
        await _host.StartAsync();

        _client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private static ConnectionProfile Profile(string name, string database) => new()
    {
        Name = name,
        Server = UnreachableServer,
        Database = database,
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p"
    };

    private static string TextOf(ContentBlock block) => ((TextContentBlock)block).Text;

    private Task<CallToolResult> CallReadonlySql(Dictionary<string, object?> extra)
    {
        var args = new Dictionary<string, object?>(extra) { ["sql"] = "UPDATE t SET a = 1" };
        return _client.CallToolAsync("execute_readonly_sql", args).AsTask();
    }

    [Fact]
    public async Task ToolsList_單庫工具_schema應含connection與database且描述可見()
    {
        var tools = await _client.ListToolsAsync();
        var tool = tools.Single(t => t.Name == "execute_readonly_sql");
        var properties = tool.JsonSchema.GetProperty("properties");

        properties.GetProperty("connection").GetProperty("description").GetString()
            .Should().Contain("switch_connection");
        properties.GetProperty("database").GetProperty("description").GetString()
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task 連線名稱不存在_應回錯誤且不執行工具()
    {
        var result = await CallReadonlySql(new() { ["connection"] = "不存在的名字" });

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle();
        TextOf(result.Content[0]).Should().Contain("找不到").And.Contain("不存在的名字");
    }

    [Fact]
    public async Task 連線已停用_應回錯誤()
    {
        var result = await CallReadonlySql(new() { ["connection"] = "停用連線" });

        result.IsError.Should().BeTrue();
        TextOf(result.Content[0]).Should().Contain("已停用");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task connection為空字串_應回錯誤而非退回目前連線(string value)
    {
        var result = await CallReadonlySql(new() { ["connection"] = value });

        result.IsError.Should().BeTrue();
        TextOf(result.Content[0]).Should().Contain("connection");
    }

    [Fact]
    public async Task connection非字串_應回錯誤而非退回目前連線()
    {
        var result = await CallReadonlySql(new() { ["connection"] = 123 });

        result.IsError.Should().BeTrue();
        TextOf(result.Content[0]).Should().Contain("connection");
    }

    [Fact]
    public async Task 只收connection的工具帶database_應回錯誤()
    {
        var result = await _client.CallToolAsync("test_connection",
            new Dictionary<string, object?> { ["connection"] = "目標連線", ["database"] = "OtherDb" });

        result.IsError.Should().BeTrue();
        TextOf(result.Content[0]).Should().Contain("不支援 database");
    }

    [Fact]
    public async Task 工具自有database參數_不應被擋且executedOn不附database()
    {
        var result = await _client.CallToolAsync("set_recovery_model",
            new Dictionary<string, object?> { ["connection"] = "目標連線", ["database"] = "SomeDb", ["model"] = "FULL" });

        result.IsError.Should().NotBeTrue();
        using var json = JsonDocument.Parse(TextOf(result.Content[0])["executedOn:".Length..]);
        json.RootElement.GetProperty("connection").GetString().Should().Be("目標連線");
        json.RootElement.TryGetProperty("database", out _).Should().BeFalse();
        TextOf(result.Content[1]).Should().Contain("SomeDb");
    }

    [Fact]
    public async Task 帶connection_回應應前置executedOn且不改目前連線()
    {
        var result = await CallReadonlySql(new() { ["connection"] = "目標連線", ["database"] = "OtherDb" });

        var executedOn = TextOf(result.Content[0]);
        executedOn.Should().StartWith("executedOn:");
        using var json = JsonDocument.Parse(executedOn["executedOn:".Length..]);
        json.RootElement.GetProperty("connection").GetString().Should().Be("目標連線");
        json.RootElement.GetProperty("server").GetString().Should().Be(UnreachableServer);
        json.RootElement.GetProperty("database").GetString().Should().Be("OtherDb");

        _cm.GetCurrentProfile()!.Id.Should().Be(_current.Id);
        _cm.GetCurrentDatabase().Should().Be("CurrentDb");
    }

    [Fact]
    public async Task 只帶database_應套用在目前連線上()
    {
        var result = await CallReadonlySql(new() { ["database"] = "OtherDb" });

        using var json = JsonDocument.Parse(TextOf(result.Content[0])["executedOn:".Length..]);
        json.RootElement.GetProperty("connection").GetString().Should().Be("目前連線");
        json.RootElement.GetProperty("database").GetString().Should().Be("OtherDb");
        _cm.GetCurrentDatabase().Should().Be("CurrentDb");
    }

    [Fact]
    public async Task 並行呼叫_應同時執行且各自看到自己的連線()
    {
        ProbeTools.Reset(participants: 2);

        var toTarget = _client.CallToolAsync("probe_catalog",
            new Dictionary<string, object?> { ["connection"] = "目標連線" }).AsTask();
        var toCurrentOtherDb = _client.CallToolAsync("probe_catalog",
            new Dictionary<string, object?> { ["database"] = "OtherDb" }).AsTask();

        // 兩次呼叫都進入 barrier 才會放行；若 server 序列處理請求，這裡會逾時
        var both = Task.WhenAll(toTarget, toCurrentOtherDb);
        (await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(both);

        TextOf((await toTarget).Content[1]).Should().Be("TargetDb");
        TextOf((await toCurrentOtherDb).Content[1]).Should().Be("OtherDb");
        _cm.GetCurrentDatabase().Should().Be("CurrentDb");
    }

    [Fact]
    public async Task 不帶新參數_輸出不應出現executedOn()
    {
        var result = await CallReadonlySql(new());

        result.Content.Should().NotContain(c => TextOf(c).StartsWith("executedOn:"));
    }
}

/// <summary>
/// 測試用工具：等所有參與者都進入後才讀取目前連線字串的資料庫，用來驗證並行與 scope 隔離。
/// </summary>
[McpServerToolType]
public class ProbeTools
{
    private static Barrier _barrier = new(1);

    public static void Reset(int participants) => _barrier = new Barrier(participants);

    [McpServerTool(Name = "probe_catalog"), TargetConnection]
    public static async Task<string> ProbeCatalog(
        IConnectionManager connectionManager,
        [Description(ConnectionTarget.ConnectionDescription)] string? connection = null,
        [Description(ConnectionTarget.DatabaseDescription)] string? database = null)
    {
        await Task.Run(() => _barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
        return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            connectionManager.GetCurrentConnectionString()).InitialCatalog;
    }
}
