# 資料庫維護計劃功能設計

## 概述

在 TableSpec 應用程式中新增「資料庫維護計劃」功能，透過「工具」選單開啟管理視窗，可使用建立精靈設定新的維護計劃，並在管理面板中檢視、管理現有的 SQL Agent Job。

## 選單入口

「工具 → 資料庫維護計劃」，開啟管理視窗。管理視窗中有「新增計劃」按鈕，點擊後開啟建立精靈。

## 建立精靈（Step Wizard）

### 步驟一：基本設定

- 資料庫名稱（下拉選單，從目前連線取得資料庫清單）
- 備份路徑（文字輸入 + 預設值提示）
- 還原路徑（文字輸入 + 預設值提示）
- 測試資料庫名稱（自動帶入 `{database}-test`，可修改）

### 步驟二：選擇步驟

勾選清單，每個步驟附簡短說明：

1. 設定 Recovery Model 為 SIMPLE
2. 重新命名邏輯檔名
3. 建立登入帳號與使用者
4. 將使用者加入 db_owner
5. 建立每日全備份排程
6. 建立每日還原排程（可選，預設不勾選）

步驟 5 和 6 獨立，使用者可只建備份不建還原。

### 步驟三：確認與執行

顯示參數摘要、選擇的步驟，以及前置檢查結果。

- 「產生 SQL」按鈕可預覽完整 SQL
- 「執行」按鈕依序執行，即時顯示各步驟的 PRINT 訊息和成功/失敗狀態

## 步驟前置檢查機制

每個步驟執行前，先檢查目前狀態：

| 步驟 | 檢查內容 | 已存在時的選項 |
|------|----------|---------------|
| Recovery Model | 查詢 `sys.databases` 目前模式 | 若已是 SIMPLE → 顯示「已設定」，可跳過 |
| 重命名邏輯檔名 | 查詢 `sys.master_files` 邏輯名稱 | 若已正確 → 顯示「無需變更」，可跳過 |
| 登入帳號 | 查詢 `sys.server_principals` | 若已存在 → 選擇「跳過 / 刪除重建」 |
| 資料庫使用者 | 查詢 `sys.database_principals` | 若已存在 → 選擇「跳過 / 重新綁定」 |
| db_owner 角色 | 查詢 `sys.database_role_members` | 若已是成員 → 顯示「已設定」，可跳過 |
| 備份排程 Job | 查詢 `msdb.dbo.sysjobs` | 若已存在 → 選擇「跳過 / 刪除重建 / 修改」 |
| 還原排程 Job | 查詢 `msdb.dbo.sysjobs` | 若已存在 → 選擇「跳過 / 刪除重建 / 修改」 |

在精靈步驟三（確認頁面）顯示檢查結果，使用者可針對每個已存在的項目決定處理方式。

## 交易處理

- **交易一**（步驟 1-4，設定類）：包在同一個交易中，任一失敗則全部回滾
- **交易二**（步驟 5，備份 Job）：獨立交易
- **交易三**（步驟 6，還原 Job）：獨立交易

分開交易的原因：SQL Agent Job 的操作（`sp_add_job` 等）本身有內部交易，與 DDL 混在一起容易衝突。且使用者可能只勾選部分步驟，獨立交易讓成功的步驟不受後續失敗影響。

### 執行時 UI 顯示

即時顯示各步驟狀態：

```
交易一：基本設定
  ✅ 步驟 1: Recovery Model 已設為 SIMPLE
  ✅ 步驟 2: 邏輯檔名無需變更（已跳過）
  ✅ 步驟 3: 登入帳號 [mis] 已建立
  ✅ 步驟 4: 已加入 db_owner
交易二：備份排程
  ✅ 步驟 5: Job [DB_FullBackup] 已建立
交易三：還原排程
  ⏭️ 步驟 6: 使用者未勾選，已跳過
```

## 管理面板

開啟後顯示目前連線的 SQL Agent Job 清單（篩選 `_FullBackup` 和 `_FullBackup_and_Restore` 結尾的 Job），以 DataGrid 呈現：

| 欄位 | 說明 |
|------|------|
| Job 名稱 | |
| 狀態 | 啟用/停用 |
| 上次執行時間 | |
| 上次執行結果 | 成功/失敗 |
| 下次排程時間 | |

### 操作按鈕

- 啟用/停用
- 立即執行
- 修改排程（開啟小對話框，設定執行時間和頻率）
- 刪除 Job
- 新增計劃（開啟精靈）

## 分層架構

| 層級 | 新增內容 |
|------|----------|
| **Domain** | `MaintenancePlanStep` 列舉、`AgentJobInfo` 實體、`AgentJobHistory` 實體、`IAgentJobRepository` 介面 |
| **Application** | `IMaintenancePlanService`（產生 SQL、執行計劃）、`IAgentJobService`（查詢/管理 Job） |
| **Infrastructure** | `AgentJobRepository`（查詢/操作 msdb 的 SQL Agent Job） |
| **Desktop** | `MaintenancePlanWizardViewModel`、`MaintenancePlanManagerViewModel`、`MaintenancePlanWizardWindow`、`MaintenancePlanManagerWindow`、`ScheduleEditWindow` |

## SQL 範本邏輯

各步驟的 SQL 硬編碼在 `MaintenancePlanService` 中，使用字串參數替換：

- `{database}` — 資料庫名稱
- `{dbBackupPath}` — 備份路徑
- `{dbPath}` — 還原路徑
- `{testDatabase}` — 測試資料庫名稱
- `{loginName}` — 登入帳號名稱
- `{loginPassword}` — 登入密碼

## 參考範本

基於 `資料庫檢查到備份計劃SIMPLE-範本.sql` 實作，包含：

1. 設定 Recovery Model 為 SIMPLE（主資料庫 + 測試資料庫）
2. 重新命名邏輯檔名（Data + Log）
3. 建立 SQL Server 登入帳號
4. 建立資料庫使用者並綁定
5. 將使用者加入 db_owner 角色
6. 建立全備份 SQL Agent Job（含保留 7 天清理）
7. 建立還原到測試庫的 SQL Agent Job Step
