using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// 匯出連線設定 ViewModel 測試
/// </summary>
public class ExportConnectionsViewModelTests
{
    private readonly IConnectionManager _connectionManager;
    private readonly IConnectionExportService _exportService;

    public ExportConnectionsViewModelTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _exportService = Substitute.For<IConnectionExportService>();
    }

    private List<ConnectionProfile> 建立測試連線清單() =>
    [
        new() { Name = "開發環境", Server = "localhost", Database = "DevDb", IsDefault = true },
        new() { Name = "測試環境", Server = "test-server", Database = "TestDb" }
    ];

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new ExportConnectionsViewModel();
        vm.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_有服務_應載入所有連線()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);

        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);

        vm.ProfileSelections.Should().HaveCount(2);
    }

    [Fact]
    public void Constructor_連線項目_預設全部勾選()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);

        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);

        vm.ProfileSelections.Should().AllSatisfy(p => p.IsSelected.Should().BeTrue());
    }

    #endregion

    #region 格式選項測試

    [Fact]
    public void UseEncryption_預設為False()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption.Should().BeFalse();
    }

    [Fact]
    public void IncludePasswords_純文字模式_預設為False()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption = false;

        vm.IncludePasswords.Should().BeFalse();
    }

    [Fact]
    public void IncludePasswords_切換為加密模式_應自動設為True()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption = true;

        vm.IncludePasswords.Should().BeTrue();
    }

    [Fact]
    public void IncludePasswords_切換為純文字模式_應自動設為False()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption = true; // 先設為加密
        vm.UseEncryption = false; // 再切回純文字

        vm.IncludePasswords.Should().BeFalse();
    }

    #endregion

    #region 全選/取消全選

    [Fact]
    public void SelectAll_應勾選所有連線()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);
        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);
        vm.ProfileSelections[0].IsSelected = false;

        vm.SelectAllCommand.Execute(null);

        vm.ProfileSelections.Should().AllSatisfy(p => p.IsSelected.Should().BeTrue());
    }

    [Fact]
    public void DeselectAll_應取消所有勾選()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);
        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);

        vm.DeselectAllCommand.Execute(null);

        vm.ProfileSelections.Should().AllSatisfy(p => p.IsSelected.Should().BeFalse());
    }

    #endregion

    #region 匯出資料產生

    [Fact]
    public void GetExportData_純文字_應呼叫ExportToJson()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);
        _exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
            .Returns([1, 2, 3]);
        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);
        vm.UseEncryption = false;

        var result = vm.GetExportData();

        result.Should().Equal([1, 2, 3]);
        _exportService.Received(1).ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), false);
    }

    [Fact]
    public void GetExportData_加密_應呼叫ExportToEncryptedJson()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);
        _exportService.ExportToEncryptedJson(
                Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns([4, 5, 6]);
        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);
        vm.UseEncryption = true;
        vm.EncryptionPassword = "secret";

        var result = vm.GetExportData();

        result.Should().Equal([4, 5, 6]);
        _exportService.Received(1).ExportToEncryptedJson(
            Arg.Any<IReadOnlyList<ConnectionProfile>>(), "secret", true);
    }

    [Fact]
    public void GetExportData_只匯出勾選的連線()
    {
        var profiles = 建立測試連線清單();
        _connectionManager.GetAllProfiles().Returns(profiles);
        _exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
            .Returns([1]);
        var vm = new ExportConnectionsViewModel(_connectionManager, _exportService);
        vm.ProfileSelections[1].IsSelected = false; // 取消第二個

        vm.GetExportData();

        _exportService.Received(1).ExportToJson(
            Arg.Is<IReadOnlyList<ConnectionProfile>>(list => list.Count == 1 && list[0].Name == "開發環境"),
            Arg.Any<bool>());
    }

    #endregion

    #region 預設副檔名

    [Fact]
    public void DefaultExtension_純文字_應為json()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption = false;

        vm.DefaultExtension.Should().Be(".json");
    }

    [Fact]
    public void DefaultExtension_加密_應為tsjson()
    {
        var vm = new ExportConnectionsViewModel();
        vm.UseEncryption = true;

        vm.DefaultExtension.Should().Be(".tsjson");
    }

    #endregion
}
