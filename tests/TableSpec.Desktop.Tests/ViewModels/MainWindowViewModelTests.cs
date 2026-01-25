using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.Tests.ViewModels;

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
        _objectTree = new ObjectTreeViewModel(_tableQueryService);
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Documents.Should().BeEmpty();
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
    public void 初始狀態_Documents應為空集合()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.Documents.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_SelectedDocument應為Null()
    {
        // Act
        var vm = new MainWindowViewModel();

        // Assert
        vm.SelectedDocument.Should().BeNull();
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

        // Act
        vm.CloseDocumentCommand.Execute(doc);

        // Assert
        vm.Documents.Should().BeEmpty();
    }

    [Fact]
    public void CloseDocumentCommand_文件不可關閉_應保留文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var doc = new TestDocumentViewModel { Title = "不可關閉", CanClose = false };
        vm.Documents.Add(doc);

        // Act
        vm.CloseDocumentCommand.Execute(doc);

        // Assert
        vm.Documents.Should().ContainSingle();
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

        // Act
        vm.CloseCurrentDocumentCommand.Execute(null);

        // Assert
        vm.Documents.Should().BeEmpty();
    }

    [Fact]
    public void CloseAllDocumentsCommand_應關閉所有可關閉文件()
    {
        // Arrange
        var vm = new MainWindowViewModel();
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

    #endregion

    #region ShowAboutCommand 測試

    [Fact]
    public void ShowAboutCommand_應開啟關於分頁()
    {
        // Arrange
        var vm = new MainWindowViewModel();
        var initialCount = vm.Documents.Count;

        // Act
        vm.ShowAboutCommand.Execute(null);

        // Assert
        vm.Documents.Should().HaveCount(initialCount + 1);
        vm.Documents.Last().Should().BeOfType<AboutDocumentViewModel>();
        vm.Documents.Last().Title.Should().Be("關於");
        vm.SelectedDocument.Should().BeOfType<AboutDocumentViewModel>();
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
            _objectTree);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        _connectionManager.Received().SetCurrentProfile(profile.Id);
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
