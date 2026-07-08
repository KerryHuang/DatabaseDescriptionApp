# SSMS 式資料庫瀏覽實作計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 左側面板從「單一連線資料庫」改為 SSMS 式階層瀏覽：連線（Host）→ 所有使用者資料庫 → Tables/Views/SP/Functions；點選資料庫即切換全域當前資料庫，全系統功能跟隨。

**Architecture:** 方案 A「連線字串層覆寫」——`ConnectionManager` 新增 in-memory 當前資料庫覆寫，`GetCurrentConnectionString()` 組字串時以覆寫值取代 `InitialCatalog`。全系統 ~20 個 Repository 透過 `Func<string?>` 每次查詢即時解析連線字串，因此 Repository/Service 層零改動。側邊欄樹狀圖加一層資料庫節點；MCP 加 `list_databases`/`switch_database` 工具；CLI 加 `databases` 命令。

**Tech Stack:** .NET 8、Avalonia 11 + CommunityToolkit.Mvvm、Microsoft.Data.SqlClient、MCP SDK、System.CommandLine、xUnit + NSubstitute + FluentAssertions

**設計文件:** `docs/superpowers/specs/2026-07-08-ssms-style-database-browsing-design.md`

## Global Constraints

- UI 文字、註解、Commit 訊息一律使用繁體中文
- Clean Architecture 分層：Domain → Application → Infrastructure → Desktop/McpServer/Cli（`.claude/rules/clean-architecture.md`）
- ViewModel 使用 CommunityToolkit.Mvvm（`[ObservableProperty]`、`[RelayCommand]`）；每個 ViewModel 需有設計時無參數建構函式 + DI 建構函式（`.claude/rules/mvvm-patterns.md`）
- 檔案使用 UTF-8 無 BOM
- TDD：先寫失敗測試，再寫最小實作
- Commit 訊息結尾加 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- 建置前確保 Specurai 桌面程式與 MCP Server 未執行（執行中會鎖 DLL 導致全方案 build 失敗）
- `ConnectionProfile` 實體與 `connections.json` 格式**不變**（`Database` 欄位語意變為「預設資料庫」）
- 覆寫為 session 層級，**不持久化**
- 設計釐清：`GetDatabasesAsync` 列舉失敗時**擲出例外**，由呼叫端 degrade（側邊欄退回僅顯示當前資料庫、MCP 回傳友善錯誤、CLI 顯示錯誤）——設計文件 §1 寫在 ConnectionManager 內 degrade，以本計劃（呼叫端 degrade）為準，語意與設計文件 §5 錯誤處理一致
- 設計釐清：切換到的資料庫若中途離線，物件載入失敗訊息顯示於狀態列（`LastError` → StatusMessage），**不自動還原到原資料庫**（還原需記憶前一庫並二次觸發事件，複雜度不划算；使用者點選其他資料庫節點即可恢復）——設計文件 §5「維持在原資料庫」以此為準

---

### Task 1: ConnectionManager 當前資料庫覆寫（核心）

**Files:**
- Modify: `src/Specurai.Application/Services/IConnectionManager.cs`
- Modify: `src/Specurai.Infrastructure/Services/ConnectionManager.cs`
- Test (Create): `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`

**Interfaces:**
- Consumes: 既有 `ConnectionProfile`（`Id`/`Name`/`Server`/`Database`/`AuthType`）、`SqlConnectionStringBuilder`
- Produces（後續 Task 依賴的精確簽章）:
  - `string? GetCurrentDatabase()`
  - `void SetCurrentDatabase(string? databaseName)`
  - `Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)`（當前設定檔）
  - `Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default)`
  - `event EventHandler<string?>? CurrentDatabaseChanged`（參數 = 新的生效資料庫名稱）
  - `ConnectionManager` 新增 `public ConnectionManager(string configPath)` 建構函式（測試用）

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// ConnectionManager 當前資料庫覆寫測試
/// </summary>
public class ConnectionManagerTests : IDisposable
{
    private readonly string _configPath = Path.Combine(
        Path.GetTempPath(), $"specurai-test-connections-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private static ConnectionProfile CreateProfile(string name, string database) => new()
    {
        Name = name,
        Server = "localhost",
        Database = database,
        AuthType = AuthenticationType.WindowsAuthentication
    };

    [Fact]
    public void GetCurrentDatabase_未設定覆寫_應回傳設定檔預設資料庫()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.GetCurrentDatabase().Should().Be("DefaultDb");
    }

    [Fact]
    public void GetCurrentDatabase_無任何設定檔_應回傳Null()
    {
        var manager = new ConnectionManager(_configPath);

        manager.GetCurrentDatabase().Should().BeNull();
    }

    [Fact]
    public void SetCurrentDatabase_設定覆寫_GetCurrentDatabase應回傳覆寫值()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.SetCurrentDatabase("OtherDb");

