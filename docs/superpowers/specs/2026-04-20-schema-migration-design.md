# Schema Migration 功能設計文件

**日期**：2026-04-20
**狀態**：待實作

---

## 概述

以「預設資料庫」為基準，將「目標資料庫」的 Schema 同步至與基準一致。功能涵蓋差異分析、T-SQL 腳本產生（含交易包裝）、直接執行，以及完整的執行報告匯出。

---

## 架構

採用方案 C：擴充現有比對基礎 + 抽出 SqlScriptGenerator 為獨立純函數服務。

### 層級相依性

```
Domain
  └── Entities/SchemaCompare/
        ├── MigrationReport.cs         ← 完整報告（差異清單 + 執行日誌）
        └── MigrationLogEntry.cs       ← 單筆執行記錄（步驟、時間、結果）

Application
  └── Services/
        ├── ISqlScriptGenerator.cs     ← 純函數：SchemaDifference[] → T-SQL
        ├── SqlScriptGenerator.cs
        ├── ISchemaMigrationService.cs ← 協調：比對 + 產生腳本 + 分類風險
        ├── SchemaMigrationService.cs
        ├── ISchemaMigrationExecutor.cs ← 執行腳本、回傳日誌
        └── SchemaMigrationExecutor.cs

Desktop
  └── Views/MigrationView.axaml
  └── ViewModels/MigrationViewModel.cs
```

`SchemaCompareService` 不修改，`SchemaMigrationService` 直接呼叫它取得差異後再處理。

---

## 資料流程

```
使用者選擇「基準 DB」與「目標 DB」
        ↓
SchemaMigrationService.AnalyzeAsync()
  → SchemaCompareService.CompareAsync()  (現有邏輯)
  → 依風險等級分類差異：
      🔴 高風險 → SkippedDifferences（列入報告，不可執行）
      🟡 中風險 → WarnDifferences（需使用者勾選確認）
      🟢 低風險 → AutoDifferences（預設勾選）
        ↓
UI 顯示差異分析表格（可勾選中/低風險項目）
        ↓
使用者確認後 → SqlScriptGenerator.Generate()
  → 產生 BEGIN TRAN ... COMMIT 包裝的 T-SQL
  → 同時產生 Rollback 腳本
        ↓
    ┌──────────────────┐
    │  A. 下載 .sql 檔  │
    │  B. 直接執行      │
    └──────────────────┘
        ↓（選 B）
SchemaMigrationExecutor.ExecuteAsync()
  → 逐步執行，記錄每步驟結果 → MigrationLogEntry
  → 失敗時自動 ROLLBACK
        ↓
MigrationReport 匯出（.sql 腳本 + .txt/.csv 執行日誌）
```

---

## 差異風險分類

### 🔴 高風險 — 警告、不執行（需人工處理）

| 狀況 | 原因 |
|------|------|
| 欄位型態不一致（如 `nvarchar` vs `int`） | 轉換可能失敗或資料遺失 |
| 欄位長度縮短（目標 > 基準） | 資料截斷風險 |
| `NULL` → `NOT NULL`（無預設值） | 現有 NULL 資料會導致 ALTER 失敗 |
| PRIMARY KEY 變更 | 影響所有關聯查詢 |
| FOREIGN KEY 刪除或修改 | 破壞資料完整性 |
| DROP TABLE / DROP COLUMN（基準沒有、目標有） | 不可逆，資料永久遺失 |
| 欄位順序不同 | SQL Server 重建資料表風險 |

### 🟡 中風險 — 警告但允許執行（使用者勾選確認）

| 狀況 | 原因 |
|------|------|
| `NULL` → `NOT NULL`（有預設值） | 可執行但需確認預設值合理 |
| 索引刪除 | 不遺失資料但影響效能 |
| 唯一約束新增 | 若現有資料有重複值會失敗 |
| 欄位長度放大（基準 > 目標） | 安全但要確認 |
| Collation 不一致 | 影響排序與比對行為 |

