# 效能診斷 — 完整性檢查分頁設計

- 日期：2026-05-15
- 作者：Kerry Huang
- 範圍：在「效能診斷」既有 `TabControl` 新增「完整性檢查」分頁，提供三段資料庫完整性的健康快照。

## 背景與動機

剛完成的維護計劃可建立每週 `DBCC CHECKDB` 排程（Job），但 DBA 仍需要一個**集中視圖**回答這些問題：

1. 我所有的資料庫**最後一次成功 CHECKDB 是什麼時候**？有沒有從未檢查過的？
2. **目前有沒有疑似損毀**的頁面（`msdb.dbo.suspect_pages`）？
3. CHECKDB Job 的**最近執行歷史**是什麼？有沒有失敗？

目前要分別到 SSMS error log、msdb 系統表、各 DB 跑 `DBCC DBINFO` 才能組出這份視圖。本功能整合到效能診斷分頁，一鍵載入。

## 設計位置

`Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml` 的 `TabControl` 內，**插在「錯誤記錄」之後**新增一個 `TabItem`：

```
等候事件 / 耗時查詢 / 索引分析 / 缺少索引 / 統計資訊 / 錯誤記錄 / 【完整性檢查】
```

選擇此分頁時不自動載入（與既有 tab 一致），由使用者按「重新整理」觸發。

## 三個區塊（同一分頁內，垂直排列）

### 區塊 1：各資料庫最後 CHECKDB 時間

| 欄位 | 來源 |
|------|------|
| Database | `sys.databases.name`（排除 `tempdb`） |
| LastKnownGood | `DBCC DBINFO WITH TABLERESULTS, NO_INFOMSGS` 解析 `dbi_dbccLastKnownGood` |
| 距今天數 | C# 計算 |
| 健康狀態 | <14 天 🟢 / 14–30 天 🟡 / >30 天或從未 🔴 |

實作備註：`DBCC DBINFO` 必須對「目標 DB」執行，需在 SQL 中 `USE [db]; DBCC DBINFO ...` 並用 INSERT INTO #temp 收集後 SELECT。對所有 user DB 迴圈一次。

### 區塊 2：Suspect Pages（疑似損毀頁面）

| 欄位 | 來源 |
|------|------|
| Database | `DB_NAME(database_id)` |
| FileId | `file_id` |
| PageId | `page_id` |
| 事件類型 | `event_type` 解碼為文字（1=824、2=不正常 shutdown、3=校驗失敗、4=還原成功、5=Repaired、7=Deallocated） |
| 錯誤次數 | `error_count` |
| 最後更新 | `last_update_date` |

SQL：`SELECT * FROM msdb.dbo.suspect_pages`。

**正常情況此區塊為空**，UI 顯示「目前無疑似損毀頁面」綠字提示。

### 區塊 3：CHECKDB Job 執行紀錄

| 欄位 | 來源 |
|------|------|
| Job Name | `sysjobs.name` 篩選 LIKE `%CheckDb%` |
| 執行時間 | `sysjobhistory.run_date` + `run_time`（合併解析） |
| 時長 | `run_duration`（HHMMSS 格式轉「mm:ss」） |
| 結果 | `run_status`（1=成功 / 0=失敗 / 3=取消 / 4=重試） |
| 訊息 | `message`（截斷顯示，可點擊看完整） |

只取 `step_id = 0`（Job 整體結果）以避免 step-level 噪音。最近 50 筆。

## Domain / Application 變更

### Domain

新增三個 Entity：

```csharp
// Specurai.Domain/Entities/IntegrityCheckStatus.cs
public class IntegrityCheckStatus
{
    public required string DatabaseName { get; init; }
    public DateTime? LastKnownGood { get; init; }   // null = 從未檢查
    public int? DaysSince { get; init; }
    public IntegrityHealth Health { get; init; }
}
public enum IntegrityHealth { Healthy, Warning, Critical, Unknown }

// Specurai.Domain/Entities/SuspectPage.cs
public class SuspectPage
{
    public required string DatabaseName { get; init; }
    public required int FileId { get; init; }
    public required long PageId { get; init; }
    public required int EventTypeRaw { get; init; }
    public string EventTypeText => EventTypeRaw switch { ... };
    public required int ErrorCount { get; init; }
    public required DateTime LastUpdateDate { get; init; }
}

// Specurai.Domain/Entities/CheckDbJobHistory.cs
public class CheckDbJobHistory
{
    public required string JobName { get; init; }
    public required DateTime RunAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int RunStatus { get; init; }
    public string StatusText => RunStatus switch { 1 => "成功", 0 => "失敗", 3 => "取消", 4 => "重試", _ => "其他" };
    public required string Message { get; init; }
}
```

