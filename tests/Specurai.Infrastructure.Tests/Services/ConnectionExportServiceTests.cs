using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// 連線設定匯出/匯入服務測試
/// </summary>
public class ConnectionExportServiceTests
{
    private readonly IConnectionExportService _service = new ConnectionExportService();

    private static List<ConnectionProfile> 建立測試連線清單() =>
    [
        new()
        {
            Name = "開發環境",
            Server = "localhost",
            Database = "DevDb",
            AuthType = AuthenticationType.WindowsAuthentication,
            IsDefault = true
        },
        new()
        {
            Name = "測試環境",
            Server = "test-server",
            Database = "TestDb",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = "sa",
            Password = "P@ssw0rd"
        }
    ];

    #region 匯出 JSON

    [Fact]
    public void ExportToJson_包含密碼_應保留密碼欄位()
    {
        var profiles = 建立測試連線清單();

        var data = _service.ExportToJson(profiles, includePasswords: true);

        var imported = _service.ImportFromJson(data);
        imported.Profiles.Should().HaveCount(2);
        imported.Profiles[1].Password.Should().Be("P@ssw0rd");
    }

    [Fact]
    public void ExportToJson_應保留環境欄位()
    {
        var profiles = new List<ConnectionProfile>
        {
            new()
            {
                Name = "正式環境",
                Server = "prod",
                Database = "ProdDb",
                Environment = DatabaseEnvironment.Production
            }
        };

        var data = _service.ExportToJson(profiles, includePasswords: false);

        var imported = _service.ImportFromJson(data);
        imported.Profiles[0].Environment.Should().Be(DatabaseEnvironment.Production);
    }

    [Fact]
    public void ExportToJson_不含密碼_密碼應為Null()
    {
        var profiles = 建立測試連線清單();

        var data = _service.ExportToJson(profiles, includePasswords: false);

        var imported = _service.ImportFromJson(data);
        imported.Profiles[1].Password.Should().BeNull();
        imported.Profiles[1].Username.Should().Be("sa");
    }

    [Fact]
    public void ExportToJson_Version應為1()
    {
        var profiles = 建立測試連線清單();

        var data = _service.ExportToJson(profiles, includePasswords: false);

        var imported = _service.ImportFromJson(data);
        imported.Version.Should().Be(1);
    }

    [Fact]
    public void ExportToJson_ExportedAt應有合理時間()
    {
        var before = DateTime.UtcNow;
        var profiles = 建立測試連線清單();

        var data = _service.ExportToJson(profiles, includePasswords: false);

        var imported = _service.ImportFromJson(data);
        imported.ExportedAt.Should().BeOnOrAfter(before);
        imported.ExportedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void ExportToJson_空清單_應匯出空Profiles()
    {
        var data = _service.ExportToJson([], includePasswords: false);

        var imported = _service.ImportFromJson(data);
        imported.Profiles.Should().BeEmpty();
    }

    #endregion

    #region 加密匯出/匯入

    [Fact]
    public void ExportToEncryptedJson_正確密碼_應能解密()
    {
        var profiles = 建立測試連線清單();
        var password = "MySecretKey123";

        var data = _service.ExportToEncryptedJson(profiles, password, includePasswords: true);

        var imported = _service.ImportFromEncryptedJson(data, password);
        imported.Profiles.Should().HaveCount(2);
        imported.Profiles[0].Name.Should().Be("開發環境");
        imported.Profiles[1].Password.Should().Be("P@ssw0rd");
    }

    [Fact]
    public void ExportToEncryptedJson_錯誤密碼_應拋出例外()
    {
        var profiles = 建立測試連線清單();

        var data = _service.ExportToEncryptedJson(profiles, "correct", includePasswords: true);

        var act = () => _service.ImportFromEncryptedJson(data, "wrong");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportToEncryptedJson_不含密碼_解密後密碼應為Null()
    {
        var profiles = 建立測試連線清單();

        var data = _service.ExportToEncryptedJson(profiles, "key", includePasswords: false);

        var imported = _service.ImportFromEncryptedJson(data, "key");
        imported.Profiles[1].Password.Should().BeNull();
    }

    #endregion

    #region 格式偵測

    [Fact]
    public void IsEncryptedFormat_加密資料_應回傳True()
    {
        var profiles = 建立測試連線清單();
        var data = _service.ExportToEncryptedJson(profiles, "key", includePasswords: false);

        _service.IsEncryptedFormat(data).Should().BeTrue();
    }

    [Fact]
    public void IsEncryptedFormat_純文字JSON_應回傳False()
    {
        var profiles = 建立測試連線清單();
        var data = _service.ExportToJson(profiles, includePasswords: false);

        _service.IsEncryptedFormat(data).Should().BeFalse();
    }

    [Fact]
    public void IsEncryptedFormat_空資料_應回傳False()
    {
        _service.IsEncryptedFormat([]).Should().BeFalse();
    }

    #endregion

    #region 匯入錯誤處理

    [Fact]
    public void ImportFromJson_無效JSON_應拋出例外()
    {
        var data = "not valid json"u8.ToArray();

        var act = () => _service.ImportFromJson(data);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImportFromEncryptedJson_非加密資料_應拋出例外()
    {
        var data = "plain text"u8.ToArray();

        var act = () => _service.ImportFromEncryptedJson(data, "key");
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion
}
