using System.IO.Pipelines;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Infrastructure;
using Specurai.Infrastructure.Services;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

/// <summary>
/// 破壞性工具經 elicitation 確認的整合測試：in-process server + 帶 elicitation handler 的 client。
/// 確認被拒時工具在連線前就返回，不需要資料庫。
/// </summary>
public class HumanConfirmIntegrationTests : IAsyncLifetime
{
    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(), $"specurai-test-connections-{Guid.NewGuid():N}.json");

    private IHost _host = null!;
    private McpClient _client = null!;
    private ElicitRequestParams? _lastRequest;
    private Func<ElicitResult> _respond = () => new ElicitResult { Action = "decline" };

    public async Task InitializeAsync()
    {
        var cm = new ConnectionManager(_configPath);
        var profile = new ConnectionProfile
        {
            Name = "測試連線", Server = "127.0.0.1,1", Database = "AppDb",
            AuthType = AuthenticationType.SqlServerAuthentication, Username = "u", Password = "p"
        };
        cm.AddProfile(profile);
        cm.AddProfile(new ConnectionProfile
        {
            Name = "正式連線", Server = "127.0.0.1,1", Database = "ProdDb", Environment = DatabaseEnvironment.Production,
            AuthType = AuthenticationType.SqlServerAuthentication, Username = "u", Password = "p"
        });
        cm.SetCurrentProfile(profile.Id);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSpecuraiCore();
        builder.Services.AddSingleton<IConnectionManager>(cm);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithToolsFromAssembly(typeof(SqlTools).Assembly)
            .AddConnectionTargetFilter();
        _host = builder.Build();
        await _host.StartAsync();

        _client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (request, _) =>
                    {
                        _lastRequest = request;
                        return ValueTask.FromResult(_respond());
                    }
                }
            });
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private async Task<string> CallDdl(string script = "CREATE TABLE T (Id int)", string? database = null)
    {
        var args = new Dictionary<string, object?> { ["script"] = script, ["confirm"] = true };
        if (database != null)
            args["database"] = database;
        var result = await _client.CallToolAsync("execute_ddl", args);
        return ((TextContentBlock)result.Content[0]).Text;
    }

    [Fact(DisplayName = "注入的 server／connectionManager 不應出現在工具 schema")]
    public async Task ToolsList_注入參數不應曝露給模型()
    {
        var tools = await _client.ListToolsAsync();
        var destructive = new[]
        {
            "execute_ddl", "execute_sql", "uninstall_health_monitoring", "restore_run", "migration_apply",
            "migration_log_resize", "delete_agent_job", "set_recovery_model", "delete_connection", "execute_maintenance_plan"
        };
        foreach (var name in destructive)
        {
            var properties = tools.Single(t => t.Name == name).JsonSchema.GetProperty("properties");
            foreach (var injected in new[] { "server", "connectionManager", "recoveryModelService", "cancellationToken" })
                properties.TryGetProperty(injected, out _).Should().BeFalse($"{name}.{injected}");
        }
    }

    [Fact(DisplayName = "使用者拒絕時 DDL 不執行，且表單要求輸入資料庫名稱")]
    public async Task 使用者拒絕_不執行()
    {
        var text = await CallDdl();

        text.Should().Be("使用者拒絕執行，未做任何變更。");
        _lastRequest!.Message.Should().Contain("CREATE TABLE T").And.Contain("「AppDb」");
    }

    [Fact(DisplayName = "使用者接受但名稱打錯時 DDL 不執行")]
    public async Task 名稱不符_不執行()
    {
        _respond = () => new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement> { ["confirmName"] = JsonSerializer.SerializeToElement("Wrong") }
        };

        var text = await CallDdl();

        text.Should().Be("確認名稱不符（預期：AppDb），未執行。");
    }

    [Fact(DisplayName = "帶 database 參數時表單應要求輸入實際執行的資料庫")]
    public async Task 帶database參數_表單要求實際庫名()
    {
        await CallDdl(database: "OtherDb");

        _lastRequest!.Message.Should().Contain("「OtherDb」").And.NotContain("「AppDb」");
    }

    [Fact(DisplayName = "長 SQL 不應截斷，使用者要看得到全部語句")]
    public async Task 長SQL_表單顯示全文()
    {
        var script = "CREATE TABLE T (Id int)\n/*" + new string('x', 600) + "*/\nDROP TABLE Customers";

        await CallDdl(script);

        _lastRequest!.Message.Should().Contain("DROP TABLE Customers");
    }

    [Fact(DisplayName = "正式環境連線應直接被服務拒絕，不先跳確認表單")]
    public async Task 正式環境_不跳表單()
    {
        var result = await _client.CallToolAsync("execute_ddl", new Dictionary<string, object?>
        {
            ["script"] = "CREATE TABLE T (Id int)", ["connection"] = "正式連線", ["confirm"] = true
        });

        _lastRequest.Should().BeNull();
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text))
            .Should().Contain("正式環境");
    }

    [Fact(DisplayName = "表單應顯示目標連線名稱與 server")]
    public async Task 表單含連線與Server()
    {
        await CallDdl();

        _lastRequest!.Message.Should().Contain("「測試連線」").And.Contain("127.0.0.1,1");
    }
}
