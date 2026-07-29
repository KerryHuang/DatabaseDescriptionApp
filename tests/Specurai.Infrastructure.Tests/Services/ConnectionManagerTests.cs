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
}
