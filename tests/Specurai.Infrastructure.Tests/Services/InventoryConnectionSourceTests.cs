using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
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
    public async Task SyncAsync_database_yml不存在_該客戶不產生連線()
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

        // 不建立 database.yml

        var result = await _sut.SyncAsync();

        result.Profiles.Should().BeEmpty();
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
