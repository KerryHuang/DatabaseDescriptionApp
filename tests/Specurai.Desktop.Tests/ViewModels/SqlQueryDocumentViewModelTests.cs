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
    public void SelectedProfile_變更時_應載入本地連線字串而不影響全域連線()
    {
        // SQL 查詢分頁採連線獨立設計：切換僅更新分頁內連線字串，
        // 不得透過 SetCurrentProfile 影響全域連線（見 SqlQueryDocumentViewModel.OnSelectedProfileChanged）。
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
        _connectionManager.GetConnectionString(profile.Id)
            .Returns("Server=localhost;Database=TestDb;");
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act
        vm.SelectedProfile = profile;

        // Assert
        _connectionManager.Received().GetConnectionString(profile.Id);
        _connectionManager.DidNotReceive().SetCurrentProfile(Arg.Any<Guid>());
        vm.StatusMessage.Should().Contain("已切換至");
    }

    [Fact]
    public async Task SelectedProfile_選到與目前相同的設定檔_執行查詢應改用無連線字串版本以跟隨目前資料庫覆寫()
    {
        // 對應 Finding 1：選到的設定檔與 GetCurrentProfile 相同時，
        // 不應釘住 GetConnectionString(profile.Id)（該設定檔的預設資料庫），
        // 而應保持 null，讓 Repository 於執行當下透過 GetCurrentConnectionString()
        // 重新解析，跟隨側邊欄「目前資料庫」覆寫。
        // Arrange
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "目前連線",
            Server = "localhost",
            Database = "DefaultDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetCurrentProfile().Returns(profile);
        _connectionManager.GetConnectionString(profile.Id)
            .Returns("Server=localhost;Database=DefaultDb;");
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(int));
        _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>()).Returns(dataTable);

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act：建構時已自動選到目前設定檔（觸發 OnSelectedProfileChanged）
        vm.SqlText = "SELECT 1";
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert：查詢應呼叫「不含連線字串」的多載（跟隨目前資料庫覆寫），
        // 而非帶入釘住的 profile 連線字串
        await _sqlQueryRepository.Received(1).ExecuteQueryAsync("SELECT 1");
        await _sqlQueryRepository.DidNotReceive().ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void SelectedProfile_手動選擇不同設定檔_仍應釘住該設定檔的連線字串()
    {
        // Arrange
        var currentProfile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "目前連線",
            Server = "localhost",
            Database = "DefaultDb"
        };
        var otherProfile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "其他連線",
            Server = "otherhost",
            Database = "OtherDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { currentProfile, otherProfile });
        _connectionManager.GetCurrentProfile().Returns(currentProfile);
        _connectionManager.GetConnectionString(otherProfile.Id)
            .Returns("Server=otherhost;Database=OtherDb;");
        _sqlQueryRepository.GetColumnDescriptionsAsync()
            .Returns(new Dictionary<string, string>());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act：手動選擇不同的設定檔
        vm.SelectedProfile = otherProfile;

        // Assert：應釘住該設定檔的連線字串
        _connectionManager.Received().GetConnectionString(otherProfile.Id);
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

    #region Dry Run 測試

    [Fact]
    public void 初始狀態_DryRunWarnings應為空字串()
    {
        var vm = new SqlQueryDocumentViewModel();

        vm.DryRunWarnings.Should().BeEmpty();
        vm.HasDryRunWarnings.Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_成功預演_應顯示筆數與回滾訊息並載入預覽()
    {
        var preview = new DataTable();
        preview.Columns.Add("舊_Name", typeof(object));
        preview.Columns.Add("新_Name", typeof(object));
        preview.Rows.Add("張三", "張三丰");

        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = preview,
                Warnings = ["測試警告"]
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "UPDATE Users SET Name = N'張三丰' WHERE Id = 1"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.QueryResults.Should().HaveCount(1);
        vm.RowCount.Should().Be(1);
        vm.StatusMessage.Should().Contain("影響 1 筆");
        vm.StatusMessage.Should().Contain("已回滾");
        vm.DryRunWarnings.Should().Contain("測試警告");
        vm.HasDryRunWarnings.Should().BeTrue();
    }

    [Fact]
    public async Task DryRun_語法錯誤_應顯示行列訊息()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 18, Message = "Incorrect syntax" }]
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "UPDATE T SET WHERE"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("語法錯誤");
        vm.StatusMessage.Should().Contain("第 1 行");
        vm.QueryResults.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_被拒絕_應顯示拒絕原因()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run"
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "DROP TABLE X"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Fact]
    public async Task DryRun_執行期錯誤_應顯示ExecutionError()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                ExecutionError = "此語句實際執行將會失敗：REFERENCE 條件約束衝突"
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "DELETE FROM Users WHERE Id = 1"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("實際執行將會失敗");
        vm.StatusMessage.Should().Contain("REFERENCE");
    }

    [Fact]
    public async Task 執行一般查詢_應清除DryRun警告()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Insert,
                AffectedRowCount = 1,
                PreviewTable = new DataTable(),
                Warnings = ["IDENTITY 警告"]
            });
        _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataTable());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "INSERT INTO T (A) VALUES (1)"
        };

        await vm.DryRunCommand.ExecuteAsync(null);
        vm.HasDryRunWarnings.Should().BeTrue();

        vm.SqlText = "SELECT 1 AS A";
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.HasDryRunWarnings.Should().BeFalse();
    }

    [Fact]
    public async Task 清除查詢_應清除DryRun警告()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Insert,
                AffectedRowCount = 1,
                PreviewTable = new DataTable(),
                Warnings = ["IDENTITY 警告"]
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "INSERT INTO T (A) VALUES (1)"
        };

        await vm.DryRunCommand.ExecuteAsync(null);
        vm.HasDryRunWarnings.Should().BeTrue();

        vm.ClearQueryCommand.Execute(null);

        vm.DryRunWarnings.Should().BeEmpty();
        vm.HasDryRunWarnings.Should().BeFalse();
    }

    #endregion
}