        manager.GetCurrentDatabase().Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_設定覆寫_連線字串InitialCatalog應為覆寫值()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));

        manager.SetCurrentDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(manager.GetCurrentConnectionString());
        builder.InitialCatalog.Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_傳入Null_應重設回設定檔預設資料庫()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        manager.SetCurrentDatabase("OtherDb");

        manager.SetCurrentDatabase(null);

        manager.GetCurrentDatabase().Should().Be("DefaultDb");
        var builder = new SqlConnectionStringBuilder(manager.GetCurrentConnectionString());
        builder.InitialCatalog.Should().Be("DefaultDb");
    }

    [Fact]
    public void SetCurrentDatabase_變更資料庫_應觸發CurrentDatabaseChanged事件()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        string? raisedDatabase = null;
        manager.CurrentDatabaseChanged += (_, db) => raisedDatabase = db;

        manager.SetCurrentDatabase("OtherDb");

        raisedDatabase.Should().Be("OtherDb");
    }

    [Fact]
    public void SetCurrentDatabase_相同資料庫_不應觸發事件()
    {
        var manager = new ConnectionManager(_configPath);
        manager.AddProfile(CreateProfile("測試連線", "DefaultDb"));
        var raised = false;
        manager.CurrentDatabaseChanged += (_, _) => raised = true;

        manager.SetCurrentDatabase("DefaultDb");

        raised.Should().BeFalse();
    }

    [Fact]
    public void SetCurrentProfile_切換設定檔_應清除資料庫覆寫()
    {
        var manager = new ConnectionManager(_configPath);
        var p1 = CreateProfile("連線1", "Db1");
        var p2 = CreateProfile("連線2", "Db2");
        manager.AddProfile(p1);
        manager.AddProfile(p2);
        manager.SetCurrentProfile(p1.Id);
        manager.SetCurrentDatabase("OtherDb");

        manager.SetCurrentProfile(p2.Id);

        manager.GetCurrentDatabase().Should().Be("Db2");
    }

    [Fact]
    public void GetConnectionString_指定ProfileId_不受當前資料庫覆寫影響()
    {
        var manager = new ConnectionManager(_configPath);
        var p1 = CreateProfile("連線1", "Db1");
        manager.AddProfile(p1);
        manager.SetCurrentDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(manager.GetConnectionString(p1.Id));
        builder.InitialCatalog.Should().Be("Db1");
    }

    [Fact]
    public async Task GetDatabasesAsync_無設定檔_應回傳空清單()
    {
        var manager = new ConnectionManager(_configPath);

        var databases = await manager.GetDatabasesAsync();

        databases.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTests"
```

預期：**編譯失敗**（`ConnectionManager(string)`、`GetCurrentDatabase` 等不存在）。

- [ ] **Step 3: 修改 IConnectionManager 介面**

在 `src/Specurai.Application/Services/IConnectionManager.cs` 的 `event EventHandler<ConnectionProfile?>? CurrentProfileChanged;`（第 77 行）之前插入：

```csharp
    /// <summary>
    /// 取得目前生效的資料庫名稱（覆寫值優先，否則為目前設定檔的預設資料庫）
    /// </summary>
    string? GetCurrentDatabase();

    /// <summary>
    /// 設定目前資料庫覆寫（null 表示重設回設定檔預設資料庫）。
    /// 僅存在於記憶體中不持久化；切換連線設定檔時自動清除。
    /// </summary>
    void SetCurrentDatabase(string? databaseName);

    /// <summary>
    /// 取得目前連線伺服器上的使用者資料庫清單（database_id > 4 且 ONLINE）。
    /// 無目前設定檔時回傳空清單；連線或查詢失敗時擲出例外，由呼叫端決定 degrade 行為。
    /// </summary>
    Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得指定連線設定檔伺服器上的使用者資料庫清單。
    /// 連線或查詢失敗時擲出例外，由呼叫端決定 degrade 行為。
    /// </summary>
    Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>
    /// 目前資料庫變更事件（參數為新的生效資料庫名稱）
    /// </summary>
    event EventHandler<string?>? CurrentDatabaseChanged;
```

- [ ] **Step 4: 實作 ConnectionManager**

修改 `src/Specurai.Infrastructure/Services/ConnectionManager.cs`：

(a) 欄位與事件（在 `private Guid? _currentProfileId;` 之後）：

```csharp
    private string? _currentDatabaseOverride;
```

在 `public event EventHandler<ConnectionProfile?>? CurrentProfileChanged;` 之後：

```csharp
    public event EventHandler<string?>? CurrentDatabaseChanged;
```

(b) 建構函式改為委派 + 測試用建構函式（取代原本的 `public ConnectionManager()`）：

```csharp
    public ConnectionManager() : this(GetConfigPath())
    {
    }

    /// <summary>
    /// 指定設定檔路徑的建構函式（測試用）
    /// </summary>
    public ConnectionManager(string configPath)
    {
        _configPath = configPath;
        LoadProfiles();
    }
```

(c) `SetCurrentProfile` 切換時清除覆寫（整個方法取代）：

```csharp
    public void SetCurrentProfile(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
        {
            _currentProfileId = profileId;
            // 切換連線設定檔時重設資料庫覆寫，回到新設定檔的預設資料庫
            _currentDatabaseOverride = null;
            CurrentProfileChanged?.Invoke(this, profile);
        }
    }
```

(d) `GetCurrentConnectionString` 套用覆寫（整個方法取代）：

```csharp
    public string? GetCurrentConnectionString()
    {
        var profile = GetCurrentProfile();
        if (profile == null)
            return null;

        var connectionString = BuildConnectionString(profile);
        if (_currentDatabaseOverride == null)
            return connectionString;

        // 目前資料庫覆寫僅影響「目前連線」，不影響 BuildConnectionString / GetConnectionString(profileId)
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = _currentDatabaseOverride
        };
        return builder.ConnectionString;
    }
