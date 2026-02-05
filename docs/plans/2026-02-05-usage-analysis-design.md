# 使用狀態分析（Usage Analysis）設計文件

日期：2026-02-05

## 功能概覽

新增「使用狀態分析」功能，檢查資料表及欄位是否有在使用。包含兩種模式：

1. **單環境分析**：掃描單一資料庫，分為資料表維度與欄位維度
2. **多環境比對**：矩陣式總覽各環境的使用狀態差異，搭配明細面板

支援直接刪除未使用的資料表與欄位（預覽 SQL + 確認對話框）。

---

## 判斷邏輯

### 資料表層級「未使用」

兩個條件皆須滿足：

- `sys.dm_db_index_usage_stats` 中 `user_seeks + user_scans + user_lookups = 0`（無查詢活動）
- `last_user_update` 早於 N 年前，或完全無記錄（無資料異動）
- N 由使用者自訂（預設 2 年，範圍 1-10）

### 欄位層級「未使用」

三個條件皆須滿足：

- 未出現在 `sys.dm_exec_query_stats` 快取的查詢計畫 XML 中
- 該欄位全部為 NULL **或** 所有非 NULL 值皆等於欄位的 DEFAULT 約束值
- 排除 PK / FK / Identity 欄位（即使符合條件也不標記為未使用）

---

## UI 結構

### 頂部操作列

- 模式切換：`單環境分析` / `多環境比對`（ToggleButton）
- 單環境模式：顯示當前連線名稱
- 多環境模式：基準環境下拉選單 + 目標環境勾選清單
- 時間區間：「近 ___ 年未使用」數字輸入框（預設 2，最小 1，最大 10）
- 「開始掃描」按鈕 + 進度條

### 單環境模式 — 結果區

- 維度切換標籤：`資料表維度` / `欄位維度`
- **資料表維度**：DataGrid（表名、Schema、最後查詢時間、最後更新時間、狀態標籤）
- **欄位維度**：DataGrid（表名、欄位名、是否被查詢引用、是否全 NULL、是否全為預設值、狀態標籤）
- 篩選列：搜尋框 + 狀態篩選（全部 / 僅未使用 / 僅使用中）

### 多環境比對模式 — 結果區

- 上方：矩陣總覽 DataGrid（縱軸為表或欄位，橫軸為各環境，儲存格顯示狀態圖示）
- 下方：選中項目的詳細數據面板
- 維度切換標籤同上

### 刪除操作

- 每列末端有「刪除」按鈕，僅在「未使用」項目上啟用
- 支援勾選多筆未使用項目，批次刪除
- 點擊刪除後彈出確認對話框，顯示即將執行的 SQL 語法 + 警告文字
- 確認後執行，完成後重新整理清單

---

## Clean Architecture 分層設計

### Domain 層 — 實體

#### `TableUsageInfo.cs`

```csharp
public class TableUsageInfo
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public long UserSeeks { get; set; }
    public long UserScans { get; set; }
    public long UserLookups { get; set; }
    public long UserUpdates { get; set; }
    public DateTime? LastUserSeek { get; set; }
    public DateTime? LastUserScan { get; set; }
    public DateTime? LastUserUpdate { get; set; }
    public bool IsUsed { get; set; }
}
```

#### `ColumnUsageStatus.cs`

```csharp
public class ColumnUsageStatus
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public string DataType { get; set; }
    public bool IsReferencedInQueries { get; set; }
    public bool IsAllNull { get; set; }
    public bool IsAllDefault { get; set; }
    public string DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsUsed { get; set; }
}
```

#### `UsageComparison.cs`

```csharp
public class UsageComparison
{
    public string BaseEnvironment { get; set; }
    public List<string> TargetEnvironments { get; set; }
    public List<TableUsageComparisonRow> TableRows { get; set; }
    public List<ColumnUsageComparisonRow> ColumnRows { get; set; }
}

public class TableUsageComparisonRow
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public Dictionary<string, TableUsageInfo> EnvironmentStatus { get; set; }
    // key = 環境名稱, value = 該環境的使用狀態（null = 表不存在）
}

public class ColumnUsageComparisonRow
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public Dictionary<string, ColumnUsageStatus> EnvironmentStatus { get; set; }
}
```

### Domain 層 — 介面

#### `IUsageAnalysisRepository.cs`

```csharp
public interface IUsageAnalysisRepository
{
    Task<IReadOnlyList<TableUsageInfo>> GetTableUsageAsync(
        int years, CancellationToken ct = default);
    Task<IReadOnlyList<ColumnUsageStatus>> GetColumnUsageStatusAsync(
        IProgress<string> progress, CancellationToken ct = default);
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);
}
```

### Application 層 — 服務

#### `IUsageAnalysisService.cs` / `UsageAnalysisService.cs`

```csharp
public interface IUsageAnalysisService
{
    Task<UsageScanResult> ScanAsync(
        int years, IProgress<string> progress, CancellationToken ct);
    Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string> progress, CancellationToken ct);
    Task DeleteTableAsync(string schemaName, string tableName, CancellationToken ct);
    Task DeleteColumnAsync(string schemaName, string tableName, string columnName, CancellationToken ct);
    string GenerateDropTableSql(string schemaName, string tableName);
    string GenerateDropColumnSql(string schemaName, string tableName, string columnName);
}
```

### Infrastructure 層 — Repository 實作

