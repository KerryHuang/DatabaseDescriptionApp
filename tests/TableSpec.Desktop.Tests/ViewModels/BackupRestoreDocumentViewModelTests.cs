using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// BackupRestoreDocumentViewModel 測試
/// </summary>
public class BackupRestoreDocumentViewModelTests
{
    private readonly IBackupService _backupService;
    private readonly IConnectionManager _connectionManager;

    public BackupRestoreDocumentViewModelTests()
    {
        _backupService = Substitute.For<IBackupService>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("備份與還原");
    }

    [Fact]
    public void Constructor_有依賴_應載入連線設定()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetCurrentProfile().Returns(profiles[0]);
        _backupService.GetBackupHistory().Returns(new BackupHistory());

        // Act
        var vm = new BackupRestoreDocumentViewModel(_backupService, _connectionManager);

        // Assert
        vm.ConnectionProfiles.Should().HaveCount(1);
    }

    #endregion

    #region DocumentType 測試

    [Fact]
    public void DocumentType_應為BackupRestore()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("BackupRestore");
    }

    [Fact]
    public void DocumentKey_應與DocumentType相同()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.DocumentKey.Should().Be(vm.DocumentType);
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_StatusMessage應為就緒()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.StatusMessage.Should().Be("就緒");
    }

    [Fact]
    public void 初始狀態_IsProcessing應為False()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_SelectedBackupType應為Full()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.SelectedBackupType.Should().Be(BackupType.Full);
    }

    [Fact]
    public void 初始狀態_SelectedRestoreMode應為OverwriteExisting()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.SelectedRestoreMode.Should().Be(RestoreMode.OverwriteExisting);
    }

    [Fact]
    public void 初始狀態_VerifyAfterBackup應為True()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.VerifyAfterBackup.Should().BeTrue();
    }

    [Fact]
    public void 初始狀態_WithRecovery應為True()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.WithRecovery.Should().BeTrue();
    }

    [Fact]
    public void 初始狀態_LastBackupInfo應為無備份記錄()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.LastBackupInfo.Should().Be("無備份記錄");
    }

    #endregion

    #region BackupTypes 測試

    [Fact]
    public void BackupTypes_應包含三種類型()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.BackupTypes.Should().HaveCount(3);
        vm.BackupTypes.Select(t => t.Type).Should()
            .Contain(new[] { BackupType.Full, BackupType.Differential, BackupType.TransactionLog });
    }

    #endregion

    #region RestoreMode 屬性測試

    [Fact]
    public void IsOverwriteMode_當SelectedRestoreMode為OverwriteExisting_應為True()
    {
        // Arrange
        var vm = new BackupRestoreDocumentViewModel();
        vm.SelectedRestoreMode = RestoreMode.OverwriteExisting;

        // Assert
        vm.IsOverwriteMode.Should().BeTrue();
        vm.IsCreateNewMode.Should().BeFalse();
    }

    [Fact]
    public void IsCreateNewMode_當SelectedRestoreMode為CreateNew_應為True()
    {
        // Arrange
        var vm = new BackupRestoreDocumentViewModel();
        vm.SelectedRestoreMode = RestoreMode.CreateNew;

        // Assert
        vm.IsCreateNewMode.Should().BeTrue();
        vm.IsOverwriteMode.Should().BeFalse();
    }

    [Fact]
    public void IsOverwriteMode_設為True_應更新SelectedRestoreMode()
    {
        // Arrange
        var vm = new BackupRestoreDocumentViewModel();
        vm.SelectedRestoreMode = RestoreMode.CreateNew;

        // Act
        vm.IsOverwriteMode = true;

        // Assert
        vm.SelectedRestoreMode.Should().Be(RestoreMode.OverwriteExisting);
    }

    [Fact]
    public void IsCreateNewMode_設為True_應更新SelectedRestoreMode()
    {
        // Arrange
        var vm = new BackupRestoreDocumentViewModel();
        vm.SelectedRestoreMode = RestoreMode.OverwriteExisting;

        // Act
        vm.IsCreateNewMode = true;

        // Assert
        vm.SelectedRestoreMode.Should().Be(RestoreMode.CreateNew);
    }

    #endregion

    #region SelectedProfile 變更測試

    [Fact]
    public void SelectedProfile_變更時_應更新CurrentDatabaseName和CurrentServerName()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "test-server",
            Database = "TestDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _backupService.GetBackupHistory().Returns(new BackupHistory());

        var vm = new BackupRestoreDocumentViewModel(_backupService, _connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        vm.CurrentDatabaseName.Should().Be("TestDb");
        vm.CurrentServerName.Should().Be("test-server");
    }

    [Fact]
    public void SelectedProfile_設為Null_應清空相關屬性()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "test-server",
            Database = "TestDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns(profile);
        _backupService.GetBackupHistory().Returns(new BackupHistory());

        var vm = new BackupRestoreDocumentViewModel(_backupService, _connectionManager);

        // Act
        vm.SelectedProfile = null;

        // Assert
        vm.CurrentDatabaseName.Should().BeEmpty();
        vm.CurrentServerName.Should().BeEmpty();
        vm.LastBackupInfo.Should().Be("無備份記錄");
    }

    #endregion

    #region CancelOperationCommand 測試

    [Fact]
    public void CancelOperationCommand_執行後_StatusMessage應更新()
    {
        // Arrange
        var vm = new BackupRestoreDocumentViewModel();

        // Act
        vm.CancelOperationCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("取消");
    }

    #endregion

    #region RefreshHistoryCommand 測試

    [Fact]
    public void RefreshHistoryCommand_應重新載入歷史記錄()
    {
        // Arrange
        var history = new BackupHistory();
        history.Add(new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            ConnectionName = "測試",
            DatabaseName = "TestDb",
            ServerName = "localhost",
            BackupFilePath = @"C:\test.bak",
            BackupTime = DateTime.Now,
            BackupType = BackupType.Full
        });

        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _backupService.GetBackupHistory().Returns(history);

        var vm = new BackupRestoreDocumentViewModel(_backupService, _connectionManager);

        // Act
        vm.RefreshHistoryCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("重新整理");
    }

    #endregion

    #region HistoryFilterConnection 測試

    [Fact]
    public void HistoryFilterConnections_初始應包含全部()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.HistoryFilterConnections.Should().Contain("全部");
    }

    [Fact]
    public void HistoryFilterConnection_初始值應為全部()
    {
        // Act
        var vm = new BackupRestoreDocumentViewModel();

        // Assert
        vm.HistoryFilterConnection.Should().Be("全部");
    }

    #endregion
}

/// <summary>
/// BackupTypeItem 測試
/// </summary>
public class BackupTypeItemTests
{
    [Fact]
    public void BackupTypeItem_應正確設定屬性()
    {
        // Act
        var item = new BackupTypeItem(BackupType.Full, "完整備份");

        // Assert
        item.Type.Should().Be(BackupType.Full);
        item.DisplayName.Should().Be("完整備份");
    }
}
