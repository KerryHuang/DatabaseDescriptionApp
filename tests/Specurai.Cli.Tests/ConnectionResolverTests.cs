using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Cli;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Tests;

public class ConnectionResolverTests
{
    private readonly IConnectionManager _connectionManager;
    private readonly ConnectionResolver _resolver;

    public ConnectionResolverTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _resolver = new ConnectionResolver(_connectionManager);
    }

    #region Resolve 優先順序

    [Fact(DisplayName = "Resolve: 有 ConnectionString 時應優先使用")]
    public void Resolve_WithConnectionString_ShouldTakePriority()
    {
        var options = new GlobalOptions
        {
            ConnectionString = "Data Source=fromConnStr;Initial Catalog=TestDB;User Id=sa;Password=P@ss",
            Server = "fromServer"
        };

        var result = _resolver.Resolve(options);

        result.Should().NotBeNull();
        result!.Server.Should().Be("fromConnStr");
        result.Database.Should().Be("TestDB");
    }

    [Fact(DisplayName = "Resolve: 有 Server 參數時應優先於環境變數")]
    public void Resolve_WithServerArg_ShouldPriorityOverEnv()
    {
        var options = new GlobalOptions { Server = "cli-server", Database = "CliDB" };

        var result = _resolver.Resolve(options);

        result.Should().NotBeNull();
        result!.Server.Should().Be("cli-server");
        result.Database.Should().Be("CliDB");
    }

    [Fact(DisplayName = "Resolve: 無任何來源時應回傳 CurrentProfile")]
    public void Resolve_WithNoSource_ShouldReturnCurrentProfile()
    {
        var currentProfile = new ConnectionProfile { Name = "預設", Server = "default-server", Database = "DefaultDB" };
        _connectionManager.GetCurrentProfile().Returns(currentProfile);

        var result = _resolver.Resolve(new GlobalOptions());

        result.Should().Be(currentProfile);
    }

    [Fact(DisplayName = "Resolve: 指定 Profile 名稱時應依名稱查找")]
    public void Resolve_WithProfileName_ShouldFindByName()
    {
        var target = new ConnectionProfile { Name = "測試環境", Server = "test-server", Database = "TestDB" };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { target });

        var result = _resolver.Resolve(new GlobalOptions { Profile = "測試環境" });

        result.Should().Be(target);
    }

    [Fact(DisplayName = "Resolve: Profile 名稱比對不區分大小寫")]
    public void Resolve_WithProfileName_ShouldBeCaseInsensitive()
    {
        var profile = new ConnectionProfile { Name = "Production", Server = "prod", Database = "ProdDB" };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var result = _resolver.Resolve(new GlobalOptions { Profile = "production" });

        result.Should().Be(profile);
    }

    [Fact(DisplayName = "Resolve: ConnStdin 有 StdinProfiles 時應回傳第一個")]
    public void Resolve_WithConnStdinAndProfiles_ShouldReturnFirstStdinProfile()
    {
        var stdinProfile = new ConnectionProfile { Name = "STDIN-DEV", Server = "stdin-srv", Database = "StdinDB" };
        var originalProfiles = Program.StdinProfiles;
        try
        {
            Program.StdinProfiles = new List<ConnectionProfile> { stdinProfile };
            var options = new GlobalOptions { ConnStdin = true };

            var result = _resolver.Resolve(options);

            result.Should().NotBeNull();
            result!.Name.Should().Be("STDIN-DEV");
            result.Server.Should().Be("stdin-srv");
        }
        finally
        {
            Program.StdinProfiles = originalProfiles;
        }
    }

    [Fact(DisplayName = "Resolve: ConnStdin 無 StdinProfiles 時應 fallback 到下一個來源")]
    public void Resolve_WithConnStdinButNoProfiles_ShouldFallback()
    {
        var currentProfile = new ConnectionProfile { Name = "預設", Server = "default", Database = "DefaultDB" };
        _connectionManager.GetCurrentProfile().Returns(currentProfile);

        var originalProfiles = Program.StdinProfiles;
        try
        {
            Program.StdinProfiles = [];
            var options = new GlobalOptions { ConnStdin = true };

            var result = _resolver.Resolve(options);

            result.Should().Be(currentProfile);
        }
        finally
        {
            Program.StdinProfiles = originalProfiles;
        }
    }

    [Fact(DisplayName = "Resolve: Server 參數應優先於 ConnStdin")]
    public void Resolve_WithServerAndConnStdin_ShouldPreferServer()
    {
        var stdinProfile = new ConnectionProfile { Name = "STDIN", Server = "stdin-srv", Database = "StdinDB" };
        var originalProfiles = Program.StdinProfiles;
        try
        {
            Program.StdinProfiles = new List<ConnectionProfile> { stdinProfile };
            var options = new GlobalOptions { Server = "cli-server", Database = "CliDB", ConnStdin = true };

            var result = _resolver.Resolve(options);

            result.Should().NotBeNull();
            result!.Server.Should().Be("cli-server", "CLI 參數應優先於 stdin");
        }
        finally
        {
            Program.StdinProfiles = originalProfiles;
        }
    }

    #endregion

    #region FromConnectionString

    [Fact(DisplayName = "FromConnectionString: SQL 驗證應正確設定帳號密碼")]
    public void FromConnectionString_SqlAuth_ShouldSetCredentials()
    {
        var result = ConnectionResolver.FromConnectionString(
            "Data Source=myserver;Initial Catalog=mydb;User Id=admin;Password=secret");

        result.Server.Should().Be("myserver");
        result.Database.Should().Be("mydb");
        result.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
        result.Username.Should().Be("admin");
        result.Password.Should().Be("secret");
    }

    [Fact(DisplayName = "FromConnectionString: IntegratedSecurity 應設為 Windows 驗證")]
    public void FromConnectionString_IntegratedSecurity_ShouldBeWindowsAuth()
    {
        var result = ConnectionResolver.FromConnectionString(
            "Data Source=myserver;Initial Catalog=mydb;Integrated Security=True");

        result.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
        result.Username.Should().BeNull();
    }

    #endregion

    #region FromMpeJson（透過 ConnectionProfileParser）

    [Fact(DisplayName = "FromMpeJson: 標準 mpe 格式應正確解析所有欄位")]
    public void FromMpeJson_StandardFormat_ShouldParseAllFields()
    {
        var json = """
        {
            "name": "junhe-staging",
            "displayName": "均賀 Staging",
            "mssql": {
                "host": "192.168.1.100",
                "port": "1433",
                "userId": "mis",
                "password": "service",
                "applicationDatabase": "moldplan-test"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = ConnectionProfileParser.FromMpeJson(root, root.GetProperty("mssql"));

        result.Name.Should().Be("均賀 Staging");
        result.Server.Should().Be("192.168.1.100");
        result.Database.Should().Be("moldplan-test");
        result.Username.Should().Be("mis");
        result.Password.Should().Be("service");
    }

    [Fact(DisplayName = "FromMpeJson: Port 為 1433 時 Server 不應附加 Port")]
    public void FromMpeJson_Port1433_ShouldNotAppendPort()
    {
        var json = """{"mssql":{"host":"10.0.0.1","port":"1433","userId":"sa","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Server.Should().Be("10.0.0.1");
    }

    [Fact(DisplayName = "FromMpeJson: Port 非 1433 時 Server 應附加逗號 Port")]
    public void FromMpeJson_NonDefaultPort_ShouldAppendPort()
    {
        var json = """{"mssql":{"host":"10.0.0.1","port":"5433","userId":"sa","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Server.Should().Be("10.0.0.1,5433");
    }

    [Fact(DisplayName = "FromMpeJson: displayName 應優先於 name 作為連線名稱")]
    public void FromMpeJson_WithDisplayName_ShouldPreferDisplayName()
    {
        var json = """{"name":"code","displayName":"顯示名稱","mssql":{"host":"h","userId":"u","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Name.Should().Be("顯示名稱");
    }

    [Fact(DisplayName = "FromMpeJson: 無 displayName 時應使用 name")]
    public void FromMpeJson_WithoutDisplayName_ShouldUseName()
    {
        var json = """{"name":"junhe-prod","mssql":{"host":"h","userId":"u","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Name.Should().Be("junhe-prod");
    }

    [Fact(DisplayName = "FromMpeJson: applicationDatabase 不存在時應 fallback 到 database")]
    public void FromMpeJson_NoAppDb_ShouldFallbackToDatabase()
    {
        var json = """{"mssql":{"host":"h","userId":"u","password":"p","database":"fallback-db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Database.Should().Be("fallback-db");
    }

    [Fact(DisplayName = "FromMpeJson: userId 不存在時應 fallback 到 user")]
    public void FromMpeJson_NoUserId_ShouldFallbackToUser()
    {
        var json = """{"mssql":{"host":"h","user":"alt-user","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromMpeJson(doc.RootElement, doc.RootElement.GetProperty("mssql"));

        result.Username.Should().Be("alt-user");
    }

    #endregion

    #region FromSimpleJson（透過 ConnectionProfileParser）

    [Fact(DisplayName = "FromSimpleJson: 標準格式應正確解析")]
    public void FromSimpleJson_StandardFormat_ShouldParse()
    {
        var json = """{"server":"localhost","database":"MyDB","user":"sa","password":"P@ss"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromSimpleJson(doc.RootElement);

        result.Server.Should().Be("localhost");
        result.Database.Should().Be("MyDB");
        result.Username.Should().Be("sa");
        result.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
    }

    [Fact(DisplayName = "FromSimpleJson: 無 user 欄位時應設為 Windows 驗證")]
    public void FromSimpleJson_NoUser_ShouldBeWindowsAuth()
    {
        var json = """{"server":"localhost","database":"MyDB"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromSimpleJson(doc.RootElement);

        result.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
    }

    [Fact(DisplayName = "FromSimpleJson: 非 1433 Port 應附加到 Server")]
    public void FromSimpleJson_NonDefaultPort_ShouldAppendToServer()
    {
        var json = """{"server":"localhost","port":5433,"database":"MyDB"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.FromSimpleJson(doc.RootElement);

        result.Server.Should().Be("localhost,5433");
    }

    #endregion

    #region FromEnvironment

    [Fact(DisplayName = "FromEnvironment: SPECURAI_CONNECTION_STRING 應優先使用")]
    public void FromEnvironment_WithConnectionString_ShouldTakePriority()
    {
        Environment.SetEnvironmentVariable("SPECURAI_CONNECTION_STRING",
            "Data Source=env-server;Initial Catalog=EnvDB;User Id=sa;Password=p");
        Environment.SetEnvironmentVariable("SPECURAI_SERVER", "other");
        try
        {
            var result = ConnectionResolver.FromEnvironment();
            result.Should().NotBeNull();
            result!.Server.Should().Be("env-server");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPECURAI_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("SPECURAI_SERVER", null);
        }
    }

    [Fact(DisplayName = "FromEnvironment: 個別環境變數應組合為 Profile")]
    public void FromEnvironment_WithIndividualVars_ShouldCombineToProfile()
    {
        Environment.SetEnvironmentVariable("SPECURAI_SERVER", "env-host");
        Environment.SetEnvironmentVariable("SPECURAI_DATABASE", "EnvDB");
        Environment.SetEnvironmentVariable("SPECURAI_USER", "envuser");
        Environment.SetEnvironmentVariable("SPECURAI_PASSWORD", "envpass");
        try
        {
            var result = ConnectionResolver.FromEnvironment();
            result.Should().NotBeNull();
            result!.Server.Should().Be("env-host");
            result.Database.Should().Be("EnvDB");
            result.Username.Should().Be("envuser");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPECURAI_SERVER", null);
            Environment.SetEnvironmentVariable("SPECURAI_DATABASE", null);
            Environment.SetEnvironmentVariable("SPECURAI_USER", null);
            Environment.SetEnvironmentVariable("SPECURAI_PASSWORD", null);
        }
    }

    [Fact(DisplayName = "FromEnvironment: 無環境變數時應回傳 null")]
    public void FromEnvironment_NoVars_ShouldReturnNull()
    {
        Environment.SetEnvironmentVariable("SPECURAI_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("SPECURAI_SERVER", null);

        var result = ConnectionResolver.FromEnvironment();

        result.Should().BeNull();
    }

    [Fact(DisplayName = "FromEnvironment: Port 非數字時應忽略")]
    public void FromEnvironment_InvalidPort_ShouldIgnore()
    {
        Environment.SetEnvironmentVariable("SPECURAI_SERVER", "host");
        Environment.SetEnvironmentVariable("SPECURAI_PORT", "not-a-number");
        Environment.SetEnvironmentVariable("SPECURAI_DATABASE", "db");
        try
        {
            var result = ConnectionResolver.FromEnvironment();
            result.Should().NotBeNull();
            result!.Server.Should().Be("host");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPECURAI_SERVER", null);
            Environment.SetEnvironmentVariable("SPECURAI_PORT", null);
            Environment.SetEnvironmentVariable("SPECURAI_DATABASE", null);
        }
    }

    #endregion
}
