using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class InventoryConnectionSourceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IExternalSourceSettings _settings;
    private readonly InventoryConnectionSource _sut;
    private readonly string _inventoryDir;
    private readonly string _groupVarsDir;

    public InventoryConnectionSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _inventoryDir = Path.Combine(_tempDir, "ansible", "customer", "inventory");
        _groupVarsDir = Path.Combine(_inventoryDir, "group_vars");
        Directory.CreateDirectory(_groupVarsDir);

        _settings = Substitute.For<IExternalSourceSettings>();
        _settings.Load().Returns(new ExternalSourceConfig(_tempDir, string.Empty));
        _sut = new InventoryConnectionSource(_settings);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void WriteHostsYml(string content) =>
        File.WriteAllText(Path.Combine(_inventoryDir, "hosts.yml"), content);

    private void WriteDatabaseYml(string group, string content) =>
        File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(Path.Combine(_groupVarsDir, group)).FullName, "database.yml"),
            content);

    [Fact]
    public async Task SyncAsync_來源目錄為空_回傳空結果()
    {
        _settings.Load().Returns(new ExternalSourceConfig(string.Empty, string.Empty));

        var result = await _sut.SyncAsync();

        result.Profiles.Should().BeEmpty();
        result.FailedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_hosts檔案不存在_回傳空結果()
    {
        var result = await _sut.SyncAsync();

        result.Profiles.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_扁平結構_正確解析客戶連線()
    {
        WriteHostsYml("""
            all:
              children:
                customer_acme:
                  vars:
                    mssql_host: 192.168.1.10
                    customer: acme
                  hosts:
                    acme-prod:
                      env: production
            """);

        WriteDatabaseYml("customer_acme_production", """
            main_sql_override:
              database: acme_db
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(1);
        result.Profiles[0].Name.Should().Be("Acme - 正式");
        result.Profiles[0].Server.Should().Be("192.168.1.10");
        result.Profiles[0].Database.Should().Be("acme_db");
    }

    [Fact]
    public async Task SyncAsync_巢狀結構_正確解析客戶連線()
    {
        WriteHostsYml("""
            all:
              children:
                customers:
                  children:
                    customer_beta:
                      vars:
                        mssql_host: 10.0.0.5
                        customer: beta
                      hosts:
                        beta-staging:
                          env: staging
            """);

        WriteDatabaseYml("customer_beta_staging", """
            main_sql_override:
              database: beta_staging_db
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(1);
        result.Profiles[0].Name.Should().Be("Beta - 測試");
        result.Profiles[0].Database.Should().Be("beta_staging_db");
    }

    [Fact]
    public async Task SyncAsync_環境層database_yml不存在_仍以預設命名產生連線()
    {
        WriteHostsYml("""
            all:
              children:
                customer_nodb:
                  vars:
                    mssql_host: 192.168.1.99
                    customer: nodb
                  hosts:
                    nodb-prod:
                      env: production
            """);

        // 不建立環境層 database.yml，所有值走 all/database.yml 的預設

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(1);
        result.Profiles[0].Server.Should().Be("192.168.1.99");
        result.Profiles[0].Database.Should().Be("NODB");
    }

    [Fact]
    public async Task SyncAsync_無伺服器位址_該環境不產生連線()
    {
        WriteHostsYml("""
            all:
              children:
                customer_nohost:
                  vars:
                    customer: nohost
                  hosts:
                    nohost-prod:
                      env: production
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().BeEmpty();
        result.FailedItems.Should().ContainSingle().Which.Should().Be("nohost/production");
    }

    [Fact]
    public async Task SyncAsync_依inventory環境對應連線環境()
    {
        WriteHostsYml("""
            all:
              children:
                customer_theta:
                  vars:
                    mssql_host: 192.168.1.40
                    customer: theta
                  hosts:
                    theta-prod:
                      env: production
                    theta-staging:
                      env: staging
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Single(p => p.Name == "Theta - 正式").Environment
            .Should().Be(DatabaseEnvironment.Production);
        result.Profiles.Single(p => p.Name == "Theta - 測試").Environment
            .Should().Be(DatabaseEnvironment.Testing);
    }

    [Fact]
    public async Task SyncAsync_無main_sql_override的database_使用客戶層釘住的名稱()
    {
        WriteHostsYml("""
            all:
              children:
                customer_delta:
                  vars:
                    mssql_host: 192.168.1.20
                    customer: delta
                  hosts:
                    delta-prod:
                      env: production
                    delta-staging:
                      env: staging
            """);

        WriteDatabaseYml("customer_delta", """
            main_sql_database_by_env:
              prod: DELTA_LEGACY
              staging: delta-staging
            """);
        WriteDatabaseYml("customer_delta_production", """
            main_sql_override:
              username: sa
            """);
        WriteDatabaseYml("customer_delta_staging", """
            main_sql_override:
              username: sa
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(2);
        result.Profiles.Single(p => p.Name == "Delta - 正式").Database.Should().Be("DELTA_LEGACY");
        result.Profiles.Single(p => p.Name == "Delta - 測試").Database.Should().Be("delta-staging");
    }

    [Fact]
    public async Task SyncAsync_未釘住資料庫名稱_使用縮寫制預設()
    {
        WriteHostsYml("""
            all:
              children:
                customer_epsilon:
                  vars:
                    mssql_host: 192.168.1.30
                    customer: epsilon
                  hosts:
                    epsilon-prod:
                      env: production
                    epsilon-staging:
                      env: staging
            """);

        WriteDatabaseYml("customer_epsilon_production", "main_sql_override:\n  username: sa\n");
        WriteDatabaseYml("customer_epsilon_staging", "main_sql_override:\n  username: sa\n");

        var result = await _sut.SyncAsync();

        result.Profiles.Single(p => p.Name == "Epsilon - 正式").Database.Should().Be("EPSILON");
        result.Profiles.Single(p => p.Name == "Epsilon - 測試").Database.Should().Be("EPSILON-stg");
    }

    [Fact]
    public async Task SyncAsync_環境層有mssql_host_覆寫hosts檔的設定()
    {
        WriteHostsYml("""
            all:
              children:
                customer_zeta:
                  vars:
                    mssql_host: 10.0.0.1
                    customer: zeta
                  hosts:
                    zeta-prod:
                      env: production
            """);

        WriteDatabaseYml("customer_zeta_production", """
            mssql_host: "192.168.10.23"
            main_sql_override:
              database: zeta_db
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(1);
        result.Profiles[0].Server.Should().Be("192.168.10.23");
        result.FailedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_override與客戶層釘住值並存_以override優先()
    {
        WriteHostsYml("""
            all:
              children:
                customer_eta:
                  vars:
                    mssql_host: 192.168.1.50
                    customer: eta
                  hosts:
                    eta-prod:
                      env: production
            """);

        WriteDatabaseYml("customer_eta", """
            main_sql_database_by_env:
              prod: ETA_PINNED
            """);
        WriteDatabaseYml("customer_eta_production", """
            main_sql_override:
              database: ETA_OVERRIDE
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().ContainSingle().Which.Database.Should().Be("ETA_OVERRIDE");
    }

    [Fact]
    public async Task SyncAsync_環境層database_yml只有註解_不影響其他客戶同步()
    {
        WriteHostsYml("""
            all:
              children:
                customer_iota:
                  vars:
                    mssql_host: 192.168.1.60
                    customer: iota
                  hosts:
                    iota-prod:
                      env: production
                customer_kappa:
                  vars:
                    mssql_host: 192.168.1.61
                    customer: kappa
                  hosts:
                    kappa-prod:
                      env: production
            """);

        // 內容被全數註解掉的檔案，YamlDotNet 會反序列化為 null
        WriteDatabaseYml("customer_iota_production", "# 設定全部註解掉\n");
        WriteDatabaseYml("customer_kappa_production", """
            main_sql_override:
              database: kappa_db
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().HaveCount(2);
        result.Profiles.Single(p => p.Name == "Iota - 正式").Database.Should().Be("IOTA");
        result.Profiles.Single(p => p.Name == "Kappa - 正式").Database.Should().Be("kappa_db");
    }

    [Fact]
    public async Task SyncAsync_客戶層database_yml語法錯誤_該環境列為失敗而非猜測名稱()
    {
        WriteHostsYml("""
            all:
              children:
                customer_lambda:
                  vars:
                    mssql_host: 192.168.1.70
                    customer: lambda
                  hosts:
                    lambda-prod:
                      env: production
            """);

        WriteDatabaseYml("customer_lambda", "main_sql_database_by_env:\n  prod: [壞掉的\n");
        WriteDatabaseYml("customer_lambda_production", "main_sql_override:\n  username: sa\n");

        var result = await _sut.SyncAsync();

        result.Profiles.Should().BeEmpty();
        result.FailedItems.Should().ContainSingle().Which.Should().Be("lambda/production");
    }

    [Fact]
    public async Task SyncAsync_非dev或staging的環境_視為正式環境命名()
    {
        WriteHostsYml("""
            all:
              children:
                customer_mu:
                  vars:
                    mssql_host: 192.168.1.80
                    customer: mu
                  hosts:
                    mu-uat:
                      env: uat
            """);

        WriteDatabaseYml("customer_mu", """
            main_sql_database_by_env:
              prod: MU_PINNED
            """);

        var result = await _sut.SyncAsync();

        result.Profiles.Should().ContainSingle().Which.Database.Should().Be("MU_PINNED");
    }

    [Fact]
    public async Task SyncAsync_vault解密失敗_仍回傳連線但密碼為預設值()
    {
        WriteHostsYml("""
            all:
              children:
                customer_gamma:
                  vars:
                    mssql_host: 10.1.1.1
                    customer: gamma
                  hosts:
                    gamma-prod:
                      env: production
            """);

        WriteDatabaseYml("customer_gamma_production", """
            main_sql_override:
              database: gamma_db
            """);

        // 寫入無效的 vault 內容
        var vaultDir = Directory.CreateDirectory(
            Path.Combine(_groupVarsDir, "customer_gamma_production")).FullName;
        File.WriteAllText(Path.Combine(vaultDir, "vault.yml"), "invalid vault content");

        var keyFile = Path.Combine(_tempDir, "vault-pass.txt");
        File.WriteAllText(keyFile, "somepassword");
        _settings.Load().Returns(new ExternalSourceConfig(_tempDir, keyFile));

        var result = await _sut.SyncAsync();

        // 連線仍存在，vault 解密失敗不影響連線產生
        result.Profiles.Should().HaveCount(1);
        result.Profiles[0].Database.Should().Be("gamma_db");
    }
}
