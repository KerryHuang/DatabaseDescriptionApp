using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConnectionToolsTests
{
    private static ConnectionProfile SampleProfile(string name, string database = "AppDb") => new()
    {
        Name = name,
        Server = "srv",
        Database = database,
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p"
    };

    [Fact(DisplayName = "list_connections: 目前設定檔應顯示使用中資料庫，其他設定檔為 null")]
    public void ListConnections_WithCurrentProfile_ShouldShowCurrentDatabaseOnlyForCurrent()
    {
        var currentProfile = SampleProfile("使用中", "AppDb");
        var otherProfile = SampleProfile("其他", "OtherDefaultDb");
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { currentProfile, otherProfile });
        cm.GetCurrentProfile().Returns(currentProfile);
        // 模擬 switch_database 覆寫後的使用中資料庫
        cm.GetCurrentDatabase().Returns("SwitchedDb");

        var result = ConnectionTools.ListConnections(cm);

        using var doc = JsonDocument.Parse(result);
        var items = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("Name").GetString()!);

        items["使用中"].GetProperty("IsCurrent").GetBoolean().Should().BeTrue();
        items["使用中"].GetProperty("CurrentDatabase").GetString().Should().Be("SwitchedDb");
        items["其他"].GetProperty("IsCurrent").GetBoolean().Should().BeFalse();
        items["其他"].GetProperty("CurrentDatabase").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "list_connections: 無目前設定檔時所有 CurrentDatabase 應為 null")]
    public void ListConnections_NoCurrentProfile_AllCurrentDatabaseShouldBeNull()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { SampleProfile("甲"), SampleProfile("乙") });
        cm.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = ConnectionTools.ListConnections(cm);

        using var doc = JsonDocument.Parse(result);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            item.GetProperty("IsCurrent").GetBoolean().Should().BeFalse();
            item.GetProperty("CurrentDatabase").ValueKind.Should().Be(JsonValueKind.Null);
        }
        cm.DidNotReceive().GetCurrentDatabase();
    }

    [Fact]
    public void SwitchConnection_目標連線已停用_回傳已停用訊息()
    {
        var cm = Substitute.For<IConnectionManager>();
        var disabled = new ConnectionProfile
        {
            Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
        };
        cm.GetEnabledProfiles().Returns([]);
        cm.GetAllProfiles().Returns([disabled]);

        var result = ConnectionTools.SwitchConnection(cm, "正式庫");

        result.Should().Be("連線「正式庫」已停用，請先在連線設定中啟用。");
    }

    [Fact]
    public void ListConnections_有停用連線_輸出包含IsEnabled()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns([
            new ConnectionProfile { Name = "停用的", Server = "s1", Database = "db1", IsEnabled = false }
        ]);

        var result = ConnectionTools.ListConnections(cm);

        result.Should().Contain("\"IsEnabled\": false");
    }

    [Fact]
    public void ResolveMultiple_未指定名稱_只回傳啟用的連線()
    {
        var cm = Substitute.For<IConnectionManager>();
        var enabled = new ConnectionProfile
        {
            Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
        };
        cm.GetEnabledProfiles().Returns([enabled]);
        cm.GetAllProfiles().Returns([
            enabled,
            new ConnectionProfile { Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false }
        ]);

        var ids = ProfileResolver.ResolveMultiple(cm, "");

        ids.Should().ContainSingle().Which.Should().Be(enabled.Id);
    }

    [Fact]
    public void UpdateConnection_目標連線已停用_應能成功解析並更新()
    {
        var cm = Substitute.For<IConnectionManager>();
        var disabled = new ConnectionProfile
        {
            Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
        };
        cm.GetAllProfiles().Returns([disabled]);

        var result = ConnectionCrudTools.UpdateConnection(cm, "正式庫", newServer: "s2");

        result.Should().Be("已更新連線「正式庫」。");
        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(p => p.Server == "s2"));
    }

    [Fact]
    public void DescribeMissing_以Guid字串指定停用連線_回傳已停用訊息()
    {
        var cm = Substitute.For<IConnectionManager>();
        var disabled = new ConnectionProfile
        {
            Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
        };
        cm.GetAllProfiles().Returns([disabled]);

        var result = ProfileResolver.DescribeMissing(cm, disabled.Id.ToString());

        result.Should().Be("連線「正式庫」已停用，請先在連線設定中啟用。");
    }

    [Fact]
    public void DeleteConnection_目標連線已停用_應能成功解析並刪除()
    {
        var cm = Substitute.For<IConnectionManager>();
        var disabled = new ConnectionProfile
        {
            Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
        };
        cm.GetAllProfiles().Returns([disabled]);

        var result = ConnectionCrudTools.DeleteConnection(cm, "正式庫", confirm: true);

        result.Should().Be("已刪除連線「正式庫」。");
        cm.Received(1).DeleteProfile(disabled.Id);
    }

    [Fact]
    public void ImportConnections_匯入檔標記正式環境_保留Environment()
    {
        var cm = Substitute.For<IConnectionManager>();
        var exportService = Substitute.For<IConnectionExportService>();
        var imported = new ConnectionProfile
        {
            Name = "正式庫",
            Server = "s1",
            Database = "db1",
            Environment = DatabaseEnvironment.Production
        };
        exportService.ImportFromJson(Arg.Any<byte[]>())
            .Returns(new ConnectionExportData { Profiles = [imported] });

        var filePath = Path.Combine(Path.GetTempPath(), $"specurai-import-{Guid.NewGuid()}.json");
        File.WriteAllText(filePath, "{}");

        try
        {
            var result = ConnectionCrudTools.ImportConnections(cm, exportService, filePath);

            result.Should().Be("已匯入 1 個連線設定。");
            cm.Received(1).AddProfile(Arg.Is<ConnectionProfile>(p =>
                p.Environment == DatabaseEnvironment.Production));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
