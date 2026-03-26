using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// ConnectionManager 臨時 Profile 功能測試
/// </summary>
public class ConnectionManagerTemporaryProfileTests
{
    private readonly ConnectionManager _manager = new();

    [Fact(DisplayName = "RegisterTemporaryProfiles: should appear in GetAllProfiles")]
    public void RegisterTemporaryProfiles_ShouldAppearInGetAllProfiles()
    {
        var existingCount = _manager.GetAllProfiles().Count;

        var tempProfiles = new List<ConnectionProfile>
        {
            new() { Name = "臨時DEV-test1", Server = "dev-srv", Database = "DevDB" },
            new() { Name = "臨時PROD-test1", Server = "prod-srv", Database = "ProdDB" }
        };

        _manager.RegisterTemporaryProfiles(tempProfiles);

        var all = _manager.GetAllProfiles();
        all.Should().HaveCount(existingCount + 2);
        all.Should().Contain(p => p.Name == "臨時DEV-test1");
        all.Should().Contain(p => p.Name == "臨時PROD-test1");
    }

    [Fact(DisplayName = "RegisterTemporaryProfiles: temporary profiles should come before persistent ones")]
    public void RegisterTemporaryProfiles_ShouldPrioritizeOverPersistent()
    {
        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = "臨時優先-test2", Server = "temporary-srv", Database = "TempDB" }
        });

        var all = _manager.GetAllProfiles();
        // 臨時 profile 應在清單最前面
        all[0].Name.Should().Be("臨時優先-test2", "臨時 profile 應排在持久化 profile 之前");
    }

    [Fact(DisplayName = "RegisterTemporaryProfiles: should not be persisted to disk")]
    public void RegisterTemporaryProfiles_ShouldNotPersistToDisk()
    {
        var uniqueName = $"不落地-{Guid.NewGuid():N}";
        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = uniqueName, Server = "volatile-srv", Database = "VolatileDB" }
        });

        // 建立新的 manager 重新載入，臨時 profile 應消失
        var freshManager = new ConnectionManager();
        freshManager.GetAllProfiles()
            .Should().NotContain(p => p.Name == uniqueName, "臨時 profile 不應被持久化到磁碟");
    }

    [Fact(DisplayName = "GetConnectionString: should work for temporary profiles")]
    public void GetConnectionString_ShouldWorkForTemporaryProfiles()
    {
        var tempProfile = new ConnectionProfile
        {
            Name = "臨時SQL-test4",
            Server = "temp-srv-test4",
            Database = "TempDB",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = "sa",
            Password = "secret"
        };

        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile> { tempProfile });

        var connStr = _manager.GetConnectionString(tempProfile.Id);
        connStr.Should().NotBeNull();
        connStr.Should().Contain("temp-srv-test4");
        connStr.Should().Contain("TempDB");
    }

    [Fact(DisplayName = "GetProfileName: should work for temporary profiles")]
    public void GetProfileName_ShouldWorkForTemporaryProfiles()
    {
        var tempProfile = new ConnectionProfile
        {
            Name = "臨時名稱-test5",
            Server = "srv",
            Database = "db"
        };

        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile> { tempProfile });

        _manager.GetProfileName(tempProfile.Id).Should().Be("臨時名稱-test5");
    }

    [Fact(DisplayName = "RegisterTemporaryProfiles: empty list should not affect existing profiles")]
    public void RegisterTemporaryProfiles_EmptyList_ShouldNotAffectExisting()
    {
        var existingCount = _manager.GetAllProfiles().Count;

        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>());

        _manager.GetAllProfiles().Should().HaveCount(existingCount);
    }

    [Fact(DisplayName = "RegisterTemporaryProfiles: calling twice should replace previous temporary profiles")]
    public void RegisterTemporaryProfiles_CalledTwice_ShouldReplacePrevious()
    {
        var existingCount = _manager.GetAllProfiles().Count;

        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = "第一次-test7", Server = "srv1", Database = "db1" }
        });

        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = "第二次-test7", Server = "srv2", Database = "db2" }
        });

        var all = _manager.GetAllProfiles();
        all.Should().HaveCount(existingCount + 1, "第二次呼叫應取代第一次的臨時 profile");
        all.Should().Contain(p => p.Name == "第二次-test7");
        all.Should().NotContain(p => p.Name == "第一次-test7");
    }
}
