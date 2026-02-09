using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// ColumnSearchDocumentViewModel 測試
/// </summary>
public class ColumnSearchDocumentViewModelTests
{
    private readonly ISqlQueryRepository _sqlQueryRepository;
    private readonly IColumnTypeRepository _columnTypeRepository;
    private readonly IConnectionManager _connectionManager;
    private readonly ITableQueryService _tableQueryService;
    private readonly IColumnSearchService _columnSearchService;

    public ColumnSearchDocumentViewModelTests()
    {
        _sqlQueryRepository = Substitute.For<ISqlQueryRepository>();
        _columnTypeRepository = Substitute.For<IColumnTypeRepository>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _tableQueryService = Substitute.For<ITableQueryService>();
        _columnSearchService = Substitute.For<IColumnSearchService>();
    }

    private ColumnSearchDocumentViewModel CreateViewModel()
    {
        return new ColumnSearchDocumentViewModel(
            _sqlQueryRepository, _columnTypeRepository, _connectionManager,
            _tableQueryService, _columnSearchService);
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().StartWith("欄位搜尋");
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

        // Act
        var vm = CreateViewModel();

        // Assert
        vm.ConnectionProfiles.Should().HaveCount(1);
        vm.SelectedProfile.Should().Be(profiles[0]);
    }

    [Fact]
    public void Constructor_有依賴_應初始化SelectableProfiles()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "正式環境", Server = "prod", Database = "ProdDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetCurrentProfile().Returns(profiles[0]);

        // Act
        var vm = CreateViewModel();

