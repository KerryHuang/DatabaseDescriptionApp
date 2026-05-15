# 維護計劃 — 檔案調校步驟設計

- 日期：2026-05-15
- 作者：Kerry Huang
- 範圍：在「維護計劃」Step 2 清單中，新增兩個資料庫檔案層級的調校步驟。

## 背景與動機

SQL Server 預設或舊資料庫常見兩種檔案配置反模式：

1. **Autogrowth 設定不佳**：mdf 1 MB / ldf 10% 等設定會讓「檔案成長」事件在線上頻繁觸發，且無法預測停頓時間，常在業務尖峰造成卡頓。
2. **無預擴慣例**：完全依賴 autogrowth，等於把成長期間的 I/O 停頓推給業務尖峰時段的使用者承擔。

維護計劃目前僅涵蓋備份/還原/權限等項目，缺少檔案層級的健康調校。本次新增兩個步驟，讓 DBA 在維護計劃中即可一次完成。

## 新增步驟

排序位置：插在 `SetRecoveryModel` 之後、`RenameLogicalFiles` 之前（檔案層級調校相鄰分組）。

### Step A：AdjustAutoGrowth — 調整檔案自動成長設定

| 項目 | 內容 |
|------|------|
| UI 標籤 | 調整檔案自動成長設定 |
| 檢查邏輯 | 讀 `sys.database_files`，逐一檔案判斷：mdf 應為固定 ≥ 64 MB；ldf 應為固定 ≥ 64 MB（不可為百分比） |
| 已最佳化狀態文字 | `自動成長設定已最佳化（資料檔 {x} MB / 記錄檔 {y} MB）` → `AlreadyExists = true` |
| 需調整狀態文字 | `自動成長設定需調整（{檔名}: {現值}）` → `AlreadyExists = false` |
| 執行 SQL | 對每個資料檔 `MODIFY FILE (NAME = ..., FILEGROWTH = {AutoGrowthDataMB}MB)`；對每個記錄檔同理用 `AutoGrowthLogMB` |

### Step B：PreExpandDataFile — 預擴資料檔（保留成長緩衝）

| 項目 | 內容 |
|------|------|
| UI 標籤 | 預擴資料檔（保留成長緩衝） |
| 檢查邏輯 | 對每個資料檔（type = 0）計算 `FreeMB = size - FILEPROPERTY(name, 'SpaceUsed')`；若任一檔案 `FreeMB / size < 20%` → 建議預擴 |
| 充足狀態文字 | `資料檔可用空間充足（{FreePct}%）` → `AlreadyExists = true` |
| 建議狀態文字 | `建議預擴（{檔名} 可用 {FreePct}%）` → `AlreadyExists = false` |
| 磁碟不足文字 | `磁碟空間不足，跳過（free {GB} GB < 需要 {GB} GB）` → `AlreadyExists = true`（視為不需執行，避免被勾選） |
| 執行 SQL | 對每個資料檔 `ALTER DATABASE ... MODIFY FILE (NAME = ..., SIZE = {目標MB})`，目標 = 湊整到 GB 的 `(目前大小 + PreExpandBufferGB)` |

#### 安全護欄

預擴前查 `sys.dm_os_volume_stats` 取得該檔所在磁碟可用空間。若 `available_bytes < (擴增量 * 1.5)` → 檢查結果即標記為「磁碟不足」，且不產生 SQL。倍率 1.5 是給 OS / log / tempdb 的緩衝。

## Domain / Application 變更

### `MaintenancePlanStep` enum

新增兩個值，置於 `SetRecoveryModel` 之後：

```csharp
SetRecoveryModel,
AdjustAutoGrowth,        // 新增
PreExpandDataFile,       // 新增
RenameLogicalFiles,
```

### `MaintenancePlanConfig`

新增三個欄位（皆有預設值，UI 不暴露）：

```csharp
public int AutoGrowthDataMB { get; init; } = 256;
public int AutoGrowthLogMB { get; init; } = 128;
public int PreExpandBufferGB { get; init; } = 5;
```

