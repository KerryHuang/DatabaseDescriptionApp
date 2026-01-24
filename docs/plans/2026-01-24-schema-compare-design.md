# Schema Compare 功能設計

> 建立日期：2026-01-24
> 狀態：已確認

---

## 需求摘要

| 項目 | 需求 |
|------|------|
| 使用場景 | 多環境比對（開發、測試、正式、多客戶） |
| 期望結果 | 完整同步工具（報告 + 腳本 + 執行） |
| 基準選擇 | 每次由使用者手動指定 |
| 比對數量 | 4 個以上資料庫 |
| 正式環境安全 | 完整驗證流程（備份→風險評估→確認→執行後驗證） |
| 禁止操作 | 任何可能資料遺失的操作只能匯出腳本 |
| 匯出格式 | Excel + SQL 腳本 + HTML 報告 + 桌面內嵌預覽 |

---

## 整體流程

```
1. 選擇連線 → 2. 收集 Schema → 3. 比對差異 → 4. 解決衝突 → 5. 執行同步/匯出
```

**階段 1 - 選擇連線**：使用者從已設定的連線中勾選要比對的資料庫（可多選），並指定一個作為「基準環境」。

**階段 2 - 收集 Schema**：對每個選中的資料庫執行 SQL 查詢，收集完整的結構資訊（表格、欄位、索引、約束、View、SP、Function、Trigger）。

**階段 3 - 比對差異**：以基準環境為標準，逐一比對其他環境的差異，產生差異清單。每個差異項目會標註風險等級。

**階段 4 - 解決衝突**：對於無法自動判斷的衝突（例如基準有 A 欄位，目標有 B 欄位），由使用者決定處理方式。

**階段 5 - 執行/匯出**：
- 低風險操作：可在驗證後直接執行
- 高風險操作：只能匯出腳本，手動執行
- 匯出 Excel 報告、SQL 腳本、HTML 報告

---

## 風險分級與安全機制

### 風險等級

| 等級 | 標示 | 操作範例 | 允許動作 |
|------|------|----------|----------|
| 🟢 低風險 | 綠色 | 新增 Nullable 欄位、延長 VARCHAR、新增索引 | 可直接執行 |
| 🟡 中風險 | 黃色 | 新增 NOT NULL 欄位（有 Default）、新增約束 | 需人工確認後執行 |
| 🔴 高風險 | 紅色 | 縮短欄位長度、修改資料型別 | 只能匯出腳本 |
| ⛔ 禁止 | 黑色 | DROP TABLE、DROP COLUMN | 只能匯出腳本，並顯示警告 |

### 執行前驗證（Pre-flight Check）

- 連線狀態檢查
- 權限驗證（ALTER、CREATE、DROP）
- 備份狀態檢查（強制要求有最新備份）
- 風險評分計算
- 受影響資料筆數評估

### 執行後驗證（Post-execution）

- 結構比對（確認變更已套用）
- 資料完整性檢查（FK 完整性）
- 產生執行報告

### Rollback 機制

每個執行的變更都會產生對應的回滾腳本，萬一出問題可以還原。

---

## UI 介面設計

Schema Compare 作為 MDI 分頁，包含三個子分頁：

### 分頁 1：比對設定 / 差異清單

```
┌─────────────────────────────────────────────────────────────┐
│  [連線選擇區]                                                │
│  ☑ 開發環境 (★基準)  ☑ 測試環境  ☑ 正式環境  ☑ 客戶A      │
│  [開始比對]  [重新整理]                                      │
├─────────────────────────────────────────────────────────────┤
│  [差異樹狀圖]          │  [差異詳情]                         │
│  📁 Tables (5)         │  ┌───────────────────────────────┐ │
│    └ 🔴 Users          │  │ Users.Email 欄位差異          │ │
│    └ 🟢 Orders         │  │                               │ │
│    └ 🟡 Products       │  │ 基準: NVARCHAR(200)           │ │
│  📁 Views (2)          │  │ 測試: VARCHAR(100) 🔴         │ │
│    └ 🟢 vw_Report      │  │ 正式: NVARCHAR(200) ✓         │ │
│  📁 Stored Procs (3)   │  │                               │ │
│                        │  │ [同步到測試] [只匯出腳本]     │ │
│                        │  └───────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  [動作區]                                                    │
│  [匯出 Excel]  [匯出 SQL 腳本]  [匯出 HTML]  [執行選中項目]  │
└─────────────────────────────────────────────────────────────┘
```

### 分頁 2：報告預覽

```
┌─────────────────────────────────────────────────────────────┐
│   ┌─────────────────────────────────────────────────────┐   │
│   │         (內嵌 HTML 報告檢視器)                      │   │
│   │                                                     │   │
│   │   Schema Compare 報告                               │   │
│   │   產生時間：2026-01-24 15:30                        │   │
│   │                                                     │   │
│   │   ▼ Tables (5 個差異)                               │   │
│   │     ▼ Users                                         │   │
│   │       Email: NVARCHAR(100) → NVARCHAR(200)          │   │
│   │       ...                                           │   │
│   └─────────────────────────────────────────────────────┘   │
│                                                             │
│   [匯出 HTML]  [在瀏覽器開啟]  [列印]                       │
└─────────────────────────────────────────────────────────────┘
```

