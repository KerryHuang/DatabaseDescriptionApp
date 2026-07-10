# 查詢結果編輯與產生異動 SQL 設計文件

日期：2026-07-10
狀態：已由使用者確認

## 需求

SQL 查詢分頁的查詢結果格可直接編輯儲存格，按「產生異動SQL」比對修改前後差異，產出對應的 UPDATE 語句，供使用者複製後配合 Dry Run（F6）預演、再至正式工具執行。系統本身仍維持唯讀（不直接寫入資料庫）。

### 已確認的決策

| 決策點 | 結論 |
|--------|------|
| WHERE 定位 | 主鍵優先；無主鍵時彈出欄位挑選視窗讓使用者勾選定位欄；略過不選則用全部欄位的原值當條件 |
| 產出位置 | 彈窗顯示（可捲動、可全選）＋「複製」按鈕 |
| 支援範圍 | 僅單一資料表的查詢結果；JOIN／運算式構成的查詢不開放編輯 |
| 中繼資料來源 | `CommandBehavior.KeyInfo`（方案 A）——每欄取得來源表、來源欄、是否主鍵、是否唯讀 |

否決方案 B（ScriptDom 解析 SELECT 找表再查 sys.columns）：別名、View、欄位順序對應都要自行處理，比 KeyInfo 脆弱。

## 架構（依 Clean Architecture）

| 層級 | 新增內容 |
|------|----------|
| **Domain** | `Entities/QueryColumnMetadata`（ColumnName、BaseTable、BaseColumn、IsKey、IsReadOnly）；`ISqlQueryRepository` 新增 `ExecuteQueryWithSchemaAsync`（DataTable＋欄位中繼資料） |
| **Application** | `UpdateSqlGenerator`（純邏輯）：表名＋欄位中繼資料＋原值列/現值列＋定位欄清單 → UPDATE 語句清單 |
| **Infrastructure** | `SqlQueryRepository` 實作 `ExecuteQueryWithSchemaAsync`：`ExecuteReaderAsync(CommandBehavior.KeyInfo)` ＋ `GetSchemaTable()` 對映中繼資料 |
| **Desktop** | SQL 查詢分頁：可編輯結果格、快照、「產生異動SQL」按鈕、定位欄挑選視窗、SQL 彈窗 |

`IsReadOnly` 涵蓋：identity（IsAutoIncrement）、timestamp/rowversion、運算式欄（BaseColumn 為 null）。

## 行為規格

### 編輯體驗

1. 執行查詢（F5）後，若結果欄位的 BaseTable 全部相同且非空 → 判定「可編輯」：結果格開放編輯（`IsReadOnly` 欄位除外），並於載入當下對每列快照原值
2. 「產生異動SQL」按鈕：
   - 比對現值與快照，無異動 → 狀態列提示「無異動」
   - 有異動 → 每個異動列產一句 UPDATE，SET 只含實際改過的欄位
3. 不可編輯的結果（JOIN、多表、運算式構成）→ 結果格維持唯讀；按「產生異動SQL」提示「僅支援單一資料表的查詢結果」
4. Dry Run（F6）的預演結果一律唯讀
5. 重新執行查詢／清除 → 重置快照與異動狀態

### WHERE 定位

- 結果包含主鍵欄（IsKey）→ `WHERE [PK欄] = 原值`（複合主鍵全數帶入）
- 無主鍵 → 彈出「選擇定位欄位」視窗（列出結果欄位供勾選）
  - 使用者勾選 → 用勾選欄位的原值當條件
  - 略過／取消 → 用全部欄位的原值當條件（timestamp/byte[] 除外），並在產出 SQL 開頭加註解：`-- 警告：無主鍵定位，執行前請先 Dry Run 確認影響筆數`
- WHERE 一律使用「原值」（快照值），非編輯後的值；原值為 NULL → `IS NULL`

### SQL 產生規則（UpdateSqlGenerator）

- 字串／char → `N'...'`，單引號跳脫為 `''`
- 數字 → InvariantCulture 字面值
- 日期時間 → `'yyyy-MM-dd HH:mm:ss.fff'`
- bit → `1`／`0`
- Guid → `'...'`
- NULL → SET 用 `NULL`；WHERE 用 `IS NULL`
- 識別字（表名、欄名）一律 `[方括號]`，`]` 跳脫為 `]]`
- timestamp／byte[] 欄位不進 SET 也不進 WHERE
- 每句 UPDATE 一行結尾加分號，多句以換行分隔

### 彈窗

顯示產生的 UPDATE 全文（唯讀 TextBox，可捲動、可全選）＋「複製」按鈕。優先重用既有 `SqlPreviewWindow`，不合適則新建小視窗（同樣式）。

## 錯誤處理

- `GetSchemaTable` 拿不到中繼資料（極端驅動情況）→ 視為不可編輯
- 編輯值無法轉回欄位型別（DataGrid 編輯已大多擋掉）→ 產生時對該格報錯並跳過該列，訊息列出列號與欄名
- 快照與現值列數不一致（防禦）→ 提示重新查詢

## 測試

- **Application.Tests**（重點）：`UpdateSqlGenerator` 純單元測試——單欄/多欄異動、NULL 雙向轉換、各型別字面值、單引號與方括號跳脫、主鍵/手選/全欄位三種 WHERE、複合主鍵、無異動回空、timestamp 排除、警告註解
- **Infrastructure.Tests**：KeyInfo schema 對映邏輯（可離線測的部分）
- **Desktop.Tests**：VM——可編輯判定、快照、無異動提示、不支援訊息、重查重置
- 活庫手動驗證：單表編輯→產生→Dry Run 驗證；JOIN 查詢唯讀；無主鍵表流程