```

(e) 新增方法（放在 `GetCurrentConnectionString` 之後）：

```csharp
    public string? GetCurrentDatabase()
        => _currentDatabaseOverride ?? GetCurrentProfile()?.Database;

    public void SetCurrentDatabase(string? databaseName)
    {
        var before = GetCurrentDatabase();
        _currentDatabaseOverride = databaseName;
        var after = GetCurrentDatabase();

        // 生效資料庫沒變就不觸發事件，避免訂閱端重複載入
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            CurrentDatabaseChanged?.Invoke(this, after);
        }
    }

    public Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var profile = GetCurrentProfile();
        if (profile == null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return GetDatabasesAsync(profile, ct);
    }

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(BuildConnectionString(profile));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 ORDER BY name";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
```

- [ ] **Step 5: 執行測試確認通過 + 全方案建置**

```bash
dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTests"
dotnet build
```

預期：ConnectionManagerTests 10 個測試全數 PASS；全方案建置成功（若有其他 `IConnectionManager` 手寫實作漏網，建置會揪出——NSubstitute mock 不受影響）。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Application/Services/IConnectionManager.cs src/Specurai.Infrastructure/Services/ConnectionManager.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs
git commit -m "feat: ConnectionManager 新增當前資料庫覆寫與資料庫列舉

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: ObjectTreeViewModel 資料庫節點層

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/ObjectTreeViewModel.cs`
- Modify: `tests/Specurai.Desktop.Tests/ViewModels/ObjectTreeViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `IConnectionManager.GetDatabasesAsync()` / `GetCurrentDatabase()` / `SetCurrentDatabase(string?)`
- Produces（Task 3 依賴）:
  - `ObjectTreeViewModel(ITableQueryService, IConnectionManager)` DI 建構函式（**移除**舊的單參數建構函式）
  - `ObservableCollection<DatabaseNodeViewModel> Databases`（TreeView 新綁定來源）
  - `IRelayCommand<DatabaseNodeViewModel?> SelectDatabaseCommand`
  - `Groups` 屬性保留，語意為「當前資料庫的四個物件群組」（`MainWindowViewModel.LoadObjectsAsync` 的統計沿用）
  - `DatabaseNodeViewModel`：`string Name`、`bool IsCurrent`、`string NameFontWeight`、`[ObservableProperty] bool IsExpanded`、`ObservableCollection<ObjectGroupViewModel> Groups`

- [ ] **Step 1: 寫失敗測試**

修改 `tests/Specurai.Desktop.Tests/ViewModels/ObjectTreeViewModelTests.cs`：

(a) 在建構函式中加入 `IConnectionManager` mock。將欄位與建構函式改為：

```csharp
    private readonly ITableQueryService _tableQueryService;
    private readonly IConnectionManager _connectionManager;

    public ObjectTreeViewModelTests()
    {
        _tableQueryService = Substitute.For<ITableQueryService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "TestDb" });
        _connectionManager.GetCurrentDatabase().Returns("TestDb");
    }
```

(b) 將檔案中所有 `new ObjectTreeViewModel(_tableQueryService)` 全數取代為 `new ObjectTreeViewModel(_tableQueryService, _connectionManager)`（共 11 處）。

(c) 更名既有測試 `Constructor_有TableQueryService_應建立四個群組` 為 `Constructor_有服務參數_應建立四個群組`（內容僅建構函式呼叫改為雙參數）。

(d) 在 `#region RefreshCommand 測試` 內新增：

```csharp
    [Fact]
    public async Task RefreshCommand_應載入資料庫節點清單()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2", "Db3" });
        _connectionManager.GetCurrentDatabase().Returns("Db2");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.Databases.Should().HaveCount(3);
        vm.Databases.Select(d => d.Name).Should().ContainInOrder("Db1", "Db2", "Db3");
    }

    [Fact]
    public async Task RefreshCommand_當前資料庫節點_應標示為Current且展開並掛載群組()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2" });
        _connectionManager.GetCurrentDatabase().Returns("Db2");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>
            {
                new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" }
            });

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        var current = vm.Databases.First(d => d.Name == "Db2");
        current.IsCurrent.Should().BeTrue();
        current.IsExpanded.Should().BeTrue();
        current.Groups.Should().HaveCount(4);
        current.Groups.First(g => g.Name == "Tables").Items.Should().HaveCount(1);

        var other = vm.Databases.First(d => d.Name == "Db1");
        other.IsCurrent.Should().BeFalse();
        other.IsExpanded.Should().BeFalse();
        other.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshCommand_當前資料庫不在清單中_應插入清單開頭()
    {
        // Arrange：連線設定檔預設資料庫可能不是使用者資料庫（例如 master）
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1" });
        _connectionManager.GetCurrentDatabase().Returns("master");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.Databases.Select(d => d.Name).Should().ContainInOrder("master", "Db1");
        vm.Databases.First(d => d.Name == "master").IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshCommand_資料庫列舉失敗_應degrade為僅顯示當前資料庫且物件照常載入()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("權限不足"));
        _connectionManager.GetCurrentDatabase().Returns("DefaultDb");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>
            {
                new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" }
            });

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.LastError.Should().BeNull();
        vm.Databases.Should().ContainSingle(d => d.Name == "DefaultDb" && d.IsCurrent);
        vm.Groups.First(g => g.Name == "Tables").Items.Should().HaveCount(1);
    }
```

(e) 檔案末尾（`ObjectItemViewModelTests` 類別之後）新增：

