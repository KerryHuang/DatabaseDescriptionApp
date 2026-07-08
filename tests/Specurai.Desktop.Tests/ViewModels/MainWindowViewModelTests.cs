using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Desktop.Services;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// MainWindowViewModel 測試
/// </summary>
public class MainWindowViewModelTests
{
    private readonly IConnectionManager _connectionManager;
    private readonly IExportService _exportService;
    private readonly ITableQueryService _tableQueryService;
    private readonly ISqlQueryRepository _sqlQueryRepository;
    private readonly IColumnTypeRepository _columnTypeRepository;
    private readonly ObjectTreeViewModel _objectTree;

    public MainWindowViewModelTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _exportService = Substitute.For<IExportService>();
        _tableQueryService = Substitute.For<ITableQueryService>();
        _sqlQueryRepository = Substitute.For<ISqlQueryRepository>();
        _columnTypeRepository = Substitute.For<IColumnTypeRepository>();
        _objectTree = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Documents.Should().ContainSingle().Which.Should().BeOfType<AboutDocumentViewModel>();
        vm.ConnectionProfiles.Should().BeEmpty();
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_StatusMessage應為就緒()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.StatusMessage.Should().Be("就緒");
    }

    [Fact]
    public void 初始狀態_IsConnected應為False()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsExporting應為False()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.IsExporting.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_Documents應包含關於分頁()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.Documents.Should().ContainSingle().Which.Should().BeOfType<AboutDocumentViewModel>();
    }

    [Fact]
    public void 初始狀態_SelectedDocument應為關於分頁()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.SelectedDocument.Should().BeOfType<AboutDocumentViewModel>();
    }

    #endregion

    #region Documents 管理測試

    [Fact]
    public void CloseDocumentCommand_文件可關閉_應移除文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc = new TestDocumentViewModel { Title = "測試文件", CanClose = true };
        vm.Documents.Add(doc);
        var countBefore = vm.Documents.Count;

        // Act
        vm.CloseDocumentCommand.Execute(doc);

        // Assert
        vm.Documents.Should().HaveCount(countBefore - 1);
        vm.Documents.Should().NotContain(doc);
    }

    [Fact]
    public void CloseDocumentCommand_文件不可關閉_應保留文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc = new TestDocumentViewModel { Title = "不可關閉", CanClose = false };
        vm.Documents.Add(doc);
        var countBefore = vm.Documents.Count;

        // Act
        vm.CloseDocumentCommand.Execute(doc);

        // Assert
        vm.Documents.Should().HaveCount(countBefore);
    }

    [Fact]
    public void CloseDocumentCommand_關閉選中文件_應選擇下一個文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc1 = new TestDocumentViewModel { Title = "文件1", CanClose = true };
        var doc2 = new TestDocumentViewModel { Title = "文件2", CanClose = true };
        vm.Documents.Add(doc1);
        vm.Documents.Add(doc2);
        vm.SelectedDocument = doc2;

        // Act
        vm.CloseDocumentCommand.Execute(doc2);

        // Assert
        vm.SelectedDocument.Should().Be(doc1);
    }

    [Fact]
    public void CloseCurrentDocumentCommand_有選中且可關閉_應關閉當前文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc = new TestDocumentViewModel { Title = "當前文件", CanClose = true };
        vm.Documents.Add(doc);
        vm.SelectedDocument = doc;
        var countBefore = vm.Documents.Count;

        // Act
        vm.CloseCurrentDocumentCommand.Execute(null);

        // Assert
        vm.Documents.Should().HaveCount(countBefore - 1);
        vm.Documents.Should().NotContain(doc);
    }

    [Fact]
    public void CloseAllDocumentsCommand_應關閉所有可關閉文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        vm.Documents.Clear();
        var doc1 = new TestDocumentViewModel { Title = "可關閉1", CanClose = true };
        var doc2 = new TestDocumentViewModel { Title = "不可關閉", CanClose = false };
        var doc3 = new TestDocumentViewModel { Title = "可關閉2", CanClose = true };
        vm.Documents.Add(doc1);
        vm.Documents.Add(doc2);
        vm.Documents.Add(doc3);

        // Act
        vm.CloseAllDocumentsCommand.Execute(null);

        // Assert
        vm.Documents.Should().ContainSingle();
        vm.Documents.First().Title.Should().Be("不可關閉");
    }

    [Fact]
    public void 初始狀態_應預設開啟關於分頁()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.Documents.Should().ContainSingle();
        vm.Documents.First().Should().BeOfType<AboutDocumentViewModel>();
        vm.SelectedDocument.Should().BeOfType<AboutDocumentViewModel>();
    }

    #endregion

    #region ShowAboutCommand 測試

    [Fact]
    public void ShowAboutCommand_應切換到關於分頁()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc = new TestDocumentViewModel { Title = "其他", CanClose = true };
        vm.Documents.Add(doc);
        vm.SelectedDocument = doc;

        // Act
        vm.ShowAboutCommand.Execute(null);

        // Assert
        vm.SelectedDocument.Should().BeOfType<AboutDocumentViewModel>();
        vm.Documents.Count(d => d.DocumentKey == "About").Should().Be(1);
    }

    [Fact]
    public void ShowAboutCommand_重複執行_應只開啟一個分頁()
    {
        // Arrange
        var vm = new MainWindowViewModel();

        // Act
        vm.ShowAboutCommand.Execute(null);
        var countAfterFirst = vm.Documents.Count;
        vm.ShowAboutCommand.Execute(null);

        // Assert
        vm.Documents.Should().HaveCount(countAfterFirst);
        vm.Documents.Count(d => d.DocumentKey == "About").Should().Be(1);
    }

    #endregion

    #region 側邊欄切換測試

    [Fact]
    public void 初始狀態_IsSidebarOpen應為True()
    {
        // Arrange - 確保初始狀態一致
        UserPreferences.IsSidebarOpen = true;

        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.IsSidebarOpen.Should().BeTrue();
    }

    [Fact]
    public void ToggleSidebarCommand_執行後_應切換為False()
    {
        // Arrange - 確保初始狀態一致
        UserPreferences.IsSidebarOpen = true;
        var vm = new MainWindowViewModel();

        // Act
        vm.ToggleSidebarCommand.Execute(null);

        // Assert
        vm.IsSidebarOpen.Should().BeFalse();
    }

    [Fact]
    public void ToggleSidebarCommand_執行兩次_應回到True()
    {
        // Arrange - 確保初始狀態一致
        UserPreferences.IsSidebarOpen = true;
        var vm = new MainWindowViewModel();

        // Act
        vm.ToggleSidebarCommand.Execute(null);
        vm.ToggleSidebarCommand.Execute(null);

        // Assert
        vm.IsSidebarOpen.Should().BeTrue();
    }

    #endregion

    #region SelectedProfile 變更測試

    [Fact]
    public void SelectedProfile_變更時_應通知ConnectionManager()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "測試",
            Server = "localhost",
            Database = "TestDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        var vm = new MainWindowViewModel(
            _connectionManager,
            _exportService,
            _tableQueryService,
            _sqlQueryRepository,
            _columnTypeRepository,
            _objectTree,
            new UpdateNotificationViewModel());

        // Act
        vm.SelectedProfile = profile;

        // Assert
        _connectionManager.Received().SetCurrentProfile(profile.Id);
    }

    #endregion

    #region Production 防呆判斷

    private MainWindowViewModel CreateVmWithCurrentProfile(ConnectionProfile? current)
    {
        _connectionManager.GetCurrentProfile().Returns(current);
        _connectionManager.GetCurrentDatabase().Returns(current?.Database);
        return new MainWindowViewModel(
            _connectionManager,
            _exportService,
            _tableQueryService,
            _sqlQueryRepository,
            _columnTypeRepository,
            _objectTree,
            new UpdateNotificationViewModel());
    }

    [Fact]
    public void IsCurrentProfileProduction_當前為Production_應為True()
    {
        // Arrange
        var vm = CreateVmWithCurrentProfile(new ConnectionProfile
        {
            Name = "正式", Server = "prod", Database = "ProdDb",
            Environment = DatabaseEnvironment.Production
        });

        // Assert
        vm.IsCurrentProfileProduction.Should().BeTrue();
        vm.CurrentEnvironmentDatabase.Should().Be("ProdDb");
    }

    [Fact]
    public void IsCurrentProfileProduction_當前為Staging_應為False()
    {
        // Arrange
        var vm = CreateVmWithCurrentProfile(new ConnectionProfile
        {
            Name = "預備", Server = "stg", Database = "StgDb",
            Environment = DatabaseEnvironment.Staging
        });

        // Assert
        vm.IsCurrentProfileProduction.Should().BeFalse();
        vm.CurrentEnvironmentDatabase.Should().Be("StgDb");
    }

    [Fact]
    public void IsCurrentProfileProduction_無當前連線_應為False()
    {
        // Arrange
        var vm = CreateVmWithCurrentProfile(null);

        // Assert
        vm.IsCurrentProfileProduction.Should().BeFalse();
        vm.CurrentEnvironmentDatabase.Should().BeNull();
    }

    #endregion

    #region 物件載入完成訊息含資料庫名稱

    private MainWindowViewModel CreateVmForLoadObjects(ConnectionProfile profile, string? currentDatabase)
    {
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns(profile);
        _connectionManager.GetCurrentDatabase().Returns(currentDatabase);
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        return new MainWindowViewModel(
            _connectionManager,
            _exportService,
            _tableQueryService,
            _sqlQueryRepository,
            _columnTypeRepository,
            _objectTree,
            new UpdateNotificationViewModel());
    }

    [Fact]
    public void LoadObjectsAsync完成_有目前資料庫名稱_StatusMessage應包含資料庫名稱()
    {
        // Arrange
        var profile = new ConnectionProfile { Name = "測試", Server = "localhost", Database = "TestDb" };

        // Act
        var vm = CreateVmForLoadObjects(profile, "TestDb");

        // Assert
        vm.StatusMessage.Should().Be("已載入 TestDb，共 0 個物件");
    }

    [Fact]
    public void LoadObjectsAsync完成_無目前資料庫名稱_StatusMessage應為預設格式()
    {
        // Arrange
        var profile = new ConnectionProfile { Name = "測試", Server = "localhost", Database = "TestDb" };

        // Act
        var vm = CreateVmForLoadObjects(profile, null);

        // Assert
        vm.StatusMessage.Should().Be("已載入 0 個物件");
    }

    #endregion
}

/// <summary>
/// 測試用的 DocumentViewModel
/// </summary>
public class TestDocumentViewModel : DocumentViewModel
{
    private string _testTitle = "Test";
    private bool _testCanClose = true;

    public override string DocumentType => "Test";

    public new string Title
    {
        get => _testTitle;
        set
        {
            _testTitle = value;
            base.Title = value;
        }
    }

    public override string DocumentKey => $"Test:{_testTitle}";

    public new bool CanClose
    {
        get => _testCanClose;
        set
        {
            _testCanClose = value;
            base.CanClose = value;
        }
    }
}
