# 維護計劃：備份與還原合併為單一 Job（還原步驟預設停用）設計文件

- **日期**：2026-07-03
- **狀態**：設計已核准，待撰寫實作計畫
- **影響範圍**：Domain（enum、config 註解）、Application（service、model）、Infrastructure（SQL 產生器兩條路徑）、Desktop（VM、View）、CLI/MCP（參數 no-op）、多個測試

## 1. 背景與目標

維護計劃精靈目前在「步驟2：選擇執行項目」提供兩個各自獨立的排程：

- **建立每日全備份排程** → SQL Agent Job `{DB}_{RecoveryModel}Backup`，每日於 `BackupTime` 執行完整備份。
- **建立每日還原排程（選填）** → SQL Agent Job `{DB}_FullRestore`，每日於 `RestoreTime` 執行還原到測試庫。

使用者希望**把備份與還原合併為 1 個排程**，且**還原預設不啟動**。

**目標**：改為單一 SQL Agent Job `{DB}_{RecoveryModel}Backup`、單一每日排程（`BackupTime`），內含兩個步驟——Step 1 全備份、Step 2 還原。透過 Step 1 的流程控制讓還原步驟**預設不被觸發**；日後需要時由 DBA 手動啟用。

## 2. 現況調查重點（實作前已確認）

| 項目 | 位置 | 說明 |
|------|------|------|
| SQL 產生器（兩條路徑） | `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs` | (a) per-step：`GenerateCreateBackupJob`（376-460）、`GenerateCreateRestoreJob`（462-549），由 `GenerateStepSql` 分派（24-25 行）。(b) 行內：`GenerateFullSql` 內備份段（約 877-952）與還原段（約 954-1049）。**兩條都要改。** |
| 備份 Step 流程控制 | 產生器 411 / 926 行 | 備份 Step 已是 `@on_success_action = 1`（結束並回報成功）、`@on_fail_action = 2`。此值正好讓**加在其後的 Step 2 預設不會執行**。 |
| Job/Step 命名 | 產生器 380 / 467 行 | 備份 Job `{DB}_{RecoveryModel}Backup`；還原 Job `{DB}_FullRestore`（將移除）。 |
| 步驟列舉 | `src/Specurai.Domain/Enums/MaintenancePlanStep.cs` | 含 `CreateRestoreJob`（將移除）。 |
| 設定物件 | `src/Specurai.Domain/Entities/MaintenancePlanConfig.cs` | `RestoreTime`、`TestDatabaseName`、`RestorePath`。`RestoreTime` 保留但不再用於排程；`TestDatabaseName`/`RestorePath` 仍供還原 Step 使用。 |
| 執行流程 | `src/Specurai.Application/Services/MaintenancePlanService.cs` | `CheckStepsAsync`（62-90）含 `CreateRestoreJob → CheckJobAsync({DB}_FullRestore)`（72）；`ExecutePlanAsync` 有獨立「交易群組 3：還原 Job」（182-190）。兩者皆移除。 |
| 步驟顯示名稱 | `src/Specurai.Application/Models/StepCheckResult.cs` | 37 行 `CreateRestoreJob => "建立還原排程"`（移除）。 |
| 桌面 VM | `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs` | `_restoreTime`（146）、`_isCreateRestoreJobSelected`（175）、`RestoreJobStatus`、`RunStep2Checks` 的還原分支（669-672）、`GetSelectedSteps` 還原項（813）、`BuildConfig` 的 `RestoreTime`（634、793）。 |
| 桌面 View | `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml` | 步驟1「還原時間」選擇器、步驟2「建立每日全備份排程」「建立每日還原排程（選填）」兩勾選。 |
| CLI | `src/Specurai.Cli/Commands/MaintenanceCommand.cs` | `RestoreTime` 參數（168、196）；`SelectedSteps` 由字串名稱解析（153）。 |
| MCP | `src/Specurai.McpServer/Tools/MaintenancePlanTools.cs` | `restoreTime` 參數（多個工具）；`SelectedSteps = Enum.GetValues<MaintenancePlanStep>()`（131）——移除 enum 值後自動不含還原步驟。 |

## 3. 設計決策（與使用者確認）

