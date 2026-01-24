using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

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
            IsDefault = true
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
}