```csharp
/// <summary>
/// SelectDatabaseCommand 與 DatabaseNodeViewModel 測試
/// </summary>
public class ObjectTreeDatabaseSelectionTests
{
    private readonly ITableQueryService _tableQueryService = Substitute.For<ITableQueryService>();
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();

    public ObjectTreeDatabaseSelectionTests()
    {
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2" });
        _connectionManager.GetCurrentDatabase().Returns("Db1");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());
    }

    [Fact]
    public async Task SelectDatabaseCommand_點選非當前資料庫_應呼叫SetCurrentDatabase()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var other = vm.Databases.First(d => d.Name == "Db2");

        // Act
        vm.SelectDatabaseCommand.Execute(other);

        // Assert
        _connectionManager.Received(1).SetCurrentDatabase("Db2");
    }

    [Fact]
    public async Task SelectDatabaseCommand_點選當前資料庫_不應呼叫SetCurrentDatabase()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var current = vm.Databases.First(d => d.Name == "Db1");

        // Act
        vm.SelectDatabaseCommand.Execute(current);

        // Assert
        _connectionManager.DidNotReceive().SetCurrentDatabase(Arg.Any<string?>());
    }

    [Fact]
    public async Task 展開非當前資料庫節點_應觸發切換()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var other = vm.Databases.First(d => d.Name == "Db2");

        // Act：模擬使用者點選 TreeView 節點的展開箭頭
        other.IsExpanded = true;

        // Assert
        _connectionManager.Received(1).SetCurrentDatabase("Db2");
    }

    [Fact]
    public void DatabaseNodeViewModel_當前節點_字重應為Bold()
    {
        var node = new DatabaseNodeViewModel("Db1", isCurrent: true, groups: []);

        node.NameFontWeight.Should().Be("Bold");
        node.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void DatabaseNodeViewModel_非當前節點_字重應為Normal()
    {
        var node = new DatabaseNodeViewModel("Db1", isCurrent: false, groups: []);

        node.NameFontWeight.Should().Be("Normal");
        node.IsExpanded.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ObjectTree"
```

預期：**編譯失敗**（`ObjectTreeViewModel` 無雙參數建構函式、無 `Databases`、無 `DatabaseNodeViewModel`）。

- [ ] **Step 3: 實作 ObjectTreeViewModel**

修改 `src/Specurai.Desktop/ViewModels/ObjectTreeViewModel.cs`：

(a) 欄位、屬性與建構函式（取代原第 16 行欄位與第 32–49 行兩個建構函式；**刪除**舊的單參數建構函式）：

```csharp
    private readonly ITableQueryService? _tableQueryService;
    private readonly IConnectionManager? _connectionManager;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TableInfo? _selectedTable;

    [ObservableProperty]
    private ObjectGroupViewModel? _selectedGroup;

    /// <summary>
    /// 伺服器上的資料庫節點（TreeView 根層）
    /// </summary>
    public ObservableCollection<DatabaseNodeViewModel> Databases { get; } = [];

    /// <summary>
    /// 當前資料庫的四個物件群組（掛載於當前資料庫節點下）
    /// </summary>
    public ObservableCollection<ObjectGroupViewModel> Groups { get; } = [];

    public ObjectTreeViewModel()
    {
        // Design-time constructor
        AddDefaultGroups();
        Databases.Add(new DatabaseNodeViewModel("DesignDb", isCurrent: true, groups: Groups));
        Databases.Add(new DatabaseNodeViewModel("OtherDb", isCurrent: false, groups: []));
    }

    public ObjectTreeViewModel(ITableQueryService tableQueryService, IConnectionManager connectionManager)
    {
        _tableQueryService = tableQueryService;
        _connectionManager = connectionManager;
        AddDefaultGroups();
    }

    private void AddDefaultGroups()
    {
        Groups.Add(new ObjectGroupViewModel("Tables", "BASE TABLE"));
        Groups.Add(new ObjectGroupViewModel("Views", "VIEW"));
        Groups.Add(new ObjectGroupViewModel("Stored Procedures", "PROCEDURE"));
        Groups.Add(new ObjectGroupViewModel("Functions", "FUNCTION"));
    }
```

(b) `RefreshAsync` 整個方法取代：

```csharp
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_tableQueryService == null)
        {
            LastError = "TableQueryService 未初始化";
            return;
        }

        try
        {
            IsLoading = true;
            LastError = null;

            // 1. 載入伺服器資料庫清單；列舉失敗（權限不足/離線）時 degrade 為僅顯示當前資料庫
            IReadOnlyList<string> databaseNames;
            try
            {
                databaseNames = _connectionManager != null
                    ? await _connectionManager.GetDatabasesAsync()
                    : Array.Empty<string>();
            }
            catch
            {
                databaseNames = Array.Empty<string>();
            }

            var currentDatabase = _connectionManager?.GetCurrentDatabase();

            // 當前資料庫必須在清單中（列舉失敗或預設庫非使用者資料庫時插入開頭）
            var names = databaseNames.ToList();
            if (currentDatabase != null &&
                !names.Contains(currentDatabase, StringComparer.OrdinalIgnoreCase))
            {
                names.Insert(0, currentDatabase);
            }

            // 2. 重建資料庫節點；僅當前資料庫掛載共用群組並展開（單一展開原則）
            Databases.Clear();
            foreach (var name in names)
            {
                var isCurrent = string.Equals(name, currentDatabase, StringComparison.OrdinalIgnoreCase);
                var node = new DatabaseNodeViewModel(name, isCurrent, isCurrent ? Groups : []);
                node.PropertyChanged += (s, e) =>
                {
                    // 使用者以展開箭頭展開非當前資料庫時，等同點選切換
                    if (e.PropertyName == nameof(DatabaseNodeViewModel.IsExpanded) &&
                        s is DatabaseNodeViewModel n && n.IsExpanded && !n.IsCurrent)
                    {
                        SelectDatabase(n);
                    }
                };
                Databases.Add(node);
            }

            // 3. 載入當前資料庫的物件
            var allObjects = await _tableQueryService.GetAllTablesAsync();

            foreach (var group in Groups)
            {
                group.Items.Clear();
                var items = allObjects.Where(t => t.Type == group.ObjectType)
                    .OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
                foreach (var item in items)
                {
                    group.Items.Add(new ObjectItemViewModel(item));
                }
                group.UpdateCount();
            }

            FilterObjects();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
```

