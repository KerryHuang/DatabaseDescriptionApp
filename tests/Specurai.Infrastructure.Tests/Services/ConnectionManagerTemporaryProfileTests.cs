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

    [Fact(DisplayName = "RegisterTemporaryProfiles: 預設連線應排在非預設之前")]
    public void RegisterTemporaryProfiles_DefaultShouldComeFirst()
    {
        var prefix = $"預設優先-{Guid.NewGuid():N}-";
        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = prefix + "zzz-非預設", Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
            new() { Name = prefix + "aaa-預設",   Server = "s", Database = "d", Environment = DatabaseEnvironment.Production, IsDefault = true },
        });

        var mine = _manager.GetAllProfiles().Where(p => p.Name.StartsWith(prefix)).ToList();

        mine[0].Name.Should().Be(prefix + "aaa-預設", "預設連線應排最前，與環境/名稱無關");
    }

    [Fact(DisplayName = "GetAllProfiles: 應依 預設→環境→名稱 排序")]
    public void GetAllProfiles_ShouldSortByDefaultThenEnvThenName()
    {
        var prefix = $"排序測試-{Guid.NewGuid():N}-";
        _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
        {
            new() { Name = prefix + "prod-zzz", Server = "s", Database = "d", Environment = DatabaseEnvironment.Production },
            new() { Name = prefix + "dev-bbb",  Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
            new() { Name = prefix + "dev-aaa",  Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
            new() { Name = prefix + "the-default", Server = "s", Database = "d", Environment = DatabaseEnvironment.Production, IsDefault = true },
        });

        var mine = _manager.GetAllProfiles().Where(p => p.Name.StartsWith(prefix)).ToList();

        mine.Select(p => p.Name).Should().Equal(
            prefix + "the-default", // 預設優先（即使環境 Production、名稱靠後）
            prefix + "dev-aaa",     // 環境 Development，名稱 aaa
            prefix + "dev-bbb",     // 環境 Development，名稱 bbb
            prefix + "prod-zzz");   // 環境 Production
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
