using System.Data;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Specurai.Application.Models;
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
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>())
            .Returns(new QueryResultWithSchema { Table = dataTable });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);

        // Act：建構時已自動選到目前設定檔（觸發 OnSelectedProfileChanged）
        vm.SqlText = "SELECT 1";
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // Assert：查詢應呼叫「不含連線字串」的多載（跟隨目前資料庫覆寫），
        // 而非帶入釘住的 profile 連線字串
        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync("SELECT 1");
        await _sqlQueryRepository.DidNotReceive().ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<string>());
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
        await _sqlQueryRepository.DidNotReceive().ExecuteQueryWithSchemaAsync(Arg.Any<string>());
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

        _sqlQueryRepository.ExecuteQueryWithSchemaAsync("SELECT * FROM Users")
            .Returns(new QueryResultWithSchema { Table = dataTable });

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
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>())
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
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>())
            .Returns(new QueryResultWithSchema { Table = dataTable });

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
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

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

    #region 選取範圍執行測試

    [Fact]
    public async Task 執行查詢_有選取範圍_應只執行選取文字並標示狀態()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

        var sql = "SELECT 1;\nSELECT 2;";
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = sql,
            SelectionStart = sql.IndexOf("SELECT 2"),
            SelectionEnd = sql.IndexOf("SELECT 2") + "SELECT 2;".Length
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync("SELECT 2;", Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("（選取範圍）");
        vm.QueryHistory.Should().Contain("SELECT 2;");
    }

    [Fact]
    public async Task 執行查詢_無選取範圍_應執行全文且不標示選取()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT 1"
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync("SELECT 1", Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().NotContain("（選取範圍）");
    }

    [Fact]
    public async Task 執行查詢_選取純空白_應視同未選取執行全文()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

        var sql = "SELECT 1;   \nSELECT 2;";
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = sql,
            SelectionStart = 9,   // 「;」後的空白區
            SelectionEnd = 12
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync(sql.Trim(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().NotContain("（選取範圍）");
    }

    [Fact]
    public async Task 執行查詢_反向選取_應正規化索引後執行選取文字()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

        var sql = "SELECT 1;\nSELECT 2;";
        var start = sql.IndexOf("SELECT 2");
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = sql,
            // 游標從後往前拖：Start > End
            SelectionStart = start + "SELECT 2;".Length,
            SelectionEnd = start
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync("SELECT 2;", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 執行查詢_選取索引超出文字長度_應鉗制在合法範圍()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResultWithSchema { Table = new DataTable() });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT 1",
            SelectionStart = 7,
            SelectionEnd = 999   // 文字變短後殘留的舊索引
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await _sqlQueryRepository.Received(1).ExecuteQueryWithSchemaAsync("1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DryRun_有選取範圍_應只預演選取文字並標示狀態()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = new DataTable()
            });

        var sql = "SELECT * FROM T;\nUPDATE T SET A = 1 WHERE Id = 9";
        var start = sql.IndexOf("UPDATE");
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = sql,
            SelectionStart = start,
            SelectionEnd = sql.Length
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        await dryRunRepo.Received(1).DryRunAsync("UPDATE T SET A = 1 WHERE Id = 9", Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("（選取範圍）");
    }

    #endregion

    #region 結果編輯與產生異動SQL測試

    private static QueryResultWithSchema SingleTableResult(bool withKey = true)
    {
        var table = new DataTable();
        table.Columns.Add("EMP_ID", typeof(string));
        table.Columns.Add("EMP_NAME", typeof(string));
        table.Rows.Add("100719", "洪玉如");

        return new QueryResultWithSchema
        {
            Table = table,
            Columns =
            [
                new QueryColumnMetadata { ColumnName = "EMP_ID", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_ID", IsKey = withKey, ClrType = typeof(string) },
                new QueryColumnMetadata { ColumnName = "EMP_NAME", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_NAME", ClrType = typeof(string) }
            ]
        };
    }

    private static QueryResultWithSchema TwoRowSingleTableResult()
    {
        var table = new DataTable();
        table.Columns.Add("EMP_ID", typeof(string));
        table.Columns.Add("EMP_NAME", typeof(string));
        table.Rows.Add("1", "甲");
        table.Rows.Add("2", "乙");

        return new QueryResultWithSchema
        {
            Table = table,
            Columns =
            [
                new QueryColumnMetadata { ColumnName = "EMP_ID", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_ID", IsKey = true, ClrType = typeof(string) },
                new QueryColumnMetadata { ColumnName = "EMP_NAME", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_NAME", ClrType = typeof(string) }
            ]
        };
    }

    private static QueryResultWithSchema MultiTableResult()
    {
        var table = new DataTable();
        table.Columns.Add("A", typeof(string));
        table.Rows.Add("x");

        return new QueryResultWithSchema
        {
            Table = table,
            Columns =
            [
                new QueryColumnMetadata { ColumnName = "A", BaseSchema = "dbo", BaseTable = "T1", BaseColumn = "A", ClrType = typeof(string) },
                new QueryColumnMetadata { ColumnName = "B", BaseSchema = "dbo", BaseTable = "T2", BaseColumn = "B", ClrType = typeof(string) }
            ]
        };
    }

    [Fact]
    public async Task 執行查詢_單表結果_應可編輯()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT * FROM SYS010"
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeTrue();
        vm.QueryResults.Should().HaveCount(1);
    }

    [Fact]
    public async Task 執行查詢_多表結果_應唯讀()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MultiTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT ..."
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    [Fact]
    public async Task 產生異動SQL_無異動_應顯示無異動()
    {
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Any<UpdateSqlRequest>()).Returns(new UpdateSqlResult());
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("無異動");
    }

    [Fact]
    public async Task 產生異動SQL_有異動_應以回呼顯示SQL並用主鍵定位()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE [dbo].[SYS010] ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        string? shownSql = null;
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            ShowGeneratedSqlAsync = sql => { shownSql = sql; return Task.CompletedTask; }
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "洪小玉";   // 模擬編輯

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        shownSql.Should().Contain("UPDATE");
        captured.Should().NotBeNull();
        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID"]);
        captured.IsFallbackKeys.Should().BeFalse();
        captured.Rows[0].Original["EMP_NAME"].Should().Be("洪玉如");   // 快照保留原值
        captured.Rows[0].Current["EMP_NAME"].Should().Be("洪小玉");
        vm.StatusMessage.Should().Contain("1 句");
    }

    [Fact]
    public async Task 產生異動SQL_無主鍵_應呼叫欄位挑選回呼()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult(withKey: false));

        IReadOnlyList<string>? offered = null;
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            PickKeyColumnsAsync = cols => { offered = cols; return Task.FromResult<IReadOnlyList<string>?>(["EMP_ID"]); },
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "改";

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        offered.Should().Contain(["EMP_ID", "EMP_NAME"]);
        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID"]);
        captured.IsFallbackKeys.Should().BeFalse();
    }

    [Fact]
    public async Task 產生異動SQL_無主鍵且略過挑選_應用全欄位Fallback()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult(withKey: false));

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            PickKeyColumnsAsync = _ => Task.FromResult<IReadOnlyList<string>?>(null),
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "改";

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID", "EMP_NAME"]);
        captured.IsFallbackKeys.Should().BeTrue();
    }

    [Fact]
    public async Task 產生異動SQL_結果集合被重排_配對仍正確()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoRowSingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // 模擬 DataGrid 排序重排底層集合
        var first = vm.QueryResults[0];
        vm.QueryResults.RemoveAt(0);
        vm.QueryResults.Add(first);
        vm.QueryResults[1]["EMP_NAME"] = "甲改";   // 改的是原第一列（EMP_ID=1）

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        var changedRow = captured!.Rows.Should().ContainSingle(r => (string?)r.Current["EMP_NAME"] == "甲改").Subject;
        changedRow.Original["EMP_ID"].Should().Be("1");
        changedRow.Original["EMP_NAME"].Should().Be("甲");
    }

    [Fact]
    public async Task 產生異動SQL_不可編輯結果_應提示僅支援單表()
    {
        var generator = Substitute.For<IUpdateSqlGenerator>();
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MultiTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT ..."
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("僅支援單一資料表");
        generator.DidNotReceive().Generate(Arg.Any<UpdateSqlRequest>());
    }

    [Fact]
    public async Task 產生異動SQL_結果集合含快照外的列_應提示重新執行查詢()
    {
        var generator = Substitute.For<IUpdateSqlGenerator>();
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        // 模擬快照外的列（非查詢結果，例如程式其他路徑誤加入）
        vm.QueryResults.Add(new Dictionary<string, object?> { ["EMP_ID"] = "999", ["EMP_NAME"] = "額外" });

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("請重新執行查詢");
        generator.DidNotReceive().Generate(Arg.Any<UpdateSqlRequest>());
    }

    [Fact]
    public async Task DryRun後_結果應不可編輯()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = new DataTable()
            });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.IsResultEditable.Should().BeTrue();

        vm.SqlText = "UPDATE SYS010 SET EMP_NAME = N'x' WHERE EMP_ID = '1'";
        await vm.DryRunCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    [Fact]
    public async Task 清除_應重置可編輯狀態()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.ClearQueryCommand.Execute(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    #endregion
}
