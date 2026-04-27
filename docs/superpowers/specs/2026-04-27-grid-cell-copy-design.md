# 設計：DataGrid 儲存格複製功能（全專案套用）

- **日期**：2026-04-27
- **範疇**：`Specurai.Desktop` 層 — 桌面 UI 行為強化
- **影響層級**：Desktop（不變動 Domain / Application / Infrastructure）

## 背景

目前專案 15 個 View、共 35 個 `DataGrid`，其中只有 `SqlQueryDocumentView`（SQL 查詢視窗）以 code-behind 自訂方式實作了「按 Ctrl+C 或右鍵僅複製當前儲存格的值」。其餘 34 個 DataGrid 仍套用 Avalonia 預設行為 —— 整列以 tab 串接後寫入剪貼簿 —— 對使用者「複製單一欄位值」的常見操作不友善。

需要將 SqlQuery 的複製體驗一般化、套用至所有 Grid。

## 目標

1. 全專案 Grid 一致提供：
   - **Ctrl+C** → 複製當前儲存格值（純文字，無欄位名）
   - **右鍵選單** → 「複製儲存格」 / 「複製整列」
2. 不破壞既有可編輯儲存格的編輯體驗。
3. 消除 `SqlQueryDocumentView.axaml.cs` 中既存的重複實作。

## 非目標

- 不處理 CSV / TSV / 含表頭 等多種匯出格式（如需另開設計）。
- 不變更 Grid 預設選取模式、不啟用多儲存格選取複製。
- `DataGridTemplateColumn` 等視覺化欄位不在儲存格複製對象內。

## 設計決策

### 決策 1：複製範圍只涵蓋 `DataGridBoundColumn`

**選擇**：只對 `DataGridTextColumn` 與 `DataGridCheckBoxColumn`（皆繼承 `DataGridBoundColumn`）開放儲存格複製；`DataGridTemplateColumn` 不處理。

**理由**：TemplateColumn 內可能是彩色徽章、按鈕、組合控制項，沒有單一可預期的「值」。強行抽取會產生不直觀結果。CheckBox 複製為 `True`/`False` 即可。

### 決策 2：可編輯儲存格的 Ctrl+C 放行

**選擇**：當 `KeyDown` 的 `e.Source` 為 `TextBox` 或 `TextPresenter`（即正處於編輯模式）時，Behavior 不攔截 Ctrl+C，由 TextBox 原生處理（複製選取文字）。

**理由**：保留 `TableDetailDocumentView` 中「說明」欄等可編輯欄位的細部編輯體驗。離開編輯狀態後 Ctrl+C 即恢復「複製整格」。

### 決策 3：以 AttachedProperty Behavior 實作

**選擇**：新增 `Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs`，公開單一附加屬性 `Enable`，在每個 DataGrid 上以 `behaviors:DataGridCellCopyBehavior.Enable="True"` 啟用。

**為何不用全域 Style**：Avalonia Style 無法掛 `KeyDown` Tunnel；共用的 `ContextMenu` 在多 Grid 間會搶用衝突。

**為何不用 code-behind 助手方法**：要修改 15 個 code-behind，許多 DataGrid 沒有 `x:Name`，需補命名；維護成本高、AXAML 看不出哪些 Grid 啟用了功能。

## 架構

### 新增檔案

```
src/Specurai.Desktop/Behaviors/
    DataGridCellCopyBehavior.cs
tests/Specurai.Desktop.Tests/Behaviors/
    DataGridCellCopyBehaviorTests.cs
```

### Behavior 公開介面

```csharp
namespace Specurai.Desktop.Behaviors;

public static class DataGridCellCopyBehavior
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>(
            "Enable", typeof(DataGridCellCopyBehavior));

    static DataGridCellCopyBehavior()
    {
        EnableProperty.Changed.AddClassHandler<DataGrid>(OnEnableChanged);
    }

    public static void SetEnable(DataGrid d, bool v) => d.SetValue(EnableProperty, v);
    public static bool GetEnable(DataGrid d) => d.GetValue(EnableProperty);
}
```