`UsageAnalysisRepository.cs` — DMV 查詢 + 動態 SQL 欄位掃描。

---

## 核心 SQL 查詢

### 表層級查詢

```sql
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    ISNULL(SUM(ius.user_seeks), 0) AS UserSeeks,
    ISNULL(SUM(ius.user_scans), 0) AS UserScans,
    ISNULL(SUM(ius.user_lookups), 0) AS UserLookups,
    ISNULL(SUM(ius.user_updates), 0) AS UserUpdates,
    MAX(ius.last_user_seek) AS LastUserSeek,
    MAX(ius.last_user_scan) AS LastUserScan,
    MAX(ius.last_user_update) AS LastUserUpdate
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.dm_db_index_usage_stats ius
    ON ius.object_id = t.object_id
    AND ius.database_id = DB_ID()
GROUP BY s.name, t.name
```

### 欄位查詢計畫引用檢查

```sql
SELECT DISTINCT
    OBJECT_SCHEMA_NAME(st.objectid) AS SchemaName,
    OBJECT_NAME(st.objectid) AS TableName,
    c.value('(@Column)[1]', 'NVARCHAR(128)') AS ColumnName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
CROSS APPLY qp.query_plan.nodes('//ColumnReference') AS ref(c)
WHERE st.objectid IS NOT NULL
```

### 欄位 NULL / 預設值檢查（動態產生，逐表掃描）

```sql
-- 範例：對 dbo.Orders 表
SELECT
    COUNT(*) AS TotalRows,
    SUM(CASE WHEN [Status] IS NULL THEN 1 ELSE 0 END) AS Status_NullCount,
    SUM(CASE WHEN [Status] = 1 THEN 1 ELSE 0 END) AS Status_DefaultCount,
    SUM(CASE WHEN [Remark] IS NULL THEN 1 ELSE 0 END) AS Remark_NullCount,
    SUM(CASE WHEN [Remark] = '' THEN 1 ELSE 0 END) AS Remark_DefaultCount
FROM [dbo].[Orders]
```

預設值從 `sys.default_constraints` 取得。

---

## 新增/修改檔案清單

### 新增檔案

| 層級 | 檔案路徑 | 用途 |
|------|----------|------|
| Domain | `Entities/TableUsageInfo.cs` | 資料表使用狀態實體 |
| Domain | `Entities/ColumnUsageStatus.cs` | 欄位使用狀態實體 |
| Domain | `Entities/UsageComparison.cs` | 多環境比對結果 |
| Domain | `Interfaces/IUsageAnalysisRepository.cs` | Repository 介面 |
| Application | `Services/IUsageAnalysisService.cs` | 服務介面 |
| Application | `Services/UsageAnalysisService.cs` | 服務實作 |
| Infrastructure | `Repositories/UsageAnalysisRepository.cs` | DMV 查詢 + 欄位掃描 |
| Desktop | `ViewModels/UsageAnalysisDocumentViewModel.cs` | ViewModel |
| Desktop | `Views/UsageAnalysisDocumentView.axaml` | UI 頁面 |
| Desktop | `Views/UsageAnalysisDocumentView.axaml.cs` | Code-behind |

### 修改檔案

| 檔案 | 修改內容 |
|------|----------|
| `Desktop/Program.cs` | DI 註冊 |
| `Desktop/ViewModels/MainWindowViewModel.cs` | 新增選單命令 |
| `Desktop/Views/MainWindow.axaml` | 新增選單入口 |

### DI 註冊

```csharp
// Repository
services.AddSingleton<IUsageAnalysisRepository>(sp =>
    new UsageAnalysisRepository(() =>
        sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

// Service
services.AddSingleton<IUsageAnalysisService>(sp =>
    new UsageAnalysisService(
        sp.GetRequiredService<IUsageAnalysisRepository>()));

// ViewModel
services.AddTransient<UsageAnalysisDocumentViewModel>();
```

多環境比對時，Service 透過 `IConnectionManager.GetConnectionString(profileId)` 取得各環境連線字串，動態建立 Repository 查詢。

---

## ViewModel 設計

### `UsageAnalysisDocumentViewModel`

```csharp
// 模式切換
[ObservableProperty] private bool _isCompareMode;

// 操作參數
[ObservableProperty] private int _yearsThreshold = 2;
[ObservableProperty] private ConnectionProfile _selectedBaseProfile;
[ObservableProperty] private bool _isScanning;
[ObservableProperty] private string _progressMessage;
[ObservableProperty] private int _progressPercent;

// 維度切換
[ObservableProperty] private bool _isColumnDimension;

// 篩選
[ObservableProperty] private string _searchText;
[ObservableProperty] private int _statusFilter;

// 單環境結果
[ObservableProperty] private ObservableCollection<TableUsageInfo> _tableResults;
[ObservableProperty] private ObservableCollection<ColumnUsageStatus> _columnResults;

// 多環境結果
[ObservableProperty] private ObservableCollection<TableUsageComparisonRow> _comparisonTableRows;
[ObservableProperty] private ObservableCollection<ColumnUsageComparisonRow> _comparisonColumnRows;

// 命令
[RelayCommand] private async Task ScanAsync();
[RelayCommand] private void CancelScan();
[RelayCommand] private async Task DeleteTableAsync(TableUsageInfo table);
[RelayCommand] private async Task DeleteColumnAsync(ColumnUsageStatus column);
[RelayCommand] private async Task BatchDeleteAsync();
```
