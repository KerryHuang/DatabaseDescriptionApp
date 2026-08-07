using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConnectionCrudToolsImportTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private static ConnectionProfile P(string name, string server = "srv") => new()
    {
        Name = name, Server = server, Database = "db"
    };

    private static (IConnectionManager cm, IConnectionExportService svc) Mocks(
        ConnectionProfile[] existing, ConnectionProfile[] imported)
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(existing);
        var svc = Substitute.For<IConnectionExportService>();
        svc.ImportFromJson(Arg.Any<byte[]>())
            .Returns(new ConnectionExportData { Profiles = imported });
        return (cm, svc);
    }

    [Fact]
    public void ImportConnections_名稱已存在_應更新而非新增()
    {
        var existing = P("甲", "old-server");
        var (cm, svc) = Mocks([existing], [P("甲", "new-server")]);

        var result = ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.DidNotReceive().AddProfile(Arg.Any<ConnectionProfile>());
        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(
            p => p.Id == existing.Id && p.Server == "new-server"));
        result.Should().Be("已匯入 0 個、已更新 1 個連線設定。");
    }

    [Fact]
    public void ImportConnections_名稱不存在_應新增且標記外部()
    {
        var (cm, svc) = Mocks([], [P("乙")]);

        var result = ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.Received(1).AddProfile(Arg.Is<ConnectionProfile>(
            p => p.Name == "乙" && p.IsExternal && !p.IsDefault));
        result.Should().Be("已匯入 1 個、已更新 0 個連線設定。");
    }

    [Fact]
    public void ImportConnections_更新既有外部連線_IsExternal仍為True()
    {
        var existing = P("甲", "old-server");
        existing.IsExternal = true;
        var (cm, svc) = Mocks([existing], [P("甲", "new-server")]);

        ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(
            p => p.Id == existing.Id && p.IsExternal));
    }

    [Fact]
    public void ImportConnections_名稱比對_不分大小寫()
    {
        var existing = P("Alpha");
        var (cm, svc) = Mocks([existing], [P("ALPHA")]);

        ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.DidNotReceive().AddProfile(Arg.Any<ConnectionProfile>());
        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(p => p.Id == existing.Id));
    }
}
