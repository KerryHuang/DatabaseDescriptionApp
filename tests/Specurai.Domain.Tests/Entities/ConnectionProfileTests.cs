using System.Text.Json;
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

/// <summary>
/// ConnectionProfile 實體測試
/// </summary>
public class ConnectionProfileTests
{
    [Fact]
    public void ConnectionProfile_建立時_Id應自動產生()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "開發環境",
            Server = "localhost",
            Database = "TestDb"
        };

        // Assert
        profile.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ConnectionProfile_兩個實例_Id應不同()
    {
        // Arrange & Act
        var profile1 = new ConnectionProfile
        {
            Name = "環境1",
            Server = "localhost",
            Database = "Db1"
        };

        var profile2 = new ConnectionProfile
        {
            Name = "環境2",
            Server = "localhost",
            Database = "Db2"
        };

        // Assert
        profile1.Id.Should().NotBe(profile2.Id);
    }

    [Fact]
    public void ConnectionProfile_WindowsAuthentication_不需要帳號密碼()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "Windows 驗證",
            Server = "localhost",
            Database = "TestDb",
            AuthType = AuthenticationType.WindowsAuthentication,
            Username = null,
            Password = null
        };

        // Assert
        profile.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
        profile.Username.Should().BeNull();
        profile.Password.Should().BeNull();
    }

    [Fact]
    public void ConnectionProfile_SqlServerAuthentication_需要帳號密碼()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "SQL 驗證",
            Server = "192.168.1.100",
            Database = "ProductionDb",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = "sa",
            Password = "P@ssw0rd"
        };

        // Assert
        profile.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
        profile.Username.Should().Be("sa");
        profile.Password.Should().Be("P@ssw0rd");
    }

    [Fact]
    public void ConnectionProfile_IsDefault_預設為False()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "localhost",
            Database = "TestDb"
        };

        // Assert
        profile.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void ConnectionProfile_設定為預設_IsDefault應為True()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "預設連線",
            Server = "localhost",
            Database = "DefaultDb",
            IsDefault = true
        };

        // Assert
        profile.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void ConnectionProfile_屬性可修改()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "原始名稱",
            Server = "localhost",
            Database = "OriginalDb"
        };

        // Act
        profile.Name = "新名稱";
        profile.Server = "newserver";
        profile.Database = "NewDb";
        profile.AuthType = AuthenticationType.SqlServerAuthentication;
        profile.Username = "newuser";
        profile.Password = "newpass";
        profile.IsDefault = true;
        profile.Environment = DatabaseEnvironment.Development;

        // Assert
        profile.Name.Should().Be("新名稱");
        profile.Server.Should().Be("newserver");
        profile.Database.Should().Be("NewDb");
        profile.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
        profile.Username.Should().Be("newuser");
        profile.Password.Should().Be("newpass");
        profile.IsDefault.Should().BeTrue();
        profile.Environment.Should().Be(DatabaseEnvironment.Development);
    }

    [Fact]
    public void AuthenticationType_WindowsAuthentication_值應為0()
    {
        // Assert
        ((int)AuthenticationType.WindowsAuthentication).Should().Be(0);
    }

    [Fact]
    public void AuthenticationType_SqlServerAuthentication_值應為1()
    {
        // Assert
        ((int)AuthenticationType.SqlServerAuthentication).Should().Be(1);
    }

    [Fact]
    public void ConnectionProfile_可指定自訂Id()
    {
        // Arrange
        var customId = Guid.NewGuid();

        // Act
        var profile = new ConnectionProfile
        {
            Id = customId,
            Name = "自訂 ID",
            Server = "localhost",
            Database = "TestDb"
        };

        // Assert
        profile.Id.Should().Be(customId);
    }

    [Fact]
    public void ConnectionProfile_Environment_預設為Staging()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "localhost",
            Database = "TestDb"
        };

        // Assert
        profile.Environment.Should().Be(DatabaseEnvironment.Staging);
    }

    [Fact]
    public void ConnectionProfile_可設定Environment為Production()
    {
        // Arrange & Act
        var profile = new ConnectionProfile
        {
            Name = "正式",
            Server = "prod",
            Database = "ProdDb",
            Environment = DatabaseEnvironment.Production
        };

        // Assert
        profile.Environment.Should().Be(DatabaseEnvironment.Production);
    }

    [Fact]
    public void ConnectionProfile_序列化往返_應保留Environment()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "正式",
            Server = "prod",
            Database = "ProdDb",
            Environment = DatabaseEnvironment.Production
        };

        // Act
        var json = JsonSerializer.Serialize(profile);
        var restored = JsonSerializer.Deserialize<ConnectionProfile>(json);

        // Assert
        restored!.Environment.Should().Be(DatabaseEnvironment.Production);
    }

    [Fact]
    public void ConnectionProfile_反序列化舊JSON無Environment欄位_應為Staging()
    {
        // Arrange：模擬既有 connections.json 內無 Environment 欄位的連線
        var legacyJson = """
            { "Name": "舊連線", "Server": "localhost", "Database": "OldDb", "AuthType": 0 }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<ConnectionProfile>(legacyJson);

        // Assert
        profile!.Environment.Should().Be(DatabaseEnvironment.Staging);
    }

    [Fact]
    public void DatabaseEnvironment_列舉值順序應為Dev_Test_Staging_Prod()
    {
        // Assert
        ((int)DatabaseEnvironment.Development).Should().Be(0);
        ((int)DatabaseEnvironment.Testing).Should().Be(1);
        ((int)DatabaseEnvironment.Staging).Should().Be(2);
        ((int)DatabaseEnvironment.Production).Should().Be(3);
    }

    [Fact]
    public void IsEnabled_未指定時_預設為啟用()
    {
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "localhost",
            Database = "TestDb"
        };

        profile.IsEnabled.Should().BeTrue();
    }
}