（順帶移除原方法內的 `System.Diagnostics.Debug.WriteLine` 除錯輸出。）

(c) 在 `SelectObject` 命令之後新增：

```csharp
    [RelayCommand]
    private void SelectDatabase(DatabaseNodeViewModel? node)
    {
        if (node == null || node.IsCurrent || _connectionManager == null)
            return;

        // 切換全域當前資料庫；後續載入由 CurrentDatabaseChanged 訂閱端（MainWindowViewModel）驅動
        _connectionManager.SetCurrentDatabase(node.Name);
    }
```

(d) 檔案末尾（`ObjectItemViewModel` 類別之後）新增：

```csharp
/// <summary>
/// 資料庫節點 ViewModel（TreeView 根層，SSMS 式資料庫瀏覽）
/// </summary>
public partial class DatabaseNodeViewModel : ViewModelBase
{
    public string Name { get; }

    /// <summary>
    /// 是否為目前使用中的資料庫（節點於每次重建時決定，不需通知變更）
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>
    /// 節點名稱字重（當前資料庫以粗體標示）
    /// </summary>
    public string NameFontWeight => IsCurrent ? "Bold" : "Normal";

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<ObjectGroupViewModel> Groups { get; }

    public DatabaseNodeViewModel(string name, bool isCurrent, ObservableCollection<ObjectGroupViewModel> groups)
    {
        Name = name;
        IsCurrent = isCurrent;
        _isExpanded = isCurrent;
        Groups = groups;
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ObjectTree"
```

預期：全數 PASS（既有 ~20 個 + 新增 9 個）。注意：此時 Desktop 專案本體尚未改 DI 與 AXAML，`dotnet build` 會因 `Program.cs` 舊的單參數呼叫失敗——Task 3 修復；先只跑上述 filter 測試需 Desktop 可編譯，因此 **Step 3 同時要把 `src/Specurai.Desktop/Program.cs` 的註冊改掉**（見 Task 3 Step 3 (a)，可提前到本步驟執行）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/ObjectTreeViewModel.cs src/Specurai.Desktop/Program.cs tests/Specurai.Desktop.Tests/ViewModels/ObjectTreeViewModelTests.cs
git commit -m "feat: 物件樹新增資料庫節點層（SSMS 式瀏覽）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Desktop UI 整合（AXAML、code-behind、MainWindowViewModel、分頁鍵）

**Files:**
- Modify: `src/Specurai.Desktop/Program.cs`（ObjectTreeViewModel DI 註冊）
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml`（TreeView 綁定與模板）
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml.cs`（選取事件處理資料庫節點）
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`（訂閱事件、當前庫顯示、分頁鍵）
- Modify: `src/Specurai.Desktop/ViewModels/TableDetailDocumentViewModel.cs`（DatabaseName 與 DocumentKey）
- Test: `tests/Specurai.Desktop.Tests/ViewModels/TableDetailDocumentViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `Databases`、`SelectDatabaseCommand`、`DatabaseNodeViewModel`；Task 1 的 `GetCurrentDatabase()`、`CurrentDatabaseChanged`
- Produces: `TableDetailDocumentViewModel(ITableQueryService, TableInfo, string? databaseName)` 建構函式；`DocumentKey` 格式 `TableDetail:{Database}.{Schema}.{Name}`

- [ ] **Step 1: 寫失敗測試（分頁鍵含資料庫名）**

在 `tests/Specurai.Desktop.Tests/ViewModels/TableDetailDocumentViewModelTests.cs` 新增（沿用該檔既有的 using 與 mock 風格）：

```csharp
    [Fact]
    public void DocumentKey_有DatabaseName_應包含資料庫名稱()
    {
        // Arrange
        var service = Substitute.For<ITableQueryService>();
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };

        // Act
        var vm = new TableDetailDocumentViewModel(service, table, "MyDb");

        // Assert
        vm.DocumentKey.Should().Be("TableDetail:MyDb.dbo.Users");
        vm.Title.Should().Be("Users (MyDb)");
    }

    [Fact]
    public void DocumentKey_無DatabaseName_應維持原格式()
    {
        // Arrange
        var service = Substitute.For<ITableQueryService>();
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };

        // Act
        var vm = new TableDetailDocumentViewModel(service, table);

        // Assert
        vm.DocumentKey.Should().Be("TableDetail:dbo.Users");
        vm.Title.Should().Be("Users");
    }
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~TableDetailDocumentViewModelTests"
```

預期：**編譯失敗**（無三參數建構函式）。

- [ ] **Step 3: 實作**

(a) `src/Specurai.Desktop/Program.cs`——`services.AddTransient<ObjectTreeViewModel>();`（第 94 行）改為：

```csharp
        services.AddTransient<ObjectTreeViewModel>(sp =>
            new ObjectTreeViewModel(
                sp.GetRequiredService<ITableQueryService>(),
                sp.GetRequiredService<IConnectionManager>()));
```