### `StepCheckResult.StepName`

`switch` 分支新增兩個對應中文名稱：「調整檔案成長設定」、「預擴資料檔」。

### `IMaintenancePlanService` 檢查流程

`CheckStepsAsync` 新增兩個步驟的檢查方法：

- `CheckAutoGrowthAsync(string dbName)` → 查 `sys.database_files`，回傳 `StepCheckResult`。
- `CheckPreExpandAsync(string dbName, int bufferGB)` → 查 `sys.database_files` + `sys.dm_os_volume_stats`，計算可用率與磁碟剩餘。

### `IMaintenancePlanSqlGenerator`

新增兩個產生器方法：

- `GenerateAdjustAutoGrowthSql(string dbName, IReadOnlyList<DatabaseFileInfo> files, int dataMB, int logMB)`
- `GeneratePreExpandDataFileSql(string dbName, IReadOnlyList<DatabaseFileInfo> dataFiles, int bufferGB)`

新增 Domain Entity `DatabaseFileInfo`（純資料載體，由 Repository 填充）：`LogicalName`、`PhysicalName`、`Type`（0 資料 / 1 記錄）、`SizeMB`、`FreeMB`、`IsPercentGrowth`、`GrowthValue`、`VolumeMountPoint`、`VolumeFreeGB`。

### Repository

`IDatabaseInfoRepository` 新增 `GetDatabaseFilesAsync(string dbName)`，Infrastructure 實作以單一 SQL 同時拉 `sys.database_files` 與 `sys.dm_os_volume_stats`。

## UI 變更

`MaintenancePlanDocumentView.axaml` 與 ViewModel：

- 兩個新步驟在現有 `ItemsControl` 自動渲染（清單由 `StepCheckResults` 驅動，因此只要 enum 與 service 提供結果即可，UI 無需個別寫死）。
- 不新增任何輸入欄位。

確認此假設：閱讀 `MaintenancePlanDocumentView.axaml` 時若發現步驟列表是寫死的（非 ItemsControl 動態），則需追加一段 XAML；實作階段確認後處理。

## 測試

依 TDD，先寫測試：

- `MaintenancePlanConfigTests`：新欄位預設值。
- `MaintenancePlanServiceTests`：
  - AutoGrowth 已最佳化 / 需調整兩種情況。
  - PreExpand 充足 / 建議 / 磁碟不足三種情況。
- `MaintenancePlanSqlGeneratorTests`：
  - AdjustAutoGrowth 正確產出每個檔案的 `MODIFY FILE` 語句。
  - PreExpandDataFile 正確湊整 GB 並只擴資料檔（不動 log）。
- `StepCheckResultTests`（若存在）：兩個新 enum 值有對應 `StepName`。

## 不在範圍

- ❌ Compatibility Level 升到 150（已有 `SetCompatibilityLevel` 步驟升至當前版本最新）。
- ❌ Long-running query 偵測（屬 DBA 即時判斷，不在維護計劃範疇）。
- ❌ 自訂成長值與緩衝區的 UI 輸入欄位（YAGNI；預設值適用大多數場景）。
- ❌ Instant File Initialization 啟用（屬 OS 層權限設定，非 T-SQL 可控）。
- ❌ Shrink 檔案（與本次目標相反，且 shrink 是反模式）。

## 風險

| 風險 | 緩解 |
|------|------|
| 預擴失敗（磁碟不足）導致 DB 進入可疑狀態 | 事前檢查 `sys.dm_os_volume_stats`，1.5x 倍率緩衝；不足直接標記跳過 |
| 不同 SQL 版本對 `sys.dm_os_volume_stats` 權限要求不同 | 包 try/catch；查不到時 `VolumeFreeGB = null`，預擴步驟保守標記為「無法判斷磁碟」並跳過 |
| 記錄檔成長受 VLF 影響 | 本設計不主動預擴 ldf；autogrowth 改成 128 MB 固定值已能控制單次停頓 |
