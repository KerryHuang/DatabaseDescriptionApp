using System.Text.Json;
using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// ConnectionManager 臨時 Profile 功能測試
/// </summary>
public class ConnectionManagerTemporaryProfileTests : IDisposable
{
    private readonly ConnectionManager _manager = new();

    private readonly string _configPath =
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-connections.json");

    public void Dispose()
    {
        if (File.Exists(_configPath)) File.Delete(_configPath);
    }

    private static ConnectionProfile Temp(string name = "外部連線") => new()
    {
        Name = name, Server = "ext-srv", Database = "ext-db",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u", Password = "p", IsExternal = true
    };

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

    [Fact]
    public void RegisterTemporaryProfiles_目前連線為已消失的臨時連線_應重設並觸發事件()
    {
        var sut = new ConnectionManager(_configPath);
        var first = Temp("第一批-外部連線");
        sut.RegisterTemporaryProfiles([first]);
        sut.SetCurrentProfile(first.Id);
        ConnectionProfile? raised = null;
        var raisedCount = 0;
        sut.CurrentProfileChanged += (_, p) => { raised = p; raisedCount++; };

        // 重新同步：新一批臨時連線不含 first（新 Guid），first 變成孤兒
        sut.RegisterTemporaryProfiles([Temp("第二批-外部連線")]);

        raisedCount.Should().Be(1, "目前連線消失時應觸發一次 CurrentProfileChanged");
        raised.Should().NotBeSameAs(first, "目前連線不應再指向已消失的臨時連線");
    }

    [Fact]
    public void SetCurrentProfile_臨時連線_應可成為目前連線()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);

        sut.SetCurrentProfile(temp.Id);

        sut.GetCurrentProfile().Should().BeSameAs(temp);
        sut.GetCurrentConnectionString().Should().Contain("ext-srv");
    }

    [Fact]
    public void SetCurrentProfile_臨時連線_應觸發連線變更事件()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);
        ConnectionProfile? raised = null;
        sut.CurrentProfileChanged += (_, p) => raised = p;

        sut.SetCurrentProfile(temp.Id);

        raised.Should().BeSameAs(temp);
    }

    [Fact]
    public void SaveProfiles_目前連線為臨時連線_不寫入其Id()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);
        sut.SetCurrentProfile(temp.Id);

        // AddProfile 會觸發存檔
        sut.AddProfile(new ConnectionProfile
        {
            Name = "自建", Server = "s", Database = "d"
        });

        using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
        var names = doc.RootElement.GetProperty("Profiles")
            .EnumerateArray().Select(e => e.GetProperty("Name").GetString()).ToList();
        names.Should().ContainSingle().Which.Should().Be("自建");
        // 臨時連線的 Id 完全不落地：欄位必須為 null
        doc.RootElement.GetProperty("CurrentProfileId").ValueKind
            .Should().Be(JsonValueKind.Null);
    }
}
