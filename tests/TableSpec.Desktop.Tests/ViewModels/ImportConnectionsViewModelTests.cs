using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// 匯入連線設定 ViewModel 測試
/// </summary>
public class ImportConnectionsViewModelTests
{
    private readonly IConnectionManager _connectionManager;
    private readonly IConnectionExportService _exportService;

    public ImportConnectionsViewModelTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _exportService = Substitute.For<IConnectionExportService>();
    }

    private static ConnectionExportData 建立匯入資料() => new()
    {
        Profiles =
        [
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "測試環境", Server = "test-server", Database = "TestDb" }
        ]
    };

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new ImportConnectionsViewModel();
        vm.Should().NotBeNull();
    }

    #endregion

    #region 載入匯入資料

    [Fact]
    public void LoadImportData_純文字JSON_應載入預覽清單()
    {
        var importData = 建立匯入資料();
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);

        vm.ImportPreviews.Should().HaveCount(2);
    }

    [Fact]
    public void LoadImportData_加密格式_NeedsPassword應為True()
    {
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(true);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);

        vm.NeedsPassword.Should().BeTrue();
    }

    [Fact]
    public void DecryptAndLoad_正確密碼_應載入預覽()
    {
        var importData = 建立匯入資料();
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(true);
        _exportService.ImportFromEncryptedJson(Arg.Any<byte[]>(), "secret").Returns(importData);
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        vm.DecryptPassword = "secret";
        vm.DecryptAndLoad();

        vm.ImportPreviews.Should().HaveCount(2);
        vm.NeedsPassword.Should().BeFalse();
    }

    #endregion

    #region 衝突偵測

    [Fact]
    public void LoadImportData_名稱重複_應標記衝突()
    {
        var importData = 建立匯入資料();
        var existingProfiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "old-server", Database = "OldDb" }
        };
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(existingProfiles);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);

        vm.ImportPreviews[0].HasConflict.Should().BeTrue();
        vm.ImportPreviews[1].HasConflict.Should().BeFalse();
    }

    #endregion

    #region 匯入執行

    [Fact]
    public void ExecuteImport_無衝突項目_應呼叫AddProfile()
    {
        var importData = 建立匯入資料();
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        var result = vm.ExecuteImport();

        _connectionManager.Received(2).AddProfile(Arg.Any<ConnectionProfile>());
        result.Added.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Overwritten.Should().Be(0);
    }

    [Fact]
    public void ExecuteImport_衝突項目選擇跳過_不應匯入()
    {
        var importData = 建立匯入資料();
        var existingProfiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "old-server", Database = "OldDb" }
        };
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(existingProfiles);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        vm.ImportPreviews[0].ConflictAction = ConflictAction.Skip;
        var result = vm.ExecuteImport();

        result.Added.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public void ExecuteImport_衝突項目選擇覆蓋_應呼叫UpdateProfile()
    {
        var importData = 建立匯入資料();
        var existingProfiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "old-server", Database = "OldDb" }
        };
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(existingProfiles);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        vm.ImportPreviews[0].ConflictAction = ConflictAction.Overwrite;
        var result = vm.ExecuteImport();

        _connectionManager.Received(1).UpdateProfile(Arg.Any<ConnectionProfile>());
        _connectionManager.Received(1).AddProfile(Arg.Any<ConnectionProfile>());
        result.Overwritten.Should().Be(1);
        result.Added.Should().Be(1);
    }

    [Fact]
    public void OverwriteAll_應將所有衝突項目設為覆蓋()
    {
        var importData = 建立匯入資料();
        var existingProfiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "old-server", Database = "OldDb" },
            new() { Name = "測試環境", Server = "old-test", Database = "OldTest" }
        };
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(existingProfiles);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        vm.OverwriteAllCommand.Execute(null);

        vm.ImportPreviews.Should().AllSatisfy(p => p.ConflictAction.Should().Be(ConflictAction.Overwrite));
    }

    [Fact]
    public void SkipAll_應將所有衝突項目設為跳過()
    {
        var importData = 建立匯入資料();
        var existingProfiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "old-server", Database = "OldDb" },
            new() { Name = "測試環境", Server = "old-test", Database = "OldTest" }
        };
        _exportService.IsEncryptedFormat(Arg.Any<byte[]>()).Returns(false);
        _exportService.ImportFromJson(Arg.Any<byte[]>()).Returns(importData);
        _connectionManager.GetAllProfiles().Returns(existingProfiles);

        var vm = new ImportConnectionsViewModel(_connectionManager, _exportService);
        vm.LoadImportData([1, 2, 3]);
        vm.SkipAllCommand.Execute(null);

        vm.ImportPreviews.Should().AllSatisfy(p => p.ConflictAction.Should().Be(ConflictAction.Skip));
    }

    #endregion
}