### 🟢 低風險 — 預設勾選，可直接執行

| 狀況 | 原因 |
|------|------|
| 新增表格 | 不影響現有資料 |
| 新增欄位（允許 NULL） | 安全 |
| 新增索引 | 只影響效能，不影響資料 |
| 新增 View / SP / Function | 不影響現有物件 |
| 描述/註解變更 | 無結構影響 |

---

## SQL 腳本格式

```sql
-- Schema Migration Script
-- 基準環境：Production
-- 目標環境：Staging
-- 產生時間：2026-04-20 15:32:00

BEGIN TRANSACTION;
BEGIN TRY

    -- [低風險] 新增表格 Products
    CREATE TABLE [dbo].[Products] ( ... );

    -- [中風險] 新增欄位 NOT NULL（使用者已確認）
    ALTER TABLE [dbo].[Users] ALTER COLUMN [Phone] nvarchar(20) NOT NULL;

    COMMIT TRANSACTION;
    PRINT 'Migration 成功完成';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT '發生錯誤，已自動回滾：' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## UI 設計

### 差異分析結果表格

| 執行 | 風險 | 物件類型 | 物件名稱 | 差異類型 | 基準值 | 目標值 |
|------|------|---------|---------|---------|--------|--------|
| — | 🔴 | 欄位 | Orders.Amount | 型態不符 | decimal | int |
| — | 🔴 | 欄位 | Users.Email | 長度縮短 | 200 | 500 |
| ☑ | 🟡 | 約束 | Users.Phone | NOT NULL 新增 | NULL | NOT NULL |
| ☑ | 🟡 | 索引 | idx_user_name | 唯一索引新增 | — | — |
| ☑ | 🟢 | 表格 | Products | 新增 | — | — |
| ☑ | 🟢 | 欄位 | Orders.Note | 新增欄位 | — | nvarchar(500) |

- 🔴 高風險：「執行」欄位為 `—`，CheckBox disabled
- 🟡🟢：可勾選，支援全選/取消全選
- 支援依「風險」或「物件類型」排序與篩選
- 底部按鈕：**[預覽 SQL]**、**[下載 .sql]**、**[執行 Migration ▶]**

### 執行報告表格

| 狀態 | 物件名稱 | 動作 | 耗時 | 備註 |
|------|---------|------|------|------|
| ✅ | Products | CREATE TABLE | 120ms | — |
| ✅ | Orders.Note | ADD COLUMN | 45ms | — |
| ⚠️ | Users.Phone | NOT NULL | — | 使用者取消 |
| ℹ️ | Orders.Amount | 型態不符 | — | 高風險未執行 |

底部按鈕：**[下載 SQL 腳本]**、**[下載執行日誌]**、**[關閉]**

---

## 錯誤處理

| 情境 | 處理方式 |
|------|---------|
| 連線失敗（基準或目標） | 分析前驗證連線，顯示錯誤訊息，不進入分析 |
| SQL 執行中途失敗 | 自動 ROLLBACK，日誌標示失敗步驟與錯誤訊息 |
| 唯一約束新增但資料重複 | 執行失敗後 ROLLBACK，報告中說明原因 |
| 使用者中途取消執行 | 不支援中途取消，執行期間按鈕 disabled |

---

## 測試策略（TDD）

| 測試對象 | 測試類型 | 重點 |
|---------|---------|------|
| `SqlScriptGenerator` | 單元測試 | 輸入差異清單 → 驗證 T-SQL 字串正確性 |
| `SchemaMigrationService` | 單元測試 | mock SchemaCompareService，驗證風險分類邏輯 |
| `SchemaMigrationExecutor` | 整合測試 | 對 LocalDB 執行，驗證 ROLLBACK 行為 |
| `MigrationViewModel` | 單元測試 | 初始狀態、勾選邏輯、按鈕啟用條件 |

---

## 涵蓋物件類型

表格、欄位、索引、約束、檢視表（View）、預存程序（Stored Procedure）、函數（Function）、觸發程序（Trigger）
