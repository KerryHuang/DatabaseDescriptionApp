# MSSQL 資料庫結構統一專案 - 評估計畫

> 建立日期：2026-01-24
> 狀態：✅ 已完成（歷史文件）
> 後續文件：[Schema Compare 設計](./2026-01-24-schema-compare-design.md)

---

## 一、需求摘要

| 項目 | 內容 |
|------|------|
| 客戶數量 | 約 10 家 |
| 目標 | 讓所有客戶的資料庫結構一致 |
| 標準結構來源 | 從 10 個客戶中分析決定最佳結構 |
| 欄位處理原則 | 取最大化（如：10碼 vs 20碼 → 取 20碼） |
| 自動化程度 | 產生 Migration SQL 腳本，人工審核後執行 |
| 停機限制 | 可以停機 |
| 資料庫連接 | 可直接連接 |
| 評估範圍 | 完整結構（Tables, Views, SPs, Functions, Triggers, Indexes, Constraints） |

### 已識別的問題

- 相同資料表欄位數不一致
- 相同資料表相同欄位長度或型別不一致
- 不同資料表相同欄位長度或型別不一致
- 可能還有其他問題（見下方分析）

---

## 二、差異分析架構

需要建立一個工具來比較所有資料庫的結構，主要分析對象：

### 2.1 Tables & Columns 分析

```
比較項目：
├── 表格存在性（哪些客戶有/沒有某個表格）
├── 欄位存在性（哪些客戶有/沒有某個欄位）
├── 欄位屬性
│   ├── 資料型別 (varchar vs nvarchar vs char)
│   ├── 長度/精度 (varchar(10) vs varchar(20))
│   ├── Nullable
│   ├── Default Value
│   └── Identity/Computed Column
└── 欄位順序（如果有需要）
```

### 2.2 Constraints 分析

```
比較項目：
├── Primary Key（名稱、包含欄位）
├── Foreign Key（名稱、參照表格、參照欄位、刪除/更新規則）
├── Unique Constraint
├── Check Constraint
└── Default Constraint
```

### 2.3 Indexes 分析

```
比較項目：
├── Index 名稱
├── 類型（Clustered/Non-Clustered）
├── 包含欄位及順序
├── Include Columns
└── Filter 條件
```

### 2.4 其他物件分析

```
比較項目：
├── Views（定義差異）
├── Stored Procedures（定義差異）
├── Functions（定義差異）
└── Triggers（定義差異）
```

---

## 三、Migration 腳本產生策略

### 3.1 標準結構決定規則（最大化原則）

| 差異類型 | 決定規則 |
|---------|---------|
| 表格存在性 | 聯集 - 任一客戶有的表格都納入標準 |
| 欄位存在性 | 聯集 - 任一客戶有的欄位都納入標準 |
| varchar/nvarchar 長度 | 取最大值 |
| decimal 精度 | 取最大精度和小數位數 |
| 型別不同 | **需人工決定** - 標記為衝突 |
| Nullable | 取 NULL（較寬鬆）除非有特殊考量 |
| Default 值不同 | **需人工決定** |

### 3.2 腳本產生順序（考慮相依性）

```
Phase 1: 準備階段
├── 備份資料庫
└── 記錄現有結構

Phase 2: 移除相依物件
├── Drop Triggers
├── Drop Views（依相依順序）
├── Drop Foreign Keys
└── Drop Indexes

Phase 3: 表格結構變更
├── 新增遺漏的表格
├── 新增遺漏的欄位
├── 修改欄位屬性（ALTER COLUMN）
└── 處理 Identity 欄位（可能需要重建表格）

Phase 4: 重建約束
├── 重建 Primary Keys
├── 重建 Unique Constraints
├── 重建 Foreign Keys
└── 重建 Check Constraints

Phase 5: 重建物件
├── 重建 Indexes
├── 重建 Views
├── 重建 Stored Procedures
├── 重建 Functions
└── 重建 Triggers

Phase 6: 驗證
├── 比較結構確認一致
└── 執行測試腳本
```

---

## 四、潛在問題與風險

### 4.1 已識別的問題

| 問題 | 風險等級 | 處理策略 |
|------|---------|---------|
| 相同資料表欄位數不一致 | 中 | 聯集所有欄位，遺漏的補 NULL 或 Default |
| 相同資料表相同欄位長度不一致 | 低 | 取最大長度 |
| 相同資料表相同欄位型別不一致 | **高** | 需人工決定，可能需要資料轉換 |
| 不同資料表相同欄位長度/型別不一致 | 中 | 建立欄位命名規範，逐一檢視 |

