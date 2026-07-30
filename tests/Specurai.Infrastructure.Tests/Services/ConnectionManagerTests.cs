using FluentAssertions;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// ConnectionManager 當前資料庫覆寫測試
/// </summary>
public class ConnectionManagerTests : IDisposable
{
    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(), $"specurai-test-connections-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private static ConnectionProfile CreateProfile(string name, string database) => new()
    {
        Name = name,
        Server = "localhost",
        Database = database,
        AuthType = AuthenticationType.WindowsAuthentication
    };

    [Fact]
    public void GetCurrentDatabase_未設定覆寫_應回傳設定檔預設資料庫()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.GetCurrentDatabase().Should().Be("DefaultDb");
    }

    [Fact]
    public void GetCurrentDatabase_無任何設定檔_應回傳Null()
    {
        var manager = new ConnectionManager(_configPath);

        manager.GetCurrentDatabase().Should().BeNull();
    }

    [Fact]
    public void SetCurrentDatabase_設定覆寫_GetCurrentDatabase應回傳覆寫值()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.SetCurrentDatabase("OtherDb");

        manager.GetCurrentDatabase().Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_設定覆寫_連線字串InitialCatalog應為覆寫值()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.SetCurrentDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(manager.GetCurrentConnectionString());
        builder.InitialCatalog.Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_傳入Null_應重設回設定檔預設資料庫()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        manager.SetCurrentDatabase("OtherDb");

        manager.SetCurrentDatabase(null);

        manager.GetCurrentDatabase().Should().Be("DefaultDb");
        var builder = new SqlConnectionStringBuilder(manager.GetCurrentConnectionString());
        builder.InitialCatalog.Should().Be("DefaultDb");
    }

    [Fact]
    public void SetCurrentDatabase_變更資料庫_應觸發CurrentDatabaseChanged事件()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        string? raisedDatabase = null;
        manager.CurrentDatabaseChanged += (_, db) => raisedDatabase = db;

        manager.SetCurrentDatabase("OtherDb");

        raisedDatabase.Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_相同資料庫_不應觸發事件()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        var raised = false;
        manager.CurrentDatabaseChanged += (_, _) => raised = true;

        manager.SetCurrentDatabase("DefaultDb");

        raised.Should().BeFalse();
    }

    [Fact]
    public void SetCurrentProfile_切換設定檔_應清除資料庫覆寫()
    {
        var manager = new ConnectionManager(_configPath);
        var p1 = CreateProfile("連線1", "Db1");
        var p2 = CreateProfile("連線2", "Db2");
        manager.AddProfile(p1);
        manager.AddProfile(p2);
        manager.SetCurrentProfile(p1.Id);
        manager.SetCurrentDatabase("OtherDb");

        manager.SetCurrentProfile(p2.Id);

        manager.GetCurrentDatabase().Should().Be("Db2");
    }

    [Fact]
    public void GetConnectionString_指定ProfileId_不受當前資料庫覆寫影響()
    {
        var manager = new ConnectionManager(_configPath);
        var p1 = CreateProfile("連線1", "Db1");
        manager.AddProfile(p1);
        manager.SetCurrentDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(manager.GetConnectionString(p1.Id));
        builder.InitialCatalog.Should().Be("Db1");
    }

    [Fact]
    public async Task GetDatabasesAsync_無設定檔_應回傳空清單()
    {
        var manager = new ConnectionManager(_configPath);

        var databases = await manager.GetDatabasesAsync();

        databases.Should().BeEmpty();
    }

    [Fact]
    public void LoadProfiles_舊設定檔無IsEnabled欄位_全部視為啟用()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        var json = """
        {
          "Profiles": [
            {
              "Id": "11111111-1111-1111-1111-111111111111",
              "Name": "舊連線",
              "Server": "localhost",
              "Database": "OldDb",
              "AuthType": 0,
              "IsDefault": true,
              "Environment": 2
            }
          ],
          "CurrentProfileId": "11111111-1111-1111-1111-111111111111"
        }
        """;
        File.WriteAllText(configPath, json);

        try
        {
            var manager = new ConnectionManager(configPath);

            manager.GetAllProfiles().Should().ContainSingle()
                .Which.IsEnabled.Should().BeTrue();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void GetEnabledProfiles_有停用連線_只回傳啟用的()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            manager.AddProfile(new ConnectionProfile
            {
                Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
            });
            manager.AddProfile(new ConnectionProfile
            {
                Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false
            });

            var enabled = manager.GetEnabledProfiles();

            enabled.Should().ContainSingle().Which.Name.Should().Be("啟用的");
            manager.GetAllProfiles().Should().HaveCount(2);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void GetConnectionString_連線已停用_回傳Null()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var profile = new ConnectionProfile
            {
                Name = "停用的", Server = "s1", Database = "db1", IsEnabled = false
            };
            manager.AddProfile(profile);

            manager.GetConnectionString(profile.Id).Should().BeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void SetCurrentProfile_目標已停用_不切換()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var enabled = new ConnectionProfile
            {
                Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
            };
            var disabled = new ConnectionProfile
            {
                Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false
            };
            manager.AddProfile(enabled);
            manager.AddProfile(disabled);
            manager.SetCurrentProfile(enabled.Id);

            manager.SetCurrentProfile(disabled.Id);

            manager.GetCurrentProfile()!.Id.Should().Be(enabled.Id);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void GetConnectionString_臨時連線_不受停用邏輯影響()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var temp = new ConnectionProfile
            {
                Name = "臨時", Server = "s1", Database = "db1"
            };
            manager.RegisterTemporaryProfiles([temp]);

            manager.GetConnectionString(temp.Id).Should().NotBeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_停用目前連線_自動切換至第一個啟用連線()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var first = new ConnectionProfile { Name = "甲", Server = "s1", Database = "db1" };
            var second = new ConnectionProfile { Name = "乙", Server = "s2", Database = "db2" };
            manager.AddProfile(first);
            manager.AddProfile(second);
            manager.SetCurrentProfile(second.Id);

            second.IsEnabled = false;
            manager.UpdateProfile(second);

            manager.GetCurrentProfile()!.Id.Should().Be(first.Id);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_停用唯一連線_目前連線變為Null()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var only = new ConnectionProfile { Name = "唯一", Server = "s1", Database = "db1" };
            manager.AddProfile(only);
            manager.SetCurrentProfile(only.Id);

            only.IsEnabled = false;
            manager.UpdateProfile(only);

            manager.GetCurrentProfile().Should().BeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_停用預設連線_一併清除預設身分()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var profile = new ConnectionProfile
            {
                Name = "預設的", Server = "s1", Database = "db1", IsDefault = true
            };
            manager.AddProfile(profile);

            profile.IsEnabled = false;
            manager.UpdateProfile(profile);

            manager.GetAllProfiles().Single().IsDefault.Should().BeFalse();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_停用目前連線_重啟後GetCurrentProfile不回傳該連線()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var only = new ConnectionProfile { Name = "唯一", Server = "s1", Database = "db1" };
            manager.AddProfile(only);
            manager.SetCurrentProfile(only.Id);

            only.IsEnabled = false;
            manager.UpdateProfile(only);

            // 模擬重啟／換行程：用同一份設定檔另建一個 ConnectionManager
            var reloaded = new ConnectionManager(configPath);

            reloaded.GetCurrentProfile().Should().BeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void GetCurrentProfile_CurrentProfileId指向停用連線_不回傳該連線()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        var disabledId = Guid.NewGuid();
        var json = $$"""
        {
          "Profiles": [
            {
              "Id": "{{disabledId}}",
              "Name": "停用連線",
              "Server": "localhost",
              "Database": "Db",
              "AuthType": 0,
              "IsDefault": false,
              "IsEnabled": false,
              "Environment": 2
            }
          ],
          "CurrentProfileId": "{{disabledId}}"
        }
        """;
        File.WriteAllText(configPath, json);

        try
        {
            var manager = new ConnectionManager(configPath);

            manager.GetCurrentProfile().Should().BeNull();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void AddProfile_新增停用且預設連線_不清除既有預設連線的預設身分()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var existingDefault = new ConnectionProfile
            {
                Name = "既有預設", Server = "s1", Database = "db1", IsDefault = true
            };
            manager.AddProfile(existingDefault);

            var newDisabledDefault = new ConnectionProfile
            {
                Name = "新停用預設", Server = "s2", Database = "db2", IsDefault = true, IsEnabled = false
            };
            manager.AddProfile(newDisabledDefault);

            manager.GetAllProfiles().First(p => p.Id == existingDefault.Id).IsDefault.Should().BeTrue();
            manager.GetAllProfiles().First(p => p.Id == newDisabledDefault.Id).IsDefault.Should().BeFalse();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_就地修改預設連線的其他欄位_IsDefault不應被清除()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var profile = new ConnectionProfile
            {
                Name = "預設連線", Server = "s1", Database = "db1", IsDefault = true
            };
            manager.AddProfile(profile);

            // 就地修改同一個參考的欄位（模擬 CLI conn update／MCP update_connection 的用法）
            profile.Server = "s1-new";
            manager.UpdateProfile(profile);

            manager.GetAllProfiles().Single().IsDefault.Should().BeTrue();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_把非預設連線改成停用且預設_不清除既有預設連線的預設身分()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var existingDefault = new ConnectionProfile
            {
                Name = "既有預設", Server = "s1", Database = "db1", IsDefault = true
            };
            var other = new ConnectionProfile
            {
                Name = "另一個", Server = "s2", Database = "db2", IsDefault = false
            };
            manager.AddProfile(existingDefault);
            manager.AddProfile(other);

            other.IsDefault = true;
            other.IsEnabled = false;
            manager.UpdateProfile(other);

            manager.GetAllProfiles().First(p => p.Id == existingDefault.Id).IsDefault.Should().BeTrue();
            manager.GetAllProfiles().First(p => p.Id == other.Id).IsDefault.Should().BeFalse();
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void GetCurrentProfile_CurrentProfileId指向停用連線但有啟用的預設連線_應回傳該預設連線()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        var disabledId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var json = $$"""
        {
          "Profiles": [
            {
              "Id": "{{disabledId}}",
              "Name": "停用連線",
              "Server": "localhost",
              "Database": "Db",
              "AuthType": 0,
              "IsDefault": false,
              "IsEnabled": false,
              "Environment": 2
            },
            {
              "Id": "{{defaultId}}",
              "Name": "啟用的預設連線",
              "Server": "localhost",
              "Database": "DefaultDb",
              "AuthType": 0,
              "IsDefault": true,
              "IsEnabled": true,
              "Environment": 2
            }
          ],
          "CurrentProfileId": "{{disabledId}}"
        }
        """;
        File.WriteAllText(configPath, json);

        try
        {
            var manager = new ConnectionManager(configPath);

            manager.GetCurrentProfile()!.Id.Should().Be(defaultId);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void UpdateProfile_停用目前連線_觸發CurrentProfileChanged()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
        try
        {
            var manager = new ConnectionManager(configPath);
            var first = new ConnectionProfile { Name = "甲", Server = "s1", Database = "db1" };
            var second = new ConnectionProfile { Name = "乙", Server = "s2", Database = "db2" };
            manager.AddProfile(first);
            manager.AddProfile(second);
            manager.SetCurrentProfile(second.Id);

            ConnectionProfile? raised = null;
            var raisedCount = 0;
            manager.CurrentProfileChanged += (_, p) => { raised = p; raisedCount++; };

            second.IsEnabled = false;
            manager.UpdateProfile(second);

            raisedCount.Should().Be(1);
            raised!.Id.Should().Be(first.Id);
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