| 決策 | 選定 |
|------|------|
| 排程結構 | **單一 Job、單一每日排程、雙步驟**（Step 1 備份、Step 2 還原）。還原步驟**建立但停用**（靠 Step 1 的 `on_success = 1` 使其不被觸發）。 |
| 還原步驟是否可選 | **永遠建立**（不再有獨立勾選）；預設不執行。 |
| 啟用還原方式 | DBA 於 SSMS 將 Step 1 的「成功時動作」改為「移至下一步」（`@on_success_action = 3`）。SQL 內加註解說明。 |
| 排程時間 | 單一時間 `BackupTime`；`RestoreTime` 不再用於排程。 |
| 套用範圍 | **共用產生器層**——Desktop/CLI/MCP 產出一致的合併版腳本。 |
| `CreateRestoreJob` enum | **整個移除**（連動 CLI/MCP/測試）。 |
| `RestoreTime` 欄位 | Domain/CLI/MCP **保留**（no-op，避免破壞簽章）；Desktop 步驟1 UI **移除**還原時間選擇器與 VM `RestoreTime` 屬性。 |

## 4. 元件設計

### 4.1 Infrastructure：`MaintenancePlanSqlGenerator`（核心）

**4.1.1 `GenerateStepSql` 分派（24-25）**：移除 `MaintenancePlanStep.CreateRestoreJob => ...` 這一行（enum 值移除後不再存在）。

**4.1.2 `GenerateCreateBackupJob`（per-step 路徑）**：在既有備份 `sp_add_jobstep`（407-442）**之後、`sp_add_jobschedule`（446）之前**，插入還原 Step 2：

- 沿用 `GenerateCreateRestoreJob`（462-549）內原還原命令主體（`SINGLE_USER` → `RESTORE DATABASE [{TestDatabaseName}]` → `MULTI_USER`），以 `sp_add_jobstep @job_name = 備份 Job` 加為第二步：
  - `@step_name = N'Restore Full to {testDbName}'`
  - `@subsystem = N'TSQL'`
  - `@on_success_action = 1`、`@on_fail_action = 2`
  - `@command` = 原還原命令（單引號轉義規則與原本一致）。
- Step 1 備份維持 `@on_success_action = 1`（不改），使 Step 2 預設不觸發。
- 在備份 Step 之後加註解：
  ```
  -- 說明：Step 1 成功後預設「結束並回報成功」，故 Step 2（還原）預設不執行。
  -- 欲啟用每日還原：在 SSMS 將 Step 1 的「成功時動作」改為「移至下一步」。
  ```
- `@description` 更新為：`N'[Specurai] 每日對 {db} 做完整備份（含還原步驟，預設停用），保留 {n} 天'`。

**4.1.3 `GenerateCreateRestoreJob`（462-549）**：**整個移除**（其還原命令主體移用至 4.1.2）。

**4.1.4 `GenerateFullSql` 行內路徑**：
- 備份段（約 877-952）：在備份 `sp_add_jobstep`（922-928）之後、`sp_add_jobschedule`（933）之前，同樣插入還原 Step 2（用行內 `@restoreCmd` 動態 SQL 建構，沿用原還原段 993-1017 的命令），`@job_name = @jobName`，並加同樣的啟用說明註解與 `@description`。
- 還原段（約 954-1049，含 `restoreStep`/`@restoreJobName`/獨立排程）：**整個移除**。

### 4.2 Domain

- `MaintenancePlanStep.cs`：移除 `CreateRestoreJob` 列舉值與其 XML 註解。
- `MaintenancePlanConfig.cs`：`RestoreTime` 保留，於其 XML 註解補「（已不用於排程；還原併入備份 Job 的第二步）」。`TestDatabaseName`、`RestorePath` 不變。

### 4.3 Application

- `MaintenancePlanService.CheckStepsAsync`：移除 `MaintenancePlanStep.CreateRestoreJob => CheckJobAsync(..., $"{db}_FullRestore", ...)` 分派（72 行）。
- `MaintenancePlanService.ExecutePlanAsync`：移除「交易群組 3：還原 Job」整段（180-190，含前一行 `ThrowIfCancellationRequested` 視情況保留一個即可）。
- `StepCheckResult.cs`：移除 37 行 `CreateRestoreJob => "建立還原排程"` 對應。

### 4.4 Desktop：`MaintenancePlanDocumentViewModel`