（若 Task 2 已改，跳過。）

(b) `src/Specurai.Desktop/ViewModels/TableDetailDocumentViewModel.cs`：

新增屬性（放在 `DocumentType` 之前）：

```csharp
    /// <summary>
    /// 此分頁綁定的資料庫名稱（開啟當下的當前資料庫，用於分頁鍵與標題）
    /// </summary>
    public string? DatabaseName { get; }
```

`DocumentKey` 覆寫（第 65–67 行）取代為：

```csharp
    public override string DocumentKey => CurrentTable != null
        ? DatabaseName != null
            ? $"{DocumentType}:{DatabaseName}.{CurrentTable.Schema}.{CurrentTable.Name}"
            : $"{DocumentType}:{CurrentTable.Schema}.{CurrentTable.Name}"
        : base.DocumentKey;
```

DI 建構函式（第 76 行起）簽章與 Title 改為：

```csharp
    public TableDetailDocumentViewModel(ITableQueryService tableQueryService, TableInfo table, string? databaseName = null)
    {
        _tableQueryService = tableQueryService;
        CurrentTable = table;
        DatabaseName = databaseName;
        Title = databaseName != null ? $"{table.Name} ({databaseName})" : table.Name;
        Icon = GetIconForType(table.Type);
        CanClose = true;
```

（其餘內容不變。）

(c) `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`：

建構函式中 `_connectionManager.CurrentProfileChanged += OnCurrentProfileChanged;`（第 130 行）之後加：

```csharp
        // 訂閱資料庫切換事件（側邊欄點選資料庫節點時觸發）
        _connectionManager.CurrentDatabaseChanged += OnCurrentDatabaseChanged;
```

`OnCurrentProfileChanged` 方法（第 230–234 行）之後新增：

```csharp
    private async void OnCurrentDatabaseChanged(object? sender, string? databaseName)
    {
        StatusMessage = $"已切換至資料庫 {databaseName}";
        await LoadObjectsAsync();
    }
```

`CurrentEnvironmentDatabase` 屬性（第 103–104 行）改為：

```csharp
    public string? CurrentEnvironmentDatabase =>
        _connectionManager?.GetCurrentDatabase();
```

`OnTableSelected`（第 239–261 行）中的分頁鍵與建構呼叫改為：

```csharp
    private void OnTableSelected(TableInfo? table)
    {
        if (table == null || _tableQueryService == null) return;

        var databaseName = _connectionManager?.GetCurrentDatabase();
        var tableKey = databaseName != null
            ? $"TableDetail:{databaseName}.{table.Schema}.{table.Name}"
            : $"TableDetail:{table.Schema}.{table.Name}";

        // 檢查是否已開啟
        var existing = Documents.OfType<TableDetailDocumentViewModel>()
            .FirstOrDefault(d => d.DocumentKey == tableKey);

        if (existing != null)
        {
            SelectedDocument = existing;
        }
        else
        {
            var doc = new TableDetailDocumentViewModel(_tableQueryService, table, databaseName);
            doc.ConfirmSaveCallback = ConfirmSaveCallback;
            doc.CloseRequested += OnDocumentCloseRequested;
            Documents.Add(doc);
            SelectedDocument = doc;
        }
    }
```

`ExportToExcelAsync` 中 `var databaseName = SelectedProfile?.Database ?? "Specurai";`（第 291 行）改為：

```csharp
                var databaseName = _connectionManager?.GetCurrentDatabase() ?? "Specurai";
```

(d) `src/Specurai.Desktop/Views/MainWindow.axaml`：

TreeView 綁定來源（第 301 行）：

```xml
                              ItemsSource="{Binding ObjectTree.Databases}" Margin="5"
```

`<TreeView.DataTemplates>` 開頭（群組模板之前）插入資料庫模板：

```xml
                            <!-- 資料庫節點模板 -->
                            <TreeDataTemplate DataType="{x:Type vm:DatabaseNodeViewModel}"
                                              ItemsSource="{Binding Groups}">
                                <StackPanel Orientation="Horizontal" Spacing="5">
                                    <TextBlock Text="🗄"/>
                                    <TextBlock Text="{Binding Name}"
                                               FontWeight="{Binding NameFontWeight}"/>
                                </StackPanel>
                            </TreeDataTemplate>
```

`<TreeView.Styles>` 中的展開樣式（第 320–322 行）由固定值改為綁定（資料庫節點依 `IsExpanded` 控制、群組節點沿用其預設 `IsExpanded = true`、葉節點無此屬性綁定靜默失敗無影響）：

```xml
                            <Style Selector="TreeViewItem">
                                <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"/>
                            </Style>
```

(e) `src/Specurai.Desktop/Views/MainWindow.axaml.cs`——`OnTreeViewSelectionChanged`（第 49–64 行）在既有 `ObjectItemViewModel` 分支之前插入資料庫節點分支：

```csharp
    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNextSelectionChanged)
        {
            _suppressNextSelectionChanged = false;
            return;
        }

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DatabaseNodeViewModel dbNode)
        {
            if (DataContext is MainWindowViewModel vmDb)
            {
                vmDb.ObjectTree?.SelectDatabaseCommand.Execute(dbNode);
            }
            return;
        }

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ObjectItemViewModel item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ObjectTree?.SelectObjectCommand.Execute(item);
            }
        }
    }
```

- [ ] **Step 4: 執行測試與建置確認通過**

