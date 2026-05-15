# 效能診斷 — 實例健康分頁設計

- 日期:2026-05-15
- 作者:Kerry Huang
- 範圍:在「效能診斷」TabControl 新增「實例健康」分頁,提供三項實例層級的健康快照(VLF / TempDB / Max Server Memory)。

## 背景與動機

「完整性檢查」分頁(v1.14.0 已上線)聚焦於「資料庫層級」(per-DB)的健康。但 SQL Server 還有一批**實例層級**的設定問題,常見於體檢清單但目前需要 DBA 自行查 DMV 才能取得:

1. **VLF 數量** — 交易記錄檔的虛擬記錄檔數量過多會讓備份/還原/啟動變慢,業界門檻通常 <500 健康、500-1000 注意、>1000 警告。
2. **TempDB 配置** — 檔案數應約等於 CPU 邏輯核心數(上限 8),且各檔大小一致,以避免 PFS 競爭。SQL 2016+ 自動啟用 TF1117/1118 行為。
3. **Max Server Memory** — 預設值是 2 PB(無限制),不限會讓 SQL Server 把整個 OS 記憶體吃光、擠掉系統與其他服務。建議預留 `max(2GB, 10%)` 給 OS。

本分頁將這三項整合在一頁,**read-only**,不執行任何修改。

## 設計位置

`PerformanceDiagnosticsDocumentView.axaml` 的 `TabControl` 內,**插在「完整性檢查」之後**新增 `TabItem`:

```
等候事件 / 耗時查詢 / 索引分析 / 缺少索引 / 統計資訊 / 錯誤記錄 / 完整性檢查 / 【實例健康】
```

選擇此分頁時不自動載入,由使用者按「重新整理」觸發。

## 三個區塊(同一分頁內,垂直排列)

### 區塊 1:VLF 數量(每個資料庫)

| 欄位 | 來源 |
|------|------|
| Database | `sys.databases.name`(排除 tempdb) |
| VLF Count | `SELECT COUNT(*) FROM sys.dm_db_log_info(database_id)` |
| Log Size MB | `SUM(file_size) / 1024 / 1024` |
| 健康分級 | <500 🟢 / 500-1000 🟡 / >1000 🔴 |

實作備註:`sys.dm_db_log_info` 在 SQL 2016 SP2+/2017+ 可用;舊版需用 `DBCC LOGINFO`(每個 DB 跑一次)。本版只支援 2016 SP2+,若不可用則該行回 Unknown。

### 區塊 2:TempDB 配置

單一資訊面板(不是 DataGrid),顯示:

| 項目 | 來源 |
|------|------|
| 資料檔數量 | `SELECT COUNT(*) FROM tempdb.sys.database_files WHERE type = 0` |
| CPU 邏輯核心數 | `SELECT cpu_count FROM sys.dm_os_sys_info` |
| 建議檔案數 | `MIN(cpu_count, 8)` |
| 各檔大小是否一致 | 比對所有資料檔 size 是否相同 |
| TF1117/1118 自動啟用 | SQL 版本 ≥ 13 (2016) |
| 健康分級 | 全 OK 🟢 / 部分不符 🟡 / 多項不符 🔴 |

提示文字:「SQL 2016+ 自動啟用 TF1117(成長一致)/ TF1118(混合區段配置)」

### 區塊 3:Max Server Memory

單一資訊面板,顯示:

| 項目 | 來源 |
|------|------|
| 目前設定 (MB) | `SELECT value_in_use FROM sys.configurations WHERE name = 'max server memory (MB)'` |
| OS 總記憶體 (MB) | `SELECT total_physical_memory_kb / 1024 FROM sys.dm_os_sys_memory` |
| 建議值 (MB) | `OS_total - max(2048, OS_total * 0.1)` |
| 健康分級 | 已設且 ≤ 建議值 🟢 / 未設定(=2147483647) 🔴 / 超過建議值 🟡 |

提示文字:「建議預留 max(2GB, 10%) 給 OS 與其他服務」

## Domain / Application 變更

### Domain

新增 Entities:

```csharp
// VlfStatus.cs
public class VlfStatus
{
    public required string DatabaseName { get; init; }
    public int? VlfCount { get; init; }       // null = 無法判斷(權限/版本)
    public int? LogSizeMB { get; init; }
    public required InstanceHealth Health { get; init; }
}

// TempDbConfiguration.cs
public class TempDbConfiguration
{
    public required int DataFileCount { get; init; }
    public required int CpuCount { get; init; }
    public required int RecommendedFileCount { get; init; }   // min(cpu, 8)
    public required bool AllFilesEqualSize { get; init; }
    public required bool TfAutoEnabled { get; init; }         // SQL 2016+
    public required InstanceHealth Health { get; init; }
}

// MaxServerMemoryConfiguration.cs
public class MaxServerMemoryConfiguration
{
    public required long CurrentMB { get; init; }
    public required long OsTotalMB { get; init; }
    public required long RecommendedMB { get; init; }
    public required InstanceHealth Health { get; init; }
    public bool IsUnlimited => CurrentMB == 2147483647;        // SQL 預設值
}

// InstanceHealth.cs (共用 enum)
public enum InstanceHealth
{
    Healthy,
    Warning,
    Critical,
    Unknown
}
```

