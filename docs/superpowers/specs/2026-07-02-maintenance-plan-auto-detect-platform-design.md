# 維護計劃頁：平台依實際伺服器自動偵測 設計文件

- **日期**：2026-07-02
- **狀態**：設計已核准，待撰寫實作計畫
- **影響範圍**：Domain（`IBackupService` 新增方法）、Infrastructure（`MssqlBackupService`）、Desktop（維護計劃 ViewModel）
- **分支**：延續 `feature/maintenance-plan-folder-picker`（同一頁的相關強化）

## 1. 背景與目標

維護計劃精靈「步驟 1」的「平台」下拉目前固定預設 `Windows`，需使用者手動改成 Linux。使用者希望**開啟時依目前連線的實際伺服器平台自動帶入**。

目標：維護計劃頁開啟時偵測目前連線伺服器平台（Windows/Linux），自動設定「平台」下拉；使用者仍可手動覆寫。

## 2. 現況調查重點（實作前已確認）

| 項目 | 位置 | 說明 |
|------|------|------|
| 平台下拉 | `MaintenancePlanDocumentViewModel.cs` | `SelectedPlatform`（第 103 行，預設 `"Windows"`）；`PlatformOptions = ["Windows","Linux","其他"]`（第 112 行）；`OnSelectedPlatformChanged`（第 114-129 行）依平台自動填入預設 `BackupPath`/`RestorePath`（Windows `D:\SQLBackup\`、`D:\sql_data\`；Linux `/var/opt/mssql/backup/`、`/var/opt/mssql/data/`）。 |
| 目前連線 | 同檔第 301 行 | 建構時 `_connectionManager.GetCurrentProfile()` 取得目前連線；`_backupService` 已於前一功能注入。 |
| 平台查詢（可重用） | `MssqlBackupService.cs` 私有 `GetHostPlatformAsync` | 已在磁碟 fallback 使用 `SELECT host_platform FROM sys.dm_os_host_info`，回傳 `"Windows"`/`"Linux"`。尚未經介面對外公開。 |

## 3. 設計

### 3.1 Domain：`IBackupService` 新增方法

```csharp
/// <summary>偵測伺服器作業系統平台，回傳 "Windows"/"Linux"/"其他"；取不到時為 null。</summary>
Task<string?> GetServerPlatformAsync(string connectionString, CancellationToken cancellationToken = default);
```

### 3.2 Infrastructure：`MssqlBackupService`

- 實作 `GetServerPlatformAsync`：查詢 `SELECT host_platform FROM sys.dm_os_host_info`，將結果對應為下拉選項字串：
  - `"Windows"` → `"Windows"`
  - `"Linux"` → `"Linux"`
  - 其他非空值 → `"其他"`
  - 查詢失敗 / 結果為 null → 回傳 `null`（例外以 try/catch 吞掉，回 null）。
- 可將既有私有 `GetHostPlatformAsync` 的查詢邏輯重用或抽共用，但不改動其現有呼叫端行為。

### 3.3 Desktop：`MaintenancePlanDocumentViewModel`

- 新增可 await 的方法 `Task DetectServerPlatformAsync()`：
  - 取 `_connectionManager.GetCurrentProfile()`；null → 直接返回（不改平台）。
  - 取連線字串；空 → 返回。
  - `var platform = await _backupService.GetServerPlatformAsync(connectionString);`
  - 若 `platform` 非空且屬於 `PlatformOptions`，於 UI 執行緒設定 `SelectedPlatform = platform`（`Dispatcher.UIThread.Post`）。設定會觸發現有 `OnSelectedPlatformChanged` 自動填入該平台預設路徑。
  - 例外一律 try/catch 吞掉、不改平台、不崩潰。
- 在 DI 建構函式尾端以 fire-and-forget 呼叫：`_ = DetectServerPlatformAsync();`（與既有 `_ = LoadJobsAsync();` 同模式）。

### 3.4 行為說明

- 偵測**只設初始預設值**；使用者之後仍可手動改下拉（手動改照舊觸發路徑自動填入）。
- 偵測到與現值相同（例如實際就是 Windows）→ 屬性未變、不重複填路徑，無副作用。
- 偵測失敗 / 無連線 / 非 Windows/Linux → 維持現有 `Windows` 預設。

## 4. 錯誤處理

| 情境 | 行為 |
|------|------|
| 無目前連線 / 連線字串為空 | 不偵測、維持預設，不崩潰 |
| `dm_os_host_info` 查詢失敗或不存在（舊版 SQL） | `GetServerPlatformAsync` 回傳 null，維持預設 |
| 回傳非 Windows/Linux 值 | 對應為 `"其他"` |

## 5. 測試

- **Desktop VM**：mock `IBackupService.GetServerPlatformAsync`——
  - 回傳 `"Linux"`：`await DetectServerPlatformAsync()` 後 `SelectedPlatform == "Linux"` 且 `BackupPath`/`RestorePath` 為 Linux 預設。
  - 回傳 `null`：`SelectedPlatform` 維持 `"Windows"`（不變）。
  - 無目前連線（`GetCurrentProfile()` 回 null）：不丟例外、`SelectedPlatform` 維持預設。
- **Infrastructure**：`GetServerPlatformAsync` 的 SQL 需真實伺服器，靠建置 + 既有測試無回歸驗收（與既有服務查詢一致，不新增單元測試）。
- 命名 `[方法]_[條件]_[預期]`（繁體中文），xUnit + NSubstitute + FluentAssertions。

## 6. 範圍外（YAGNI）

- 不改 `PlatformOptions` 清單與路徑自動填入邏輯本身。
- 不在使用者手動改平台後再自動覆寫（僅開啟時偵測一次）。
- 不新增 MCP/CLI 對應（服務方法未來可再曝露）。