```bash
dotnet build
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj
```

預期：建置成功、Desktop 測試全數 PASS。

- [ ] **Step 5: 手動驗證（桌面程式）**

```bash
dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj
```

檢查清單（對照 SSMS）：

1. 連線後側邊欄顯示該伺服器所有使用者資料庫，設定檔預設資料庫粗體且自動展開，其下四組物件數量正確
2. 點選另一資料庫節點 → 原節點收合、新節點展開並載入物件，狀態列顯示「已切換至資料庫 …」
3. 展開箭頭展開另一資料庫 → 同樣觸發切換
4. 跨庫開啟同名資料表 → 兩個分頁並存，標題各帶資料庫名
5. 切換資料庫後開 SQL 查詢分頁執行 `SELECT DB_NAME()` → 回傳當前資料庫
6. 匯出 Excel → 建議檔名為當前資料庫名
7. 「搜尋物件」框過濾當前資料庫物件正常
8. 切回連線下拉選另一連線 → 覆寫重設，樹狀圖顯示新伺服器的資料庫清單
9. Production 環境連線的警告橫幅顯示當前資料庫名

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop tests/Specurai.Desktop.Tests
git commit -m "feat: 側邊欄整合資料庫節點切換與跨庫分頁鍵

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: MCP list_databases / switch_database 工具

**Files:**
- Create: `src/Specurai.McpServer/Tools/DatabaseTools.cs`
- Modify: `src/Specurai.McpServer/Tools/ConnectionTools.cs`（list_connections 加註當前資料庫）

**Interfaces:**
- Consumes: Task 1 的 `IConnectionManager.GetDatabasesAsync()` / `GetCurrentDatabase()` / `SetCurrentDatabase(string?)`
- Produces: MCP 工具 `list_databases`、`switch_database`（`[McpServerToolType]` 靜態類別，`WithToolsFromAssembly()` 自動註冊，無需改 `Program.cs`）

（無 McpServer 測試專案——依既有慣例 MCP 工具為薄包裝不做單元測試，核心邏輯已由 Task 1 的 ConnectionManagerTests 覆蓋；以 Step 3 手動驗證。）

- [ ] **Step 1: 建立 DatabaseTools**

建立 `src/Specurai.McpServer/Tools/DatabaseTools.cs`：

```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫瀏覽 MCP 工具（SSMS 式：一個連線可瀏覽伺服器上所有使用者資料庫）
/// </summary>
[McpServerToolType]
public static class DatabaseTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 列出目前連線伺服器上的使用者資料庫
    /// </summary>
    [McpServerTool, Description("列出目前連線伺服器上的所有使用者資料庫，並標示目前使用中的資料庫與連線設定檔預設資料庫")]
    public static async Task<string> ListDatabases(IConnectionManager connectionManager)
    {
        var profile = connectionManager.GetCurrentProfile();
        if (profile == null)
            return "目前沒有選擇任何連線設定。請先使用 switch_connection 選擇連線。";

        try
        {
            var databases = await connectionManager.GetDatabasesAsync();
            var current = connectionManager.GetCurrentDatabase();

            var result = databases.Select(name => new
            {
                Name = name,
                IsCurrent = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
                IsProfileDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase)
            });

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"無法列舉資料庫（{profile.Server}）：{ex.Message}";
        }
    }

    /// <summary>
    /// 切換目前使用的資料庫
    /// </summary>
    [McpServerTool, Description("切換目前使用的資料庫（僅影響本次工作階段，不變更連線設定檔；使用 switch_connection 可重設回設定檔預設資料庫）")]
    public static async Task<string> SwitchDatabase(
        IConnectionManager connectionManager,
        [Description("資料庫名稱")] string databaseName)
    {
        var profile = connectionManager.GetCurrentProfile();
        if (profile == null)
            return "目前沒有選擇任何連線設定。請先使用 switch_connection 選擇連線。";

        IReadOnlyList<string> databases;
        try
        {
            databases = await connectionManager.GetDatabasesAsync();
        }
        catch (Exception ex)
        {
            return $"無法列舉資料庫（{profile.Server}）：{ex.Message}";
        }

        var target = databases.FirstOrDefault(d =>
            d.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return $"伺服器 {profile.Server} 上找不到使用者資料庫「{databaseName}」。可用資料庫：{string.Join("、", databases)}";

        connectionManager.SetCurrentDatabase(target);
        return $"已切換至資料庫「{target}」（{profile.Server}）";
    }
}
```

- [ ] **Step 2: ConnectionTools.ListConnections 加註當前資料庫**

`src/Specurai.McpServer/Tools/ConnectionTools.cs` 的 `ListConnections` 匿名物件（第 28–37 行）改為：

```csharp
        var result = profiles.Select(p => new
        {
            p.Id,
            p.Name,
            p.Server,
            p.Database,
            AuthType = p.AuthType.ToString(),
            IsCurrent = current?.Id == p.Id,
            // 目前使用中的資料庫（可能因 switch_database 而異於設定檔預設資料庫）
            CurrentDatabase = current?.Id == p.Id ? connectionManager.GetCurrentDatabase() : null,
            p.IsDefault
        });
```

- [ ] **Step 3: 建置與手動驗證**

```bash
dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj
```