- 移除 `[ObservableProperty] _restoreTime`（146）、`[ObservableProperty] _isCreateRestoreJobSelected`（175）、`[ObservableProperty] _restoreJobStatus`。
- `RunStep2ChecksAsync`：移除 `case MaintenancePlanStep.CreateRestoreJob`（669-672）。
- `GetSelectedSteps`：移除 `if (IsCreateRestoreJobSelected) steps.Add(CreateRestoreJob)`（813）。
- `BuildConfig`（兩處 634、793）：`RestoreTime` 改傳固定預設（例如 `0`），因 UI 不再提供；或以既有 `BackupTime` 帶入亦可——採 `0`（明確表示未使用）。
- 備份勾選相關字串不變（`IsCreateBackupJobSelected`、`BackupJobStatus` 保留）。

### 4.5 Desktop：`MaintenancePlanDocumentView.axaml`

- **步驟1**：移除「還原時間」`TimePicker`／輸入區塊（原與「備份時間」並排的還原時間欄）。版面調整為僅「備份時間」＋「保留天數」。
- **步驟2**：
  - 「建立每日全備份排程」標籤改為「**建立每日全備份排程（含還原步驟，預設停用）**」，其狀態文字 `BackupJobStatus` 綁定不變。
  - **移除**「建立每日還原排程（選填）」勾選與其狀態列。

### 4.6 CLI / MCP

- **MCP**（`MaintenancePlanTools`）：`SelectedSteps = Enum.GetValues<MaintenancePlanStep>()` 於 enum 移除後自動不含還原步驟，無需改動；`restoreTime` 參數保留為 no-op（仍寫入 config.RestoreTime，但產生器不再據以排程）。
- **CLI**（`MaintenanceCommand`）：`RestoreTime` 參數保留為 no-op；`SelectedSteps` 字串解析（153）需**忽略無法解析為現有 enum 的名稱**（移除後若外部傳入 `"CreateRestoreJob"` 不應丟例外）——確認現行 `Enum.TryParse` 略過失敗項，否則補上防護。

## 5. 錯誤處理

| 情境 | 行為 |
|------|------|
| Step 1 備份失敗 | `@on_fail_action = 2`（結束回報失敗），不進還原步驟。 |
| 還原步驟命令錯誤（啟用後） | 命令內 `BEGIN TRY/CATCH ... THROW`，Job 回報失敗。 |
| CLI 傳入已移除的步驟名稱 | 解析時忽略未知名稱，不丟例外。 |

## 6. 測試

- **Infrastructure `MaintenancePlanSqlGeneratorTests`**：
  - `GenerateCreateBackupJob` 產出**單一 Job、兩個 `sp_add_jobstep`**（備份步驟名 `Full Backup ...`、還原步驟名 `Restore Full to ...`）、**單一 `sp_add_jobschedule`**（`@active_start_time = BackupTime`）。
  - 產出**不含** `{DB}_FullRestore`、不含以 `RestoreTime` 為 `@active_start_time` 的排程。
  - Step 1 仍為 `@on_success_action = 1`；含啟用還原的說明註解。
  - `GenerateFullSql` 對應斷言（合併後單一備份 Job 雙步驟、無獨立還原段）。
  - 移除／改寫現有針對 `GenerateCreateRestoreJob` 與獨立還原排程的測試。
- **Application `MaintenancePlanServiceTests`**：`CheckStepsAsync` 不再回傳 `CreateRestoreJob` 結果；`ExecutePlanAsync` 不再對還原 Job 產生 SQL 呼叫（驗證 `ExecuteSqlAsync` 呼叫次數／內容）。
- **Domain**：`MaintenancePlanConfigTests` 等引用 `CreateRestoreJob` 之處移除／更新；enum 不再含該值。
- **Desktop `MaintenancePlanDocumentViewModelTests`**：移除還原勾選／`RestoreTime` 相關斷言；`GetSelectedSteps` 不含還原；步驟2 檢查對應更新。
- 命名 `[方法]_[條件]_[預期]`（繁體中文），xUnit + NSubstitute + FluentAssertions。
- UI 版面（步驟1 移除還原時間、步驟2 單一勾選）靠建置＋手動驗證。

## 7. 範圍外（YAGNI）

- 不提供 UI 讓使用者直接切換還原步驟的啟用狀態（維持由 SSMS 手動改 on_success）。
- 不移除 Domain/CLI/MCP 的 `RestoreTime` 欄位（保留為 no-op 以免破壞外部簽章）。
- 不改「每週完整性檢查排程（CheckDb）」與其他步驟。
- 不改備份／還原命令本身的邏輯（僅搬移還原命令成為備份 Job 的第二步）。
