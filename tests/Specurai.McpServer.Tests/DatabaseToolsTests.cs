using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class DatabaseToolsTests
{
    private static ConnectionProfile SampleProfile(string database = "AppDb") => new()
    {
        Name = "目前連線",
        Server = "srv",
        Database = database,
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p"
    };

    [Fact(DisplayName = "list_databases: 無目前設定檔應回傳提示訊息")]
    public async Task ListDatabases_NoCurrentProfile_ShouldReturnHint()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await DatabaseTools.ListDatabases(cm);

        result.Should().Contain("switch_connection");
        await cm.DidNotReceive().GetDatabasesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "list_databases: 正常情況應回傳含 IsCurrent 與 IsProfileDefault 標記的 JSON")]
    public async Task ListDatabases_Normal_ShouldReturnJsonWithFlags()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile("AppDb"));
        cm.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "AppDb", "OtherDb" });
        cm.GetCurrentDatabase().Returns("OtherDb");

        var result = await DatabaseTools.ListDatabases(cm);

        // 反序列化逐項驗證，確保標記對應到正確的資料庫（避免旗標來源對調仍通過）
        using var doc = JsonDocument.Parse(result);
        var items = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("Name").GetString()!);

        items.Should().ContainKeys("AppDb", "OtherDb");
        items["AppDb"].GetProperty("IsCurrent").GetBoolean().Should().BeFalse();
        items["AppDb"].GetProperty("IsProfileDefault").GetBoolean().Should().BeTrue();
        items["OtherDb"].GetProperty("IsCurrent").GetBoolean().Should().BeTrue();
        items["OtherDb"].GetProperty("IsProfileDefault").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "list_databases: GetDatabasesAsync 擲例外應回傳友善錯誤")]
    public async Task ListDatabases_ThrowsException_ShouldReturnFriendlyError()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new InvalidOperationException("連線逾時"));

        var result = await DatabaseTools.ListDatabases(cm);

        result.Should().Contain("無法列舉資料庫");
        result.Should().Contain("連線逾時");
    }

    [Fact(DisplayName = "switch_database: 目標存在（不分大小寫）應呼叫 SetCurrentDatabase 並回傳成功訊息")]
    public async Task SwitchDatabase_TargetExistsCaseInsensitive_ShouldSetAndReturnSuccess()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "AppDb", "OtherDb" });

        var result = await DatabaseTools.SwitchDatabase(cm, "otherdb");

        cm.Received(1).SetCurrentDatabase("OtherDb");
        result.Should().Contain("已切換至資料庫「OtherDb」");
    }

    [Fact(DisplayName = "switch_database: 目標不存在應回傳找不到訊息含可用清單且不呼叫 SetCurrentDatabase")]
    public async Task SwitchDatabase_TargetNotExists_ShouldReturnNotFoundAndNotSet()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "AppDb", "OtherDb" });

        var result = await DatabaseTools.SwitchDatabase(cm, "NotExistDb");

        cm.DidNotReceive().SetCurrentDatabase(Arg.Any<string?>());
        result.Should().Contain("找不到");
        result.Should().Contain("AppDb");
        result.Should().Contain("OtherDb");
    }

    [Fact(DisplayName = "switch_database: 無目前設定檔應回傳提示訊息")]
    public async Task SwitchDatabase_NoCurrentProfile_ShouldReturnHint()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await DatabaseTools.SwitchDatabase(cm, "AppDb");

        result.Should().Contain("switch_connection");
        cm.DidNotReceive().SetCurrentDatabase(Arg.Any<string?>());
    }
}
