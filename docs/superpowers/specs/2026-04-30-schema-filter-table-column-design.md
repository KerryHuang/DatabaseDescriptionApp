# 設計文件：結構比對與 Schema Migration 新增資料表／欄位名稱篩選

**日期**：2026-04-30  
**狀態**：已核准  
**影響範圍**：SchemaCompareDocumentViewModel、SchemaMigrationDocumentViewModel 及對應 AXAML View

---

## 背景

結構比對（SchemaCompare）與 Schema Migration 兩個功能視圖，使用者需要能夠依**資料表名稱**與**欄位名稱**快速縮小差異清單，以便聚焦在特定物件。

---

## ObjectName 格式

`SchemaDifference.ObjectName` 的格式規則：

| 物件類型 | ObjectName 範例 |
|---|---|
| 表格 | `dbo.Orders` |
| 欄位 | `dbo.Orders.[CustomerName]` |
| 索引 | `dbo.Orders.[IX_CustomerName]` |
| 檢視表／預存程序 | `dbo.vw_Orders` |

---

## 方案決策

採用**方案 A：兩個獨立文字篩選欄位（純記憶體篩選）**。

- Schema 差異是應用程式比對兩個環境 Schema 後在記憶體中計算出的結果，不存在 DB 查詢篩選的可能性。
- 資料量天花板低（數百筆），記憶體篩選即時回應，無需重新查詢。

---

## 篩選邏輯規格

### 資料表名稱篩選（FilterTableName）

- 套用對象：所有物件類型
- 條件：`ObjectName.Contains(FilterTableName, OrdinalIgnoreCase)`
- 說明：ObjectName 均以表格完整名稱開頭，故此條件對所有列有效

### 欄位名稱篩選（FilterColumnName）

- 套用對象：僅 `ObjectType == Column` 的列
- 條件：從 ObjectName 解析出欄位名稱部分（`.[ColumnName]` 內），比對是否包含關鍵字
- 非欄位類型的列**不受此篩選影響**，維持顯示
- 解析方式：`ObjectName` 取最後一個 `.[` 之後、`]` 之前的字串

---

## 變更範圍

### Schema Compare（SchemaCompareDocumentViewModel）

**ViewModel：**
- 新增 `[ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredDifferences))] private string _filterTableName`
- 新增 `[ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredDifferences))] private string _filterColumnName`
- 調整 `FilteredDifferences` getter 加入兩個新篩選條件

**View（SchemaCompareDocumentView.axaml）：**
- 在差異清單上方新增篩選列，包含兩個 TextBox（資料表名稱、欄位名稱）

### Schema Migration（SchemaMigrationDocumentViewModel）

**ViewModel：**
- 移除 `FilterObjectName`（原「搜尋物件名稱」單一欄位）
- 新增 `FilterTableName`、`FilterColumnName`
- 調整 `ApplyFilter()` 邏輯

**View（SchemaMigrationDocumentView.axaml）：**
- 將原 `FilterObjectName` TextBox 替換為兩個新 TextBox

---

## UI 樣式

參照截圖，兩個 TextBox 並排顯示，樣式如下：

```
資料表名稱：[輸入資料表名稱關鍵字...]   欄位名稱：[輸入欄位名稱關鍵字...]
```

---

## 測試範圍

- `SchemaCompareDocumentViewModel`：`FilteredDifferences` 依資料表名稱篩選、依欄位名稱篩選（不影響非欄位列）
- `SchemaMigrationDocumentViewModel`：`ApplyFilter()` 同上邏輯驗證
- 兩個篩選同時啟用時的交集行為
