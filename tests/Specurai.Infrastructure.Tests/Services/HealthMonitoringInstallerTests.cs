using System.Text.RegularExpressions;
using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// 健康監控安裝器測試（版本偵測與舊版相容性轉換）
/// </summary>
public class HealthMonitoringInstallerTests
{
    [Theory]
    [InlineData("15.0.2000.5", 15)]
    [InlineData("16.0.1000.6", 16)]
    [InlineData("14.0.1000.169", 14)]
    [InlineData("13.0.5026.0", 13)]
    [InlineData("10.50.1600.1", 10)]
    [InlineData("9.00.5000.00", 9)]
    public void ParseMajorVersion_有效版本字串_回傳主版本(string productVersion, int expected)
    {
        HealthMonitoringInstaller.ParseMajorVersion(productVersion).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void ParseMajorVersion_無法解析_回傳零作為安全降級(string? productVersion)
    {
        // 回傳 0 → 視為舊版 → 套用相容寫法（FOR XML PATH 全版本可用），為安全預設
        HealthMonitoringInstaller.ParseMajorVersion(productVersion).Should().Be(0);
    }

    [Fact]
    public async Task ConvertAggregationToLegacy_實際安裝腳本_不再包含StringAgg()
    {
        var script = await HealthMonitoringInstaller.LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");
        script.Should().NotBeNullOrEmpty();

        var legacy = HealthMonitoringInstaller.ConvertAggregationToLegacy(script!);

        legacy.Should().NotContain("STRING_AGG");
    }

    [Fact]
    public async Task ConvertAggregationToLegacy_實際安裝腳本_四處改用ForXmlPath()
    {
        var script = await HealthMonitoringInstaller.LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");

        var legacy = HealthMonitoringInstaller.ConvertAggregationToLegacy(script!);

        Regex.Matches(legacy, "FOR XML PATH").Count.Should().Be(4);
        Regex.Matches(legacy, @"STUFF\(\(").Count.Should().Be(4);
    }

    [Fact]
    public async Task ConvertAggregationToLegacy_保留原本的Count彙總()
    {
        var script = await HealthMonitoringInstaller.LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");

        var legacy = HealthMonitoringInstaller.ConvertAggregationToLegacy(script!);

        // 拆分後 COUNT 仍需保留，方可正確計算指標數值
        legacy.Should().Contain("@NoBackupCount = COUNT(*)");
        legacy.Should().Contain("@FailedJobCount = COUNT(DISTINCT j.name)");
    }

    [Fact]
    public void ConvertAggregationToLegacy_沒有StringAgg的腳本_原樣返回()
    {
        const string script = "SELECT 1;\nGO\n";

        HealthMonitoringInstaller.ConvertAggregationToLegacy(script).Should().Be(script);
    }

    [Theory]
    [InlineData(16, false)] // 2022：原生 STRING_AGG
    [InlineData(15, false)] // 2019
    [InlineData(14, false)] // 2017：STRING_AGG 起始版本，用原生
    [InlineData(13, true)]  // 2016：無 STRING_AGG，走相容
    [InlineData(10, true)]  // 2008
    [InlineData(0, true)]   // 無法解析版本：安全降級
    public void ShouldUseLegacy_依主版本決定是否降級(int majorVersion, bool expected)
    {
        HealthMonitoringInstaller.ShouldUseLegacy(majorVersion).Should().Be(expected);
    }

    [Fact]
    public async Task 安裝腳本_建立DBA資料庫時設定為SIMPLE復原模式()
    {
        var script = await HealthMonitoringInstaller.LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");

        script.Should().Contain("ALTER DATABASE DBA SET RECOVERY SIMPLE");
    }

    [Fact]
    public async Task GenerateExportSqlAsync_復原模式設定使用SQLCMD資料庫名稱變數()
    {
        var installer = new HealthMonitoringInstaller(() => null);

        var sql = await installer.GenerateExportSqlAsync();

        sql.Should().Contain("ALTER DATABASE [$(DBADatabaseName)] SET RECOVERY SIMPLE");
    }
}