### Repository — 擴充 `IPerformanceDiagnosticsRepository`

新增三個方法:

```csharp
Task<IReadOnlyList<VlfRow>> GetVlfCountsAsync(IProgress<string>? progress = null, CancellationToken ct = default);
Task<TempDbConfigurationRaw> GetTempDbConfigurationAsync(CancellationToken ct = default);
Task<MaxServerMemoryConfigurationRaw> GetMaxServerMemoryAsync(CancellationToken ct = default);
```

回原始 DTO(不算 Health),Service 算 Health。新增 DTO:

```csharp
// VlfRow.cs (raw)
public class VlfRow { public required string DatabaseName { get; init; } public int? VlfCount { get; init; } public int? LogSizeMB { get; init; } }

// TempDbConfigurationRaw.cs
public class TempDbConfigurationRaw {
    public required int DataFileCount { get; init; }
    public required int CpuCount { get; init; }
    public required bool AllFilesEqualSize { get; init; }
    public required int SqlMajorVersion { get; init; }   // 從 SERVERPROPERTY('ProductMajorVersion')
}

// MaxServerMemoryConfigurationRaw.cs
public class MaxServerMemoryConfigurationRaw { public required long CurrentMB { get; init; } public required long OsTotalMB { get; init; } }
```

### Application — 擴充 `IPerformanceDiagnosticsService`

新增:

```csharp
Task<IReadOnlyList<VlfStatus>> GetVlfStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default);
Task<TempDbConfiguration> GetTempDbConfigurationAsync(CancellationToken ct = default);
Task<MaxServerMemoryConfiguration> GetMaxServerMemoryAsync(CancellationToken ct = default);
```

Health 分類純函式:

```csharp
private static InstanceHealth ClassifyVlfHealth(int? count) => count switch
{
    null => InstanceHealth.Unknown,
    < 500 => InstanceHealth.Healthy,
    < 1000 => InstanceHealth.Warning,
    _ => InstanceHealth.Critical
};

private static InstanceHealth ClassifyTempDbHealth(int actual, int recommended, bool equalSize)
{
    int issues = 0;
    if (actual < recommended) issues++;
    if (!equalSize) issues++;
    return issues switch { 0 => InstanceHealth.Healthy, 1 => InstanceHealth.Warning, _ => InstanceHealth.Critical };
}

private static InstanceHealth ClassifyMaxMemHealth(long current, long recommended) =>
    current == 2147483647 ? InstanceHealth.Critical
    : current <= recommended ? InstanceHealth.Healthy
    : InstanceHealth.Warning;

private static long CalcMaxMemRecommended(long osTotalMB) =>
    osTotalMB - Math.Max(2048, osTotalMB / 10);
```

## ViewModel 變更

`PerformanceDiagnosticsDocumentViewModel` 新增:

- `ObservableCollection<VlfStatus> VlfStatuses`
- `[ObservableProperty] TempDbConfiguration? _tempDbConfig`
- `[ObservableProperty] MaxServerMemoryConfiguration? _maxMemConfig`
- `[ObservableProperty] bool _isLoadingInstance`
- `[ObservableProperty] string _instanceProgressMessage`
- `RelayCommand RunInstanceHealthAnalysisCommand` — `Task.WhenAll` 並行三個 query

## View 變更

新 `TabItem Header="實例健康"`,內含:
- 工具列:[重新整理] + 進度訊息
- 三段 `Expander`(預設展開):
  - VLF — DataGrid
  - TempDB — Grid 表格(屬性:值)+ 健康提示
  - Max Server Memory — Grid 表格 + 健康提示

## 測試

- `VlfStatusTests` / `TempDbConfigurationTests` / `MaxServerMemoryConfigurationTests`:屬性測試 + `IsUnlimited`
- `PerformanceDiagnosticsServiceTests`:
  - VLF Health 邊界:null / 0 / 499 / 500 / 999 / 1000
  - TempDB Health:全 OK / 缺一項 / 缺多項
  - MaxMem Health:Unlimited / 健康 / 過量
  - `CalcMaxMemRecommended`:8GB → 8192-2048=6144 MB(因 10% 為 819 MB 小於 2GB);128GB → 128*1024 - 13107 = 117965 MB
- `PerformanceDiagnosticsDocumentViewModelTests`:設計時建構、空集合

## 不在範圍

- ❌ 修改實例組態(會用到 `sp_configure RECONFIGURE` 屬高風險,本版純檢視)
- ❌ TempDB 自動重建檔案(需停 SQL Server,絕對不在工具範圍)
- ❌ VLF 自動 shrink/重建 log(風險高,DBA 應手動執行)
- ❌ 匯出報表(YAGNI)

## 風險

| 風險 | 緩解 |
|------|------|
| `sys.dm_db_log_info` 在 SQL 2014 以下不可用 | try/catch,該 DB 標 Unknown,UI 顯示「需 SQL 2016 SP2+」提示 |
| `sys.dm_os_sys_memory` 在某些容器版本可能受限 | 同 try/catch 處理,顯示 N/A |
| Max Memory 預設值 2147483647 易誤解 | UI 顯示時轉「未設定(無限制)」並標紅 |