### 4.2 可能遺漏的問題

| 問題 | 風險等級 | 說明 |
|------|---------|------|
| **Identity 欄位差異** | 高 | Seed/Increment 值不同，或某客戶沒有 Identity |
| **Collation 不一致** | 高 | 字元排序規則不同會導致 JOIN/比較問題 |
| **Computed Column 差異** | 中 | 計算公式不同 |
| **欄位順序不一致** | 低 | 通常不影響功能，但某些 ORM 可能有問題 |
| **Index 名稱/定義不一致** | 中 | 影響查詢效能一致性 |
| **FK 的 ON DELETE/UPDATE 規則不同** | 高 | 影響資料完整性行為 |
| **SP/Function 邏輯差異** | **高** | 可能是客製化，不能直接覆蓋 |
| **物件名稱大小寫差異** | 低 | 取決於 Server Collation |
| **Schema 不一致** | 中 | dbo vs 其他 Schema |
| **資料表權限設定** | 低 | 安全性相關 |

### 4.3 無法自動處理的情況

1. **型別完全不相容**：如 `varchar` vs `int` - 需要理解業務邏輯
2. **SP/Function 客製化邏輯**：可能是故意不同的
3. **欄位語意變更**：同名欄位在不同客戶代表不同意義
4. **資料遺失風險**：如 `varchar(100)` 改成 `varchar(50)` 雖然取最大，但反過來的情況需要小心

---

## 五、建議實施階段

### 階段 1：結構收集與分析
- 建立工具連接所有 10 個資料庫
- 提取完整 Schema 資訊
- 產生差異分析報告
- 標記需要人工決定的衝突點

### 階段 2：標準結構定義
- 根據最大化原則自動產生建議的標準結構
- 人工審核並解決衝突
- 產生最終的「黃金標準」Schema

### 階段 3：Migration 腳本產生
- 針對每個客戶產生專屬的 Migration 腳本
- 腳本包含備份、變更、驗證三部分
- 產生 Rollback 腳本（以防萬一）

### 階段 4：測試與執行
- 在測試環境驗證腳本
- 逐一客戶執行 Migration
- 執行後結構驗證

---

## 六、建議的工具功能

```
DatabaseMigrationTool/
├── 1. SchemaCollector
│   └── 從所有資料庫收集結構到統一格式
│
├── 2. SchemaComparer
│   └── 比較所有資料庫，產生差異矩陣報告
│
├── 3. StandardSchemaGenerator
│   └── 根據規則產生建議的標準結構
│
├── 4. ConflictResolver (UI/報告)
│   └── 讓人工解決無法自動決定的衝突
│
├── 5. MigrationScriptGenerator
│   └── 針對每個客戶產生 Migration 腳本
│
└── 6. SchemaValidator
    └── 執行後驗證結構是否一致
```

---

## 七、技術實作參考

### 7.1 收集 Schema 資訊的 SQL 查詢

```sql
-- 取得所有表格和欄位
SELECT
    t.TABLE_SCHEMA,
    t.TABLE_NAME,
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE,
    c.IS_NULLABLE,
    c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.TABLES t
JOIN INFORMATION_SCHEMA.COLUMNS c
    ON t.TABLE_SCHEMA = c.TABLE_SCHEMA
    AND t.TABLE_NAME = c.TABLE_NAME
WHERE t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION;

-- 取得 Primary Keys
SELECT
    tc.TABLE_SCHEMA,
    tc.TABLE_NAME,
    tc.CONSTRAINT_NAME,
    kcu.COLUMN_NAME,
    kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY';

-- 取得 Foreign Keys
SELECT
    fk.name AS FK_NAME,
    tp.name AS PARENT_TABLE,
    cp.name AS PARENT_COLUMN,
    tr.name AS REFERENCED_TABLE,
    cr.name AS REFERENCED_COLUMN,
    fk.delete_referential_action_desc,
    fk.update_referential_action_desc
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id;

-- 取得 Indexes
SELECT
    t.name AS TABLE_NAME,
    i.name AS INDEX_NAME,
    i.type_desc AS INDEX_TYPE,
    i.is_unique,
    c.name AS COLUMN_NAME,
    ic.key_ordinal,
    ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
WHERE i.type > 0
ORDER BY t.name, i.name, ic.key_ordinal;
```

---

## 八、下一步行動

- [ ] 確認 10 個客戶資料庫的連線資訊
- [ ] 執行第一輪結構收集
- [ ] 產生初步差異分析報告
- [ ] 審查差異並識別需人工決定的衝突
- [ ] 設計並開發 Migration 工具

---

*此文件將隨專案進行持續更新*
