using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// ConnectionExportData 實體測試
/// </summary>
public class ConnectionExportDataTests
{
    [Fact]
    public void ConnectionExportData_建立時_Version應為1()
    {
        var data = new ConnectionExportData
        {
            Profiles = []
        };

        data.Version.Should().Be(1);
    }

    [Fact]
    public void ConnectionExportData_建立時_ExportedAt應有預設值()
    {
        var before = DateTime.UtcNow;

        var data = new ConnectionExportData
        {
            Profiles = []
        };

        data.ExportedAt.Should().BeOnOrAfter(before);
        data.ExportedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void ConnectionExportData_設定Profiles_應正確保存()
    {
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "測試環境", Server = "test-server", Database = "TestDb" }
        };

        var data = new ConnectionExportData
        {
            Profiles = profiles
        };

        data.Profiles.Should().HaveCount(2);
        data.Profiles[0].Name.Should().Be("開發環境");
        data.Profiles[1].Name.Should().Be("測試環境");
    }

    [Fact]
    public void ConnectionExportData_空Profiles_應為空清單()
    {
        var data = new ConnectionExportData
        {
            Profiles = []
        };

        data.Profiles.Should().BeEmpty();
    }
}