### 內部組成

| 元件 | 職責 |
|---|---|
| `OnEnableChanged` | 監聽附加屬性變化，連接 `AttachedToVisualTree` / `DetachedFromVisualTree` |
| `Attach(DataGrid)` | 設 `ClipboardCopyMode = None`、注入 `ContextMenu`、掛 `KeyDown` Tunnel |
| `Detach(DataGrid)` | 反註冊事件、清除 `ContextMenu` |
| `OnKeyDown` | 偵測編輯狀態 → 放行；否則執行「複製儲存格」 |
| `CopyCurrentCell(DataGrid)` | 取當前欄位 BindingPath → 取當前 row 值 → 寫入剪貼簿 |
| `CopyCurrentRow(DataGrid)` | 走訪所有 `DataGridBoundColumn`、取值、tab 串接、寫入剪貼簿 |
| `GetBindingPath(DataGridColumn)` | 從 `DataGridBoundColumn.Binding.Path` 抽欄位名（兼容 `[Key]` 與 `PropertyName`） |
| `GetCellValue(row, path)` | 雙模式：`IDictionary<string, object?>` 走 key 查找；其他走反射屬性 |

### 核心程式片段（設計用，實作以最終為準）

```csharp
private static string? GetBindingPath(DataGridColumn column)
{
    if (column is DataGridBoundColumn bound &&
        bound.Binding is Avalonia.Data.Binding b &&
        !string.IsNullOrEmpty(b.Path))
    {
        return b.Path.TrimStart('[').TrimEnd(']');
    }
    return null;
}

private static string? GetCellValue(object row, string path)
{
    if (row is IDictionary<string, object?> dict)
        return dict.TryGetValue(path, out var v) ? v?.ToString() : null;
    return row.GetType().GetProperty(path)?.GetValue(row)?.ToString();
}

private static void OnKeyDown(object? sender, KeyEventArgs e)
{
    if (e.Source is TextBox or TextPresenter) return;
    if (e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control) return;
    if (sender is not DataGrid grid) return;
    e.Handled = true;
    CopyCurrentCell(grid);
}
```

### ContextMenu 策略

每個啟用 Behavior 的 DataGrid 在 Attach 時建立**獨立的** `ContextMenu` 實例並指派給 `dataGrid.ContextMenu`。Detach 時清掉。不共用實例避免多 Grid 搶用。

選單項目固定兩項：
- 「複製儲存格」→ 觸發 `CopyCurrentCell(grid)`
- 「複製整列」→ 觸發 `CopyCurrentRow(grid)`

若 DataGrid 在 AXAML 已自訂 `ContextMenu`（目前無），Behavior 僅追加項目而非覆寫。

### 邊界情境

- `dataGrid.SelectedItem == null`（未選取行）→ 兩種複製動作皆 no-op，不寫入剪貼簿、不丟例外
- `dataGrid.CurrentColumn == null`（從未點過任何儲存格）→ 「複製儲存格」no-op；「複製整列」仍可執行
- 取得的值為 `null` → 寫入空字串

## 套用清單（35 個 DataGrid）

| View 檔案 | DataGrid 數 |
|---|---|
| `BackupRestoreDocumentView.axaml` | 1 |
| `ColumnSearchDocumentView.axaml` | 3 |
| `ColumnUsageDocumentView.axaml` | 1 |
| `HealthMonitoringDocumentView.axaml` | 3 |
| `ImportJobWindow.axaml` | 1 |
| `MaintenancePlanDocumentView.axaml` | 3 |
| `MissingIndexReportDocumentView.axaml` | 1 |
| `PerformanceDiagnosticsDocumentView.axaml` | 8 |
| `SchemaCompareDocumentView.axaml` | 1 |
| `SchemaMigrationDocumentView.axaml` | 2 |
| `SqlQueryDocumentView.axaml` | 1（並收斂 code-behind） |
| `TableDetailDocumentView.axaml` | 4 |
| `TableStatisticsDocumentView.axaml` | 1 |
| `UnusedIndexReportDocumentView.axaml` | 1 |
| `UsageAnalysisDocumentView.axaml` | 4 |
| **合計** | **35** |