        // Assert
        vm.SelectableProfiles.Should().HaveCount(2);
        vm.SelectableProfiles[0].Profile.Should().Be(profiles[0]);
        vm.SelectableProfiles[0].IsSelected.Should().BeTrue();
        vm.SelectableProfiles[1].Profile.Should().Be(profiles[1]);
        vm.SelectableProfiles[1].IsSelected.Should().BeFalse();
    }

    #endregion

    #region DocumentType 測試

    [Fact]
    public void DocumentType_應為ColumnSearch()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("ColumnSearch");
    }

    [Fact]
    public void DocumentKey_應包含DocumentType和InstanceId()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.DocumentKey.Should().StartWith("ColumnSearch:");
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_ColumnSearchText應為空()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.ColumnSearchText.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_IsSearching應為False()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.IsSearching.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_StatusMessage應為空()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_ShowTypeAnalysis應為False()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.ShowTypeAnalysis.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsUpdating應為False()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.IsUpdating.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_DetailFilter應為全部()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.DetailFilter.Should().Be("全部");
    }

    [Fact]
    public void 初始狀態_Icon應為搜尋圖示()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.Icon.Should().Be("🔍");
    }

    [Fact]
    public void 初始狀態_ColumnSearchResults應為空()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.ColumnSearchResults.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_ColumnGroups應為空()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.ColumnGroups.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_SelectableProfiles應為空()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.SelectableProfiles.Should().BeEmpty();
    }

    #endregion

    #region FilterOptions 測試

    [Fact]
    public void FilterOptions_應包含三個選項()
    {
        // Act
        var vm = new ColumnSearchDocumentViewModel();

        // Assert
        vm.FilterOptions.Should().HaveCount(3);
        vm.FilterOptions.Should().Contain(new[] { "全部", "僅不一致", "僅一致" });
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

        var vm = CreateViewModel();

        // Act
        vm.SelectedProfile = profile;

        // Assert
        _connectionManager.Received().SetCurrentProfile(profile.Id);
    }

    #endregion

    #region ClearColumnSearchCommand 測試

    [Fact]
    public void ClearColumnSearchCommand_執行後_應清空所有資料()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        vm.ColumnSearchText = "UserId";
        vm.ShowTypeAnalysis = true;

        // Act
        vm.ClearColumnSearchCommand.Execute(null);

        // Assert
        vm.ColumnSearchText.Should().BeEmpty();
        vm.ColumnSearchResults.Should().BeEmpty();
        vm.ColumnGroups.Should().BeEmpty();
        vm.SelectedGroup.Should().BeNull();
        vm.SelectedColumnType.Should().BeNull();
        vm.ShowTypeAnalysis.Should().BeFalse();
        vm.StatusMessage.Should().BeEmpty();
    }

    #endregion

    #region SearchColumnsCommand 測試

    [Fact]
    public async Task SearchColumnsCommand_無ColumnSearchText_應不執行()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var vm = CreateViewModel();
        vm.ColumnSearchText = "";

        // Act
        await vm.SearchColumnsCommand.ExecuteAsync(null);

        // Assert
        await _sqlQueryRepository.DidNotReceive().SearchColumnsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SearchColumnsCommand_無勾選連線_應顯示提示()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "測試", Server = "localhost", Database = "TestDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var vm = CreateViewModel();
        vm.ColumnSearchText = "UserId";
        // 不勾選任何連線

        // Act
        await vm.SearchColumnsCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("請至少勾選一個");
    }

    [Fact]
    public async Task SearchColumnsCommand_單一連線_應使用SqlQueryRepository()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "開發環境",
            Server = "localhost",
            Database = "DevDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns(profile);

        var results = new List<ColumnSearchResult>
        {
            new() { SchemaName = "dbo", ObjectName = "Users", ColumnName = "UserId", ObjectType = "TABLE" },
            new() { SchemaName = "dbo", ObjectName = "Orders", ColumnName = "UserId", ObjectType = "TABLE" }
        };
        _sqlQueryRepository.SearchColumnsAsync("UserId").Returns(results);

        var vm = CreateViewModel();
        vm.ColumnSearchText = "UserId";

        // Act
        await vm.SearchColumnsCommand.ExecuteAsync(null);

        // Assert
        vm.ColumnSearchResults.Should().HaveCount(2);
        vm.ColumnSearchResults[0].DatabaseName.Should().Be("DevDb");
        vm.StatusMessage.Should().Contain("找到 2 個");
    }

    [Fact]
    public async Task SearchColumnsCommand_多個連線_應使用ColumnSearchService()
    {
        // Arrange
        var profile1 = new ConnectionProfile
        {
            Id = Guid.NewGuid(), Name = "環境A", Server = "s1", Database = "DbA"
        };
        var profile2 = new ConnectionProfile
        {
            Id = Guid.NewGuid(), Name = "環境B", Server = "s2", Database = "DbB"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile1, profile2 });
        _connectionManager.GetCurrentProfile().Returns(profile1);

        var results = new List<ColumnSearchResult>
        {
            new() { SchemaName = "dbo", ObjectName = "Users", ColumnName = "Id", ObjectType = "TABLE", DatabaseName = "DbA" },
            new() { SchemaName = "dbo", ObjectName = "Orders", ColumnName = "Id", ObjectType = "TABLE", DatabaseName = "DbB" }
        };
        _columnSearchService.SearchColumnsMultiAsync(
            "Id", Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<bool>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(results);

        var vm = CreateViewModel();
        // 勾選兩個連線
        vm.SelectableProfiles[0].IsSelected = true;
        vm.SelectableProfiles[1].IsSelected = true;
        vm.ColumnSearchText = "Id";

        // Act
        await vm.SearchColumnsCommand.ExecuteAsync(null);

        // Assert
        vm.ColumnSearchResults.Should().HaveCount(2);
        vm.StatusMessage.Should().Contain("2 個資料庫");
        await _columnSearchService.Received().SearchColumnsMultiAsync(
            "Id", Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<bool>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchColumnsCommand_搜尋失敗_應顯示錯誤訊息()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(), Name = "測試", Server = "localhost", Database = "TestDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns(profile);
        _sqlQueryRepository.SearchColumnsAsync(Arg.Any<string>())
            .ThrowsAsync(new Exception("搜尋錯誤"));

        var vm = CreateViewModel();
        vm.ColumnSearchText = "Test";

        // Act
        await vm.SearchColumnsCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("搜尋錯誤");
    }

    #endregion

    #region SelectAllProfiles / DeselectAllProfiles 測試

    [Fact]
    public void SelectAllProfilesCommand_應全選所有連線()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "環境A", Server = "s1", Database = "A" },
            new() { Name = "環境B", Server = "s2", Database = "B" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var vm = CreateViewModel();
        vm.SelectableProfiles.Should().AllSatisfy(sp => sp.IsSelected.Should().BeFalse());

        // Act
        vm.SelectAllProfilesCommand.Execute(null);

        // Assert
        vm.SelectableProfiles.Should().AllSatisfy(sp => sp.IsSelected.Should().BeTrue());
    }

    [Fact]
    public void DeselectAllProfilesCommand_應取消全選()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "環境A", Server = "s1", Database = "A" },
            new() { Name = "環境B", Server = "s2", Database = "B" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetCurrentProfile().Returns(profiles[0]);

        var vm = CreateViewModel();
        foreach (var sp in vm.SelectableProfiles) sp.IsSelected = true;

        // Act
        vm.DeselectAllProfilesCommand.Execute(null);

        // Assert
        vm.SelectableProfiles.Should().AllSatisfy(sp => sp.IsSelected.Should().BeFalse());
    }

    #endregion

    #region SelectedGroup 變更測試

    [Fact]
    public void SelectedGroup_變更時_應清空SelectedColumnType和NewLength()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        var group = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.SelectedColumnType = new ColumnTypeInfo { ColumnName = "UserId", MaxLength = 100 };
        vm.NewLength = 50;

        // Act
        vm.SelectedGroup = group;

        // Assert
        vm.SelectedColumnType.Should().BeNull();
        vm.NewLength.Should().Be(0);
    }

    #endregion

    #region SelectedColumnType 變更測試

    [Fact]
    public void SelectedColumnType_變更時_NewLength應設為目前長度()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        var columnInfo = new ColumnTypeInfo
        {
            ColumnName = "Name",
            MaxLength = 100
        };

        // Act
        vm.SelectedColumnType = columnInfo;

        // Assert
        vm.NewLength.Should().Be(100);
    }

    #endregion

    #region PrepareApplyDescription 測試

    [Fact]
    public void PrepareApplyDescriptionCommand_無SelectedSearchResult_應顯示訊息()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        vm.SelectedSearchResult = null;

        // Act
        vm.PrepareApplyDescriptionCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("請先選擇");
    }

    [Fact]
    public void PrepareApplyDescriptionCommand_SelectedSearchResult無說明_應顯示訊息()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        vm.SelectedSearchResult = new ColumnSearchResult
        {
            ColumnName = "Id",
            Description = ""
        };

        // Act
        vm.PrepareApplyDescriptionCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("沒有說明");
    }

    [Fact]
    public void PrepareApplyDescriptionCommand_有說明但無空白項目_應顯示訊息()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        vm.SelectedSearchResult = new ColumnSearchResult
        {
            ColumnName = "Id",
            ObjectType = "TABLE",
            Description = "識別碼"
        };
        vm.ColumnSearchResults.Add(vm.SelectedSearchResult);
        // 所有同名欄位都已有說明
        vm.ColumnSearchResults.Add(new ColumnSearchResult
        {
            ColumnName = "Id",
            ObjectType = "TABLE",
            Description = "其他說明"
        });

        // Act
        vm.PrepareApplyDescriptionCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("沒有需要更新");
    }

    [Fact]
    public void PrepareApplyDescriptionCommand_有空白項目_應顯示確認對話框()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        var sourceResult = new ColumnSearchResult
        {
            ColumnName = "UserId",
            ObjectType = "TABLE",
            ObjectName = "Users",
            SchemaName = "dbo",
            Description = "使用者識別碼"
        };
        var emptyResult = new ColumnSearchResult
        {
            ColumnName = "UserId",
            ObjectType = "TABLE",
            ObjectName = "Orders",
            SchemaName = "dbo",
            Description = ""
        };

        vm.ColumnSearchResults.Add(sourceResult);
        vm.ColumnSearchResults.Add(emptyResult);
        vm.SelectedSearchResult = sourceResult;

        // Act
        vm.PrepareApplyDescriptionCommand.Execute(null);

        // Assert
        vm.ShowApplyDescriptionConfirm.Should().BeTrue();
        vm.EmptyDescriptionCount.Should().Be(1);
        vm.ApplyDescriptionPreview.Should().Contain("使用者識別碼");
    }

    #endregion

    #region CancelApplyDescriptionCommand 測試

    [Fact]
    public void CancelApplyDescriptionCommand_執行後_應關閉確認對話框()
    {
        // Arrange
        var vm = new ColumnSearchDocumentViewModel();
        vm.ShowApplyDescriptionConfirm = true;

        // Act
        vm.CancelApplyDescriptionCommand.Execute(null);

        // Assert
        vm.ShowApplyDescriptionConfirm.Should().BeFalse();
    }

    #endregion

    #region 每個實例有獨立 ID 測試

    [Fact]
    public void 多個實例_DocumentKey應不同()
    {
        // Act
        var vm1 = new ColumnSearchDocumentViewModel();
        var vm2 = new ColumnSearchDocumentViewModel();

        // Assert
        vm1.DocumentKey.Should().NotBe(vm2.DocumentKey);
    }

    #endregion
}
