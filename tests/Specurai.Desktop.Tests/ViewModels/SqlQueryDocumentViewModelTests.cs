using System.Data;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// SqlQueryDocumentViewModel 測試
/// </summary>
public class SqlQueryDocumentViewModelTests
{
    private readonly ISqlQueryRepository _sqlQueryRepository;
    private readonly IConnectionManager _connectionManager;

    public SqlQueryDocumentViewModelTests()
    {
        _sqlQueryRepository = Substitute.For<ISqlQueryRepository>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().StartWith("SQL 查詢");
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
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        // Act
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Assert
        vm.ConnectionProfiles.Should().HaveCount(1);
        vm.SelectedProfile.Should().Be(profiles[0]);
    }

    #endregion

    #region DocumentType 測試

    [Fact]
    public void DocumentType_應為SqlQuery()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("SqlQuery");
    }

    [Fact]
    public void DocumentKey_應包含DocumentType和InstanceId()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.DocumentKey.Should().StartWith("SqlQuery:");
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_SqlText應為空()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.SqlText.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_IsExecuting應為False()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.IsExecuting.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_StatusMessage應為空()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_RowCount應為0()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.RowCount.Should().Be(0);
    }

    [Fact]
    public void 初始狀態_ExecutionTimeMs應為0()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.ExecutionTimeMs.Should().Be(0);
    }

    [Fact]
    public void 初始狀態_QueryResults應為空()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.QueryResults.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_QueryHistory應為空()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.QueryHistory.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_Icon應為筆記圖示()
    {
        // Act
        var vm = new SqlQueryDocumentViewModel();

        // Assert
        vm.Icon.Should().Be("📝");
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
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        _connectionManager.Received().SetCurrentProfile(profile.Id);
        vm.StatusMessage.Should().Contain("已切換至");
    }

    #endregion

    #region ClearQueryCommand 測試

    [Fact]
    public void ClearQueryCommand_執行後_應清空所有資料()
    {
        // Arrange
        var vm = new SqlQueryDocumentViewModel();
        vm.SqlText = "SELECT * FROM Users";

        // Act
        vm.ClearQueryCommand.Execute(null);

        // Assert
        vm.SqlText.Should().BeEmpty();
        vm.QueryResults.Should().BeEmpty();
        vm.StatusMessage.Should().BeEmpty();
        vm.RowCount.Should().Be(0);
        vm.ExecutionTimeMs.Should().Be(0);
    }

    #endregion

    #region LoadFromHistoryCommand 測試

    [Fact]
    public void LoadFromHistoryCommand_有SQL_應載入SqlText()
    {
        // Arrange
        var vm = new SqlQueryDocumentViewModel();
        var sql = "SELECT * FROM Users";

        // Act
        vm.LoadFromHistoryCommand.Execute(sql);

        // Assert
        vm.SqlText.Should().Be(sql);
    }

    [Fact]
    public void LoadFromHistoryCommand_SQL為空_應不載入()
    {
        // Arrange
        var vm = new SqlQueryDocumentViewModel();
        vm.SqlText = "原始SQL";

        // Act
        vm.LoadFromHistoryCommand.Execute(null);

        // Assert
        vm.SqlText.Should().Be("原始SQL");
    }

    [Fact]
    public void LoadFromHistoryCommand_SQL為空字串_應不載入()
    {
        // Arrange
        var vm = new SqlQueryDocumentViewModel();
        vm.SqlText = "原始SQL";

        // Act
        vm.LoadFromHistoryCommand.Execute("");

        // Assert
        vm.SqlText.Should().Be("原始SQL");
    }

    #endregion

    #region ExecuteQueryCommand 測試

    [Fact]
    public async Task ExecuteQueryCommand_無SqlText_應不執行()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);
        vm.SqlText = "";

        // Act
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert
        await _sqlQueryRepository.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteQueryCommand_有SqlText_應執行查詢()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        dataTable.Columns.Add("Name", typeof(string));
        dataTable.Rows.Add(1, "Test");
        dataTable.Rows.Add(2, "User");

        _sqlQueryRepository.ExecuteQueryAsync("SELECT * FROM Users")
            .Returns(dataTable);

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);
        vm.SqlText = "SELECT * FROM Users";

        // Act
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert
        vm.RowCount.Should().Be(2);
        vm.QueryResults.Should().HaveCount(2);
        vm.StatusMessage.Should().Contain("查詢完成");
        vm.QueryHistory.Should().Contain("SELECT * FROM Users");
    }

    [Fact]
    public async Task ExecuteQueryCommand_查詢失敗_應顯示錯誤訊息()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());
        _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>())
            .ThrowsAsync(new Exception("語法錯誤"));

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);
        vm.SqlText = "SELEC * FROM";

        // Act
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("錯誤");
        vm.QueryResults.Should().BeEmpty();
        vm.RowCount.Should().Be(0);
    }

    #endregion

    #region 查詢歷史記錄測試

    [Fact]
    public async Task ExecuteQueryCommand_重複執行相同SQL_歷史記錄應不重複()
    {
        // Arrange
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>()).Returns(dataTable);

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act
        vm.SqlText = "SELECT 1";
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert
        vm.QueryHistory.Count(h => h == "SELECT 1").Should().Be(1);
    }

    #endregion

    #region 每個實例有獨立 ID 測試

    [Fact]
    public void 多個實例_DocumentKey應不同()
    {
        // Act
        var vm1 = new SqlQueryDocumentViewModel();
        var vm2 = new SqlQueryDocumentViewModel();

        // Assert
        vm1.DocumentKey.Should().NotBe(vm2.DocumentKey);
    }

    [Fact]
    public void 多個實例_Title應包含不同編號()
    {
        // Act
        var vm1 = new SqlQueryDocumentViewModel();
        var vm2 = new SqlQueryDocumentViewModel();

        // Assert
        vm1.Title.Should().NotBe(vm2.Title);
    }

    #endregion
}