預期：建置成功。手動驗證（發佈前先關閉執行中的 McpServer/Desktop 行程）：透過 MCP 客戶端依序呼叫 `list_databases`（列出使用者資料庫、標示當前）→ `switch_database` 切到另一庫 → `list_tables` 回傳該庫物件 → `switch_database` 傳不存在庫名 → 回傳友善錯誤含可用清單 → `switch_connection` → `list_databases` 確認覆寫已重設。

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.McpServer/Tools/DatabaseTools.cs src/Specurai.McpServer/Tools/ConnectionTools.cs
git commit -m "feat: MCP 新增 list_databases 與 switch_database 工具

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: CLI databases 命令

**Files:**
- Create: `src/Specurai.Cli/Commands/DatabasesCommand.cs`
- Modify: `src/Specurai.Cli/Program.cs`（註冊命令）
- Test (Create): `tests/Specurai.Cli.Tests/DatabasesCommandParseTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `IConnectionManager.GetDatabasesAsync(ConnectionProfile, CancellationToken)`；既有 `ConnectionResolver.Resolve(GlobalOptions)`（`--server`/`--database`/`--profile`/`--conn-stdin` 均沿用既有全域選項機制）
- Produces: `specurai databases` 命令（支援 `--json`）

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Cli.Tests/DatabasesCommandParseTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Cli.Commands;

namespace Specurai.Cli.Tests;

public class DatabasesCommandParseTests
{
    [Fact(DisplayName = "Create: 命令名稱應為 databases")]
    public void Create_ShouldBeNamedDatabases()
    {
        var command = DatabasesCommand.Create();

        command.Name.Should().Be("databases");
        command.Description.Should().Contain("使用者資料庫");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~DatabasesCommandParseTests"
```

預期：**編譯失敗**（`DatabasesCommand` 不存在）。

- [ ] **Step 3: 實作 DatabasesCommand**

建立 `src/Specurai.Cli/Commands/DatabasesCommand.cs`：

```csharp
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 資料庫清單命令（SSMS 式：列出連線伺服器上的所有使用者資料庫）
/// </summary>
public static class DatabasesCommand
{
    public static Command Create()
    {
        var command = new Command("databases", "列出伺服器上的所有使用者資料庫");

        command.SetHandler(async () =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null)
            {
                CliOutput.Error("找不到連線設定。請使用 --server 或 --profile 指定連線。");
                Environment.ExitCode = 1;
                return;
            }

            IReadOnlyList<string> databases;
            try
            {
                databases = await cm.GetDatabasesAsync(profile);
            }
            catch (Exception ex)
            {
                CliOutput.Error($"無法列舉資料庫（{profile.Server}）：{ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (CliOutput.JsonMode)
            {
                var data = databases.Select(name => new
                {
                    Name = name,
                    IsProfileDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase)
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (databases.Count == 0)
                {
                    CliOutput.Info("伺服器上沒有使用者資料庫。");
                    return;
                }

                var table = new Table().Title($"[bold]{profile.Server}[/] 使用者資料庫");
                table.AddColumn("資料庫");
                table.AddColumn("預設");

                foreach (var name in databases)
                {
                    var isDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase);
                    table.AddRow(name.EscapeMarkup(), isDefault ? "[green]✓[/]" : "");
                }

                AnsiConsole.Write(table);
                CliOutput.Info($"共 {databases.Count} 個資料庫");
            }
        });

        return command;
    }
}
```

- [ ] **Step 4: 註冊命令**

`src/Specurai.Cli/Program.cs` 的 `rootCommand.AddCommand(TablesCommand.Create());`（第 59 行）之後插入：

```csharp
        rootCommand.AddCommand(DatabasesCommand.Create());
```

- [ ] **Step 5: 執行測試確認通過 + 手動驗證**

```bash
dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj
dotnet run --project src/Specurai.Cli -- databases
dotnet run --project src/Specurai.Cli -- databases --json
```

預期：測試 PASS；`databases` 以表格列出預設連線伺服器的使用者資料庫並以 ✓ 標示設定檔預設資料庫；`--json` 輸出 JSON。另驗證一次性資料庫指定沿用既有機制：`dotnet run --project src/Specurai.Cli -- tables list --server <主機> --database <另一庫>`。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Cli/Commands/DatabasesCommand.cs src/Specurai.Cli/Program.cs tests/Specurai.Cli.Tests/DatabasesCommandParseTests.cs
git commit -m "feat: CLI 新增 databases 命令列出伺服器使用者資料庫

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: 全方案驗證與收尾

**Files:**
- Test: 全部測試專案
- Modify（如適用）: `README.md`（若其中列有 MCP 工具/CLI 命令清單則補上 `list_databases`/`switch_database`/`databases`；若無此清單則不動）

- [ ] **Step 1: 全方案建置與全部測試**

```bash
dotnet build
dotnet test
```

預期：0 錯誤；全部測試 PASS（既有 604+ 與本計劃新增皆綠）。

- [ ] **Step 2: 檢查 README 是否需補文件**

```bash
grep -n "switch_connection\|tables list" README.md
```

若 README 列有 MCP 工具或 CLI 命令清單，依既有格式補上三個新入口（繁體中文描述）；若無則跳過。

- [ ] **Step 3: 完整手動驗證**

執行 Task 3 Step 5 的 9 項檢查清單（若當時已全數通過且後續 Task 未再動 Desktop，可抽驗 1、2、4、8 四項）。

- [ ] **Step 4: Commit（若有文件變更）**

```bash
git add README.md
git commit -m "docs: 補充資料庫瀏覽相關 MCP 工具與 CLI 命令說明

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: 程式碼審查**

依專案憲法，使用 `superpowers:requesting-code-review` 技能對本次全部變更進行審查後再回報完成。