### Repository — 擴充 `IPerformanceDiagnosticsRepository`

新增三個方法：

```csharp
Task<IReadOnlyList<IntegrityCheckStatus>> GetLastCheckDbAsync(CancellationToken ct = default);
Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default);
Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default);
```

Infrastructure 實作：
- `GetLastCheckDbAsync`：查 `sys.databases` 排除 system DBs（除了 master/model/msdb 視情況），對每個 DB 執行 `DBCC DBINFO`。為效能與權限友善：用單一動態 SQL 串接 `INSERT INTO #t EXEC('USE [...]; DBCC DBINFO ...')`，最後 SELECT。
- `GetSuspectPagesAsync`：直接 `SELECT FROM msdb.dbo.suspect_pages`，含 JOIN 取 db name。
- `GetCheckDbJobHistoryAsync`：`msdb.dbo.sysjobhistory` JOIN `sysjobs` WHERE name LIKE `%CheckDb%` AND step_id = 0 ORDER BY run_date DESC, run_time DESC TOP @n。

### Application — 擴充 `IPerformanceDiagnosticsService`

加三個對應的 `Async` 方法，純轉發 + Health 判斷邏輯：

```csharp
Task<IReadOnlyList<IntegrityCheckStatus>> GetIntegrityCheckStatusAsync(CancellationToken ct = default);
Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default);
Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default);
```

Health 判斷在 Service 層計算（純函式）：

```csharp
private static IntegrityHealth ClassifyHealth(int? days) => days switch
{
    null => IntegrityHealth.Critical,        // 從未檢查
    < 14 => IntegrityHealth.Healthy,
    < 30 => IntegrityHealth.Warning,
    _ => IntegrityHealth.Critical
};
```

## ViewModel 變更

`PerformanceDiagnosticsDocumentViewModel` 新增：

- `ObservableCollection<IntegrityCheckStatus> IntegrityChecks`
- `ObservableCollection<SuspectPage> SuspectPages`
- `ObservableCollection<CheckDbJobHistory> CheckDbJobHistories`
- `bool IsLoadingIntegrity`
- `RelayCommand RunIntegrityCheckAnalysisCommand` — 同時觸發三個 query
- `bool HasSuspectPages` 計算屬性供 UI 顯示「正常 / 發現損毀」

## View 變更

新 `TabItem Header="完整性檢查"`，內含：
- 上方按鈕列：[重新整理]
- 三段 `Expander`（預設展開）：「最後 CHECKDB 時間」、「Suspect Pages」、「最近 CHECKDB Job 紀錄」
- 各段內 `DataGrid`，欄位如上表
- Suspect Pages 區塊在 `Items.Count == 0` 時顯示綠色「✅ 目前無疑似損毀頁面」TextBlock

行著色：依現有專案慣例，在 code-behind 處理 `LoadingRow` 事件，依 `IntegrityHealth` 染色（綠/黃/紅）。

## 測試

- `IntegrityCheckStatusTests`：`Health` 計算屬性 + `EventTypeText` 解碼
- `PerformanceDiagnosticsServiceTests`：mock repository，驗 Health 分類邏輯（4 個 boundary case：null、13、14、30）
- `PerformanceDiagnosticsDocumentViewModelTests`：設計時建構、Command 觸發、`HasSuspectPages` 變化

## 不在範圍

- ❌ 「立即執行 CHECKDB」按鈕：維護計劃已可建立 Job 並可從 Job 管理面板手動觸發；本分頁定位為**檢視**，不執行修改。
- ❌ 自動修復 / `REPAIR_ALLOW_DATA_LOSS`：屬高風險操作，DBA 應手動評估後執行。
- ❌ Suspect Pages 清除：清除動作（如 `DELETE FROM msdb.dbo.suspect_pages WHERE ...`）需要審慎判斷時機，本版不提供。
- ❌ 匯出報表：YAGNI，可日後追加。
- ❌ 排除特定 DB（如 user 想忽略某些 DB）：YAGNI，本版顯示所有 user DB 含 master/model/msdb。

## 風險

| 風險 | 緩解 |
|------|------|
| `DBCC DBINFO` 對某些 DB 可能因權限失敗 | 用 `BEGIN TRY/CATCH` 包覆，失敗的 DB `LastKnownGood = null` 標記為 Unknown |
| 對大量 DB（>50 個）執行 DBINFO 耗時 | 用 `IProgress<string>` 回報進度（與既有 `GetIndexStatusAsync` 同模式） |
| `suspect_pages` 表可能不存在於極舊版本 | SQL 2005+ 都支援，視為硬性需求；若失敗以友善訊息回報 |
| Job History 訊息含換行符破壞 grid 顯示 | UI 層用 `TextWrapping="NoWrap"` + tooltip 顯示完整內容 |
