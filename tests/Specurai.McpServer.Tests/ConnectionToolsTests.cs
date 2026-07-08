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
}