每個 `<DataGrid ...>` 加：
- `xmlns:behaviors="using:Specurai.Desktop.Behaviors"`（檔頭命名空間，每個 axaml 加一次）
- `behaviors:DataGridCellCopyBehavior.Enable="True"`（每個 DataGrid）

## SqlQuery 收斂

`SqlQueryDocumentView.axaml`：
- 移除 `ClipboardCopyMode="None"`
- 移除整段 `<DataGrid.ContextMenu>`
- 加 `behaviors:DataGridCellCopyBehavior.Enable="True"`

`SqlQueryDocumentView.axaml.cs`：
- 刪除 `OnGridKeyDown`、`OnCopyCellClicked`、`OnCopyRowClicked`、`CopyCurrentCell`、`GetCurrentColumnName`
- 移除建構函式中的 `ResultGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);`
- 保留 `OnDataContextChanged`、`OnResultColumnsChanged`、`UpdateResultGridColumns`

## 測試

### 單元測試（`tests/Specurai.Desktop.Tests/Behaviors/DataGridCellCopyBehaviorTests.cs`）

| 測試 | 驗證 |
|---|---|
| `GetBindingPath_對_TextColumn_回傳屬性名` | `DataGridTextColumn{ Binding=new Binding("Name") }` → `"Name"` |
| `GetBindingPath_對_CheckBoxColumn_回傳屬性名` | `DataGridCheckBoxColumn{ Binding=new Binding("IsActive") }` → `"IsActive"` |
| `GetBindingPath_對_TemplateColumn_回傳_null` | `DataGridTemplateColumn` → `null` |
| `GetBindingPath_對_中括號路徑_去除括號` | `Binding("[Name]")` → `"Name"`（SqlQuery 動態欄位案例） |
| `GetCellValue_對強型別實體_用反射取值` | `new { Name="A" }` + `"Name"` → `"A"` |
| `GetCellValue_對_Dictionary_用_key_取值` | `Dictionary<string, object?>{["X"]=42}` + `"X"` → `"42"` |
| `GetCellValue_對不存在屬性_回傳_null` | 驗證雙模式皆 null-safe |
| `GetCellValue_對_null_值_回傳_null` | 驗證 |

### 手動煙霧測試（`/run` 後執行）

1. SqlQuery：執行 SELECT，Ctrl+C / 右鍵選單複製儲存格與整列
2. TableDetail「欄位」分頁：在「說明」欄編輯模式下 Ctrl+C 應複製選取文字（不被攔截）
3. ColumnSearch、UsageAnalysis 等 TemplateColumn 較多的 Grid：右鍵「複製儲存格」在 Template 欄不應覆蓋為奇怪的值；Bound 欄則正確
4. PerformanceDiagnostics（8 個 Grid）抽樣驗證

## 風險與緩解

| 風險 | 緩解 |
|---|---|
| Avalonia 版本中 `e.Source` 在編輯模式不一定是 `TextBox` | 加 `TextPresenter` 備援；若仍誤判則改檢查 `dataGrid.CurrentColumn?.GetCellContent()` 型別 |
| AttachedProperty + AttachedToVisualTree 的事件外洩 | `DetachedFromVisualTree` 嚴格成對反註冊；測試手動驗證關閉 Tab 後不重複處理 |
| 動態欄位 SqlQuery 的 `[ColumnName]` 路徑格式 | `GetBindingPath` 同時去除 `[ ]`，與既有實作一致 |
| 反射效能 | 每次 Ctrl+C 才呼叫一次，無感 |

## 驗收標準

- [ ] `DataGridCellCopyBehavior.cs` 完成，單元測試全綠
- [ ] 35 個 DataGrid 皆已加上 `Enable="True"`
- [ ] `SqlQueryDocumentView` 程式碼有減量（移除約 50 行 code-behind）
- [ ] 手動煙霧測試的 4 項全通
- [ ] `dotnet build` 無 warning，`dotnet test` 全綠
