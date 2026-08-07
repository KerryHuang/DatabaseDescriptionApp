using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ExternalSyncToolTests
{
    [Fact]
    public async Task SyncExternalConnections_同步成功_應註冊臨時連線並回報筆數()
    {
        var cm = Substitute.For<IConnectionManager>();
        var source = Substitute.For<IExternalConnectionSource>();
        var profiles = new[]
        {
            new ConnectionProfile { Name = "甲 正式", Server = "s1", Database = "d1" },
            new ConnectionProfile { Name = "乙 正式", Server = "s2", Database = "d2" }
        };
        source.SyncAsync().Returns(new ExternalConnectionResult(profiles, ["丙/production"]));

        var result = await ConnectionTools.SyncExternalConnections(cm, source);

        cm.Received(1).RegisterTemporaryProfiles(
            Arg.Is<IReadOnlyList<ConnectionProfile>>(list => list.Count == 2));
        result.Should().Contain("2").And.Contain("1");
    }

    [Fact]
    public async Task SyncExternalConnections_來源未設定_回傳未取得任何連線()
    {
        var cm = Substitute.For<IConnectionManager>();
        var source = Substitute.For<IExternalConnectionSource>();
        source.SyncAsync().Returns(new ExternalConnectionResult([], []));

        var result = await ConnectionTools.SyncExternalConnections(cm, source);

        result.Should().Be("未取得任何外部連線，請確認外部來源目錄設定。");
    }
}