---

## 比對邏輯

### 存在性比對（最大化原則）

| 情況 | 動作 |
|------|------|
| 基準有，目標沒有 | 目標新增 |
| 目標有，基準沒有 | 基準新增 |

**不刪除任何物件**，所有環境收斂到最完整的標準結構。

### 比對項目

**Tables 表格**
- 存在性

**Columns 欄位**
| 比對項目 | 說明 |
|----------|------|
| 名稱 | 是否存在 |
| 資料型別 | INT vs VARCHAR 等 |
| 長度/精度 | NVARCHAR(100) vs NVARCHAR(200) |
| Nullable | NULL vs NOT NULL |
| Default | 預設值不同 |
| Identity | 是否為自增欄位 |
| Collation | 定序不同 |

**Indexes 索引**
| 比對項目 | 說明 |
|----------|------|
| 名稱 | 索引是否存在 |
| 類型 | Clustered / NonClustered |
| 欄位組成 | 包含哪些欄位、順序 |
| Include Columns | 涵蓋欄位 |
| Filter | 篩選條件 |

**Constraints 約束**
- Primary Key：名稱、欄位組成
- Foreign Key：參照表、ON DELETE/UPDATE 規則
- Unique：欄位組成
- Check：定義內容

**程式物件（View、SP、Function、Trigger）**
- 存在性比對
- 定義內容比對（正規化後比較，忽略格式差異）

---

## 匯出報告格式

### Excel 報告（多個工作表）

| 工作表 | 內容 |
|--------|------|
| 摘要 | 比對時間、環境清單、差異統計 |
| 差異清單 | 所有差異項目、風險等級、狀態 |
| Tables 比對 | 矩陣：列=表格，欄=各環境（✓/✗） |
| Columns 比對 | 詳細欄位屬性比對 |
| Indexes 比對 | 索引差異 |
| 程式物件 | View/SP/Function/Trigger 差異 |

### SQL 腳本（依環境分檔）

```
Scripts/
├── Sync_開發環境.sql
├── Sync_測試環境.sql
├── Sync_正式環境.sql
└── Rollback/
    ├── Rollback_開發環境.sql
    └── ...
```

### HTML 報告

- 單一 HTML 檔案，可直接用瀏覽器開啟
- 包含互動式摺疊/展開
- 可分享給沒有工具的同事檢視
- 同時內嵌於桌面應用程式中預覽

---

## 實作架構（Clean Architecture）

### Domain 層（實體與介面）

```
src/TableSpec.Domain/
├── Entities/SchemaCompare/
│   ├── DatabaseSchema.cs      # 單一資料庫完整快照
│   ├── SchemaTable.cs         # 表格結構
│   ├── SchemaColumn.cs        # 欄位結構
│   ├── SchemaIndex.cs         # 索引結構
│   ├── SchemaConstraint.cs    # 約束結構
│   ├── SchemaComparison.cs    # 比對結果
│   ├── SchemaDifference.cs    # 單一差異項目
│   └── SyncScript.cs          # 同步腳本
├── Enums/
│   ├── DifferenceType.cs      # 新增/修改
│   ├── RiskLevel.cs           # 風險等級
│   └── SyncAction.cs          # 同步動作
└── Interfaces/
    └── ISchemaCollector.cs    # Schema 收集介面
```

### Application 層（服務邏輯）

```
src/TableSpec.Application/Services/
├── ISchemaCompareService.cs   # 比對服務介面
└── SchemaCompareService.cs    # 比對邏輯實作
```

### Infrastructure 層（資料存取與匯出）

```
src/TableSpec.Infrastructure/
├── Services/
│   ├── MssqlSchemaCollector.cs      # SQL Server Schema 收集
│   ├── SchemaCompareExcelExporter.cs # Excel 匯出
│   └── SchemaCompareHtmlExporter.cs  # HTML 匯出
└── Scripts/
    └── SyncScriptGenerator.cs        # SQL 腳本產生器
```

### Desktop 層（UI）

```
src/TableSpec.Desktop/
├── ViewModels/
│   └── SchemaCompareDocumentViewModel.cs
└── Views/
    └── SchemaCompareDocumentView.axaml
```

---

## 與原計畫的差異

| 項目 | 原計畫 | 本設計 |
|------|--------|--------|
| 存在性處理 | 目標多餘的標記為「多餘」 | 最大化原則，基準也新增 |
| HTML 報告 | 僅匯出檔案 | 新增桌面內嵌預覽 |
| 刪除操作 | 允許（高風險） | 只能匯出腳本 |
