using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// ConnectionSetupViewModel 測試
/// </summary>
public class ConnectionSetupViewModelTests
{
    private readonly IConnectionManager _connectionManager;

    public ConnectionSetupViewModelTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Profiles.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_有ConnectionManager_應載入設定檔()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "測試環境", Server = "test-server", Database = "TestDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);

        // Act
        var vm = new ConnectionSetupViewModel(_connectionManager);

        // Assert
        vm.Profiles.Should().HaveCount(2);
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_表單欄位應為空()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.Name.Should().BeEmpty();
        vm.Server.Should().BeEmpty();
        vm.Database.Should().BeEmpty();
        vm.Username.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_UseWindowsAuth應為True()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.UseWindowsAuth.Should().BeTrue();
    }

    [Fact]
    public void 初始狀態_IsEditing應為False()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsTesting應為False()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.IsTesting.Should().BeFalse();
    }

    #endregion

    #region SelectedProfile 變更測試

    [Fact]
    public void SelectedProfile_選擇設定檔_應填入表單()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "測試連線",
            Server = "localhost",
            Database = "TestDb",
            AuthType = AuthenticationType.SqlServerAuthentication,
            Username = "sa",
            Password = "password",
            IsDefault = true,
            Environment = DatabaseEnvironment.Production
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        vm.Name.Should().Be("測試連線");
        vm.Server.Should().Be("localhost");
        vm.Database.Should().Be("TestDb");
        vm.UseWindowsAuth.Should().BeFalse();
        vm.Username.Should().Be("sa");
        vm.Password.Should().Be("password");
        vm.IsDefault.Should().BeTrue();
        vm.Environment.Should().Be(DatabaseEnvironment.Production);
        vm.IsEditing.Should().BeTrue();
    }

    [Fact]
    public void SelectedProfile_選擇Windows驗證設定檔_UseWindowsAuth應為True()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "Windows 驗證",
            Server = "localhost",
            Database = "TestDb",
            AuthType = AuthenticationType.WindowsAuthentication
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        vm.UseWindowsAuth.Should().BeTrue();
    }

    [Fact]
    public void SelectedProfile_設為Null_應清空表單()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "測試",
            Server = "server",
            Database = "db"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile;

        // Act
        vm.SelectedProfile = null;

        // Assert
        vm.Name.Should().BeEmpty();
        vm.Server.Should().BeEmpty();
        vm.Database.Should().BeEmpty();
    }

    #endregion

    #region NewProfileCommand 測試

    [Fact]
    public void NewProfileCommand_執行後_應清空表單並設IsEditing為False()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "現有連線",
            Server = "server",
            Database = "db"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile;

        // Act
        vm.NewProfileCommand.Execute(null);

        // Assert
        vm.SelectedProfile.Should().BeNull();
        vm.Name.Should().BeEmpty();
        vm.IsEditing.Should().BeFalse();
    }

    #endregion

    #region TestConnectionCommand 測試

    [Fact]
    public async Task TestConnectionCommand_連線成功_TestResult應顯示成功()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.TestConnectionAsync(Arg.Any<ConnectionProfile>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.Name = "測試";
        vm.Server = "localhost";
        vm.Database = "TestDb";

        // Act
        await vm.TestConnectionCommand.ExecuteAsync(null);

        // Assert
        vm.TestResult.Should().Contain("成功");
        vm.IsTesting.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionCommand_連線失敗_TestResult應顯示失敗()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.TestConnectionAsync(Arg.Any<ConnectionProfile>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.Name = "測試";
        vm.Server = "invalid-server";
        vm.Database = "TestDb";

        // Act
        await vm.TestConnectionCommand.ExecuteAsync(null);

        // Assert
        vm.TestResult.Should().Contain("失敗");
    }

    #endregion

    #region SaveCommand 測試

    [Fact]
    public void SaveCommand_新增設定檔_應呼叫AddProfile()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.Name = "新連線";
        vm.Server = "localhost";
        vm.Database = "NewDb";
        vm.IsEditing = false;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _connectionManager.Received(1).AddProfile(Arg.Is<ConnectionProfile>(p =>
            p.Name == "新連線" &&
            p.Server == "localhost" &&
            p.Database == "NewDb"));
    }

    [Fact]
    public void SaveCommand_更新設定檔_應呼叫UpdateProfile()
    {
        // Arrange
        var existingProfile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "舊名稱",
            Server = "old-server",
            Database = "OldDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { existingProfile });

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = existingProfile;
        vm.Name = "新名稱";
        vm.Server = "new-server";
        vm.Database = "NewDb";

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _connectionManager.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(p =>
            p.Id == existingProfile.Id &&
            p.Name == "新名稱"));
    }

    #endregion

    #region DeleteCommand 測試

    [Fact]
    public void DeleteCommand_有選擇設定檔_應呼叫DeleteProfile()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "待刪除",
            Server = "server",
            Database = "db"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile;

        // Act
        vm.DeleteCommand.Execute(null);

        // Assert
        _connectionManager.Received(1).DeleteProfile(profile.Id);
    }

    [Fact]
    public void DeleteCommand_無選擇設定檔_不應呼叫DeleteProfile()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ConnectionSetupViewModel(_connectionManager);

        // Act
        vm.DeleteCommand.Execute(null);

        // Assert
        _connectionManager.DidNotReceive().DeleteProfile(Arg.Any<Guid>());
    }

    #endregion

    #region ConnectCommand 測試

    [Fact]
    public void ConnectCommand_有選擇設定檔_應呼叫SetCurrentProfile()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "連線目標",
            Server = "server",
            Database = "db"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile;

        // Act
        vm.ConnectCommand.Execute(null);

        // Assert
        _connectionManager.Received(1).SetCurrentProfile(profile.Id);
    }

    #endregion

    #region UseProfile 測試（外部連線）

    [Fact]
    public void ConnectCommand_有選擇外部連線_應呼叫RegisterTemporaryProfilesAndSetCurrentProfile()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        var externalProfile = new ConnectionProfile
        {
            Name = "外部 - 正式",
            Server = "10.0.0.1",
            Database = "ext_db"
        };

        var externalSource = Substitute.For<IExternalConnectionSource>();
        var externalSettings = Substitute.For<IExternalSourceSettings>();
        externalSettings.Load().Returns(new ExternalSourceConfig("some/path", string.Empty));

        var vm = new ConnectionSetupViewModel(_connectionManager, externalSource, externalSettings);
        vm.SelectedExternalProfile = externalProfile;
        vm.SelectedProfile = null;

        // Act
        vm.ConnectCommand.Execute(null);

        // Assert
        _connectionManager.Received(1).RegisterTemporaryProfiles(
            Arg.Is<IReadOnlyList<ConnectionProfile>>(list =>
                list.Count == 1 && list[0] == externalProfile));
        _connectionManager.Received(1).SetCurrentProfile(externalProfile.Id);
    }

    #endregion

    #region IsExternalProfileSelected 測試

    [Fact]
    public void IsExternalProfileSelected_選取外部連線_為True()
    {
        var vm = CreateVmWithExternal();
        var profile = new ConnectionProfile { Name = "外部 - 正式", Server = "10.0.0.1", Database = "ext_db" };

        vm.SelectedExternalProfile = profile;

        vm.IsExternalProfileSelected.Should().BeTrue();
    }

    [Fact]
    public void IsExternalProfileSelected_未選取外部連線_為False()
    {
        var vm = CreateVmWithExternal();

        vm.SelectedExternalProfile = null;

        vm.IsExternalProfileSelected.Should().BeFalse();
    }

    #endregion

    #region CanConnect 測試

    [Fact]
    public void CanConnect_未選取任何連線_為False()
    {
        var vm = CreateVmWithExternal();

        vm.CanConnect.Should().BeFalse();
    }

    [Fact]
    public void CanConnect_選取內部連線_應為True()
    {
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = new ConnectionProfile { Name = "內部測試", Server = "s", Database = "db" };

        vm.CanConnect.Should().BeTrue();
    }

    [Fact]
    public void CanConnect_選取外部連線_應為True()
    {
        var vm = CreateVmWithExternal();
        vm.SelectedExternalProfile = new ConnectionProfile { Name = "外部測試", Server = "s", Database = "db" };

        vm.CanConnect.Should().BeTrue();
    }

    [Fact]
    public void CanConnect_選取停用的內部連線_應為False()
    {
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = new ConnectionProfile
        {
            Name = "停用連線", Server = "s", Database = "db", IsEnabled = false
        };

        vm.CanConnect.Should().BeFalse();
    }

    [Fact]
    public void CanConnect_勾選切換啟用狀態後_按鈕狀態應更新()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        var profile = new ConnectionProfile
        {
            Name = "測試", Server = "s1", Database = "db1", IsEnabled = false
        };
        connectionManager.GetAllProfiles().Returns([profile]);
        var vm = new ConnectionSetupViewModel(connectionManager);
        vm.SelectedProfile = profile;
        vm.CanConnect.Should().BeFalse();

        // 使用者勾選「啟用此連線」；View 已先寫回 profile.IsEnabled，再觸發 ToggleProfileEnabledCommand
        profile.IsEnabled = true;
        vm.ToggleProfileEnabledCommand.Execute(profile);

        vm.CanConnect.Should().BeTrue();
    }

    #endregion

    #region Environment 欄位測試

    [Fact]
    public void EnvironmentOptions_應包含四個環境選項()
    {
        // Assert
        ConnectionSetupViewModel.EnvironmentOptions.Should().Equal(
            DatabaseEnvironment.Development,
            DatabaseEnvironment.Testing,
            DatabaseEnvironment.Staging,
            DatabaseEnvironment.Production);
    }

    [Fact]
    public void 初始狀態_Environment應為Staging()
    {
        // Act
        var vm = new ConnectionSetupViewModel();

        // Assert
        vm.Environment.Should().Be(DatabaseEnvironment.Staging);
    }

    [Fact]
    public void 選取Profile_應載入其Environment()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "正式",
            Server = "prod",
            Database = "ProdDb",
            Environment = DatabaseEnvironment.Production
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        var vm = new ConnectionSetupViewModel(_connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        vm.Environment.Should().Be(DatabaseEnvironment.Production);
    }

    [Fact]
    public void 儲存_應將Environment寫入新Profile()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.Name = "正式";
        vm.Server = "prod";
        vm.Database = "ProdDb";
        vm.Environment = DatabaseEnvironment.Production;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _connectionManager.Received().AddProfile(
            Arg.Is<ConnectionProfile>(p => p.Environment == DatabaseEnvironment.Production));
    }

    [Fact]
    public void 儲存更新_應將Environment寫入更新Profile()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "正式",
            Server = "prod",
            Database = "ProdDb",
            Environment = DatabaseEnvironment.Staging
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile; // 進入編輯模式
        vm.Environment = DatabaseEnvironment.Production;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _connectionManager.Received().UpdateProfile(
            Arg.Is<ConnectionProfile>(p => p.Environment == DatabaseEnvironment.Production));
    }

    [Fact]
    public void 新增_應將Environment重置為Staging()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "正式",
            Server = "prod",
            Database = "ProdDb",
            Environment = DatabaseEnvironment.Production
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        var vm = new ConnectionSetupViewModel(_connectionManager);
        vm.SelectedProfile = profile; // 先載入 Production

        // Act
        vm.NewProfileCommand.Execute(null);

        // Assert
        vm.Environment.Should().Be(DatabaseEnvironment.Staging);
    }

    #endregion

    #region 外部連線 Sync 測試

    private IExternalConnectionSource CreateExternalSource(
        IReadOnlyList<ConnectionProfile>? profiles = null)
    {
        var source = Substitute.For<IExternalConnectionSource>();
        source.SyncAsync().Returns(new ExternalConnectionResult(
            profiles ?? new List<ConnectionProfile>(), []));
        return source;
    }

    private IExternalSourceSettings CreateExternalSettings(
        string sourceDir = "", string keyFile = "")
    {
        var settings = Substitute.For<IExternalSourceSettings>();
        settings.Load().Returns(new ExternalSourceConfig(sourceDir, keyFile));
        return settings;
    }

    private ConnectionSetupViewModel CreateVmWithExternal(
        IExternalConnectionSource? source = null,
        IExternalSourceSettings? settings = null)
    {
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        return new ConnectionSetupViewModel(
            _connectionManager,
            source ?? CreateExternalSource(),
            settings ?? CreateExternalSettings());
    }

    [Fact]
    public void IsSyncButtonVisible_來源目錄為空_為False()
    {
        var vm = CreateVmWithExternal(settings: CreateExternalSettings(""));
        vm.IsSyncButtonVisible.Should().BeFalse();
    }

    [Fact]
    public void IsSyncButtonVisible_來源目錄有值_為True()
    {
        var vm = CreateVmWithExternal(settings: CreateExternalSettings("some/path"));
        vm.IsSyncButtonVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SyncExternalSourceCommand_執行後_ExternalProfiles更新()
    {
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "外部 - 正式", Server = "10.0.0.1", Database = "ext_db" }
        };
        var vm = CreateVmWithExternal(
            source: CreateExternalSource(profiles),
            settings: CreateExternalSettings("some/path"));

        await vm.SyncExternalSourceCommand.ExecuteAsync(null);

        vm.ExternalProfiles.Should().HaveCount(1);
        vm.ExternalProfiles[0].Name.Should().Be("外部 - 正式");
    }

    [Fact]
    public async Task SyncExternalSourceCommand_執行後_SyncStatusMessage顯示數量()
    {
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "A - 正式", Server = "1.1.1.1", Database = "a" },
            new() { Name = "B - 測試", Server = "2.2.2.2", Database = "b" }
        };
        var vm = CreateVmWithExternal(
            source: CreateExternalSource(profiles),
            settings: CreateExternalSettings("some/path"));

        await vm.SyncExternalSourceCommand.ExecuteAsync(null);

        vm.SyncStatusMessage.Should().Contain("2");
    }

    [Fact]
    public void SaveExternalSourceSettings_儲存後_IsSyncButtonVisible更新()
    {
        var settings = Substitute.For<IExternalSourceSettings>();
        settings.Load().Returns(new ExternalSourceConfig(string.Empty, string.Empty));
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());

        var vm = new ConnectionSetupViewModel(
            _connectionManager, CreateExternalSource(), settings);
        vm.IsSyncButtonVisible.Should().BeFalse();

        // 模擬使用者填入路徑並儲存
        vm.ExternalSourceDirectory = "new/path";
        vm.SaveExternalSourceSettings();

        vm.IsSyncButtonVisible.Should().BeTrue();
    }

    #endregion

    #region IsEnabled 與 ToggleProfileEnabledCommand 測試

    [Fact]
    public void ToggleProfileEnabled_切換啟用_呼叫UpdateProfile存檔()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        var profile = new ConnectionProfile
        {
            Name = "測試", Server = "s1", Database = "db1", IsEnabled = false
        };
        connectionManager.GetAllProfiles().Returns([profile]);
        var vm = new ConnectionSetupViewModel(connectionManager);

        vm.ToggleProfileEnabledCommand.Execute(profile);

        connectionManager.Received(1).UpdateProfile(profile);
    }

    [Fact]
    public void OnSelectedProfileChanged_選取停用的連線_表單顯示未啟用()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        var profile = new ConnectionProfile
        {
            Name = "測試", Server = "s1", Database = "db1", IsEnabled = false
        };
        connectionManager.GetAllProfiles().Returns([profile]);
        var vm = new ConnectionSetupViewModel(connectionManager);

        vm.SelectedProfile = profile;

        vm.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Save_表單啟用為False_建立的Profile為停用()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        connectionManager.GetAllProfiles().Returns([]);
        var vm = new ConnectionSetupViewModel(connectionManager)
        {
            Name = "新連線",
            Server = "s1",
            Database = "db1",
            IsEnabled = false
        };

        vm.SaveCommand.Execute(null);

        connectionManager.Received(1).AddProfile(Arg.Is<ConnectionProfile>(p => !p.IsEnabled));
    }

    [Fact]
    public void NewProfile_清空表單_啟用回到預設True()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        connectionManager.GetAllProfiles().Returns([]);
        var vm = new ConnectionSetupViewModel(connectionManager)
        {
            IsEnabled = false
        };

        vm.NewProfileCommand.Execute(null);

        vm.IsEnabled.Should().BeTrue();
    }

    #endregion
}
