# DataGrid 儲存格複製功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將 SqlQuery 既有的「Ctrl+C 僅複製當前儲存格」體驗，以可重用的 AttachedProperty Behavior 套用至全專案 35 個 DataGrid，並收斂 SqlQueryDocumentView 自訂的 code-behind 實作。

**Architecture:** 新增 `Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs`，公開單一附加屬性 `Enable`，內部於 DataGrid `AttachedToVisualTree` 時設定 `ClipboardCopyMode = None`、注入獨立 `ContextMenu`、掛 Tunnel `KeyDown` 攔截 Ctrl+C；以反射讀取強型別實體屬性、以 `IDictionary<string, object?>` 索引方式讀取動態欄位（SqlQuery 案例）。

**Tech Stack:** .NET 8、Avalonia 11.x、CommunityToolkit.Mvvm、xUnit、FluentAssertions、NSubstitute。

設計參考：`docs/superpowers/specs/2026-04-27-grid-cell-copy-design.md`

---

## 檔案結構

| 檔案 | 動作 | 說明 |
|---|---|---|
| `src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs` | 新增 | 主要 Behavior 實作，公開 `Enable` 附加屬性 |
| `src/Specurai.Desktop/Specurai.Desktop.csproj` | 修改 | 加 `InternalsVisibleTo` 讓測試專案存取 internal 純函式 |
| `tests/Specurai.Desktop.Tests/Behaviors/DataGridCellCopyBehaviorTests.cs` | 新增 | `NormalizeBindingPath` 與 `GetCellValue` 兩個純函式的單元測試 |
| 15 個 `*.axaml` View 檔 | 修改 | 加命名空間、為 35 個 DataGrid 加 `Enable="True"` |
| `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs` | 修改 | 移除舊複製邏輯（5 個方法 + Tunnel 註冊行） |

---

### Task 1：建立 Behavior 純函式骨架（TDD）

**目的**：先以 TDD 完成兩個可單元測試的純函式 —— `NormalizeBindingPath`（字串路徑正規化）與 `GetCellValue`（雙模式取值）。互動性 Behavior 邏輯放下個 Task。

**Files:**
- Create: `src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs`
- Modify: `src/Specurai.Desktop/Specurai.Desktop.csproj`
- Create: `tests/Specurai.Desktop.Tests/Behaviors/DataGridCellCopyBehaviorTests.cs`

- [ ] **Step 1：在 Desktop.csproj 加 `InternalsVisibleTo`**

讀 `src/Specurai.Desktop/Specurai.Desktop.csproj`，在 `<Project>` 結尾前的最後一個 `</ItemGroup>` 之後（或 `</Project>` 之前）加：

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Specurai.Desktop.Tests" />
  </ItemGroup>
```

- [ ] **Step 2：寫測試類別與第一批失敗測試**

新建檔案 `tests/Specurai.Desktop.Tests/Behaviors/DataGridCellCopyBehaviorTests.cs`：

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Specurai.Desktop.Behaviors;
using Xunit;

namespace Specurai.Desktop.Tests.Behaviors;

public class DataGridCellCopyBehaviorTests
{
    // --- NormalizeBindingPath ---

    [Fact]
    public void NormalizeBindingPath_對_null_回傳_null()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeBindingPath_對空字串_回傳_null()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("").Should().BeNull();
    }

    [Fact]
    public void NormalizeBindingPath_對普通屬性名_原樣回傳()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("Name").Should().Be("Name");
    }

    [Fact]
    public void NormalizeBindingPath_對中括號路徑_去除括號()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("[Name]").Should().Be("Name");
    }

    [Fact]
    public void NormalizeBindingPath_對只有左括號_只去左側()
    {
        DataGridCellCopyBehavior.NormalizeBindingPath("[Name").Should().Be("Name");
    }

    // --- GetCellValue（強型別反射）---

    private sealed record SampleRow(string Name, int Age, bool IsActive);

    [Fact]
    public void GetCellValue_對強型別實體_用反射取字串屬性()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "Name").Should().Be("Alice");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取整數屬性以_ToString_輸出()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "Age").Should().Be("30");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取布林屬性以_True_False_輸出()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "IsActive").Should().Be("True");
    }

    [Fact]
    public void GetCellValue_對強型別實體_取不存在屬性_回傳_null()
    {
        var row = new SampleRow("Alice", 30, true);
        DataGridCellCopyBehavior.GetCellValue(row, "DoesNotExist").Should().BeNull();
    }

    // --- GetCellValue（Dictionary 動態欄位）---

    [Fact]
    public void GetCellValue_對_Dictionary_用_key_取值()
    {
        var row = new Dictionary<string, object?> { ["X"] = 42 };
        DataGridCellCopyBehavior.GetCellValue(row, "X").Should().Be("42");
    }

    [Fact]
    public void GetCellValue_對_Dictionary_不存在的_key_回傳_null()
    {
        var row = new Dictionary<string, object?> { ["X"] = 42 };
        DataGridCellCopyBehavior.GetCellValue(row, "Y").Should().BeNull();
    }

    [Fact]
    public void GetCellValue_對_Dictionary_null_值_回傳_null()
    {
        var row = new Dictionary<string, object?> { ["X"] = null };
        DataGridCellCopyBehavior.GetCellValue(row, "X").Should().BeNull();
    }

    [Fact]
    public void GetCellValue_對強型別_null_屬性值_回傳_null()
    {
        var row = new { Description = (string?)null };
        DataGridCellCopyBehavior.GetCellValue(row, "Description").Should().BeNull();
    }
}
```

- [ ] **Step 3：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~DataGridCellCopyBehaviorTests"`

Expected：編譯失敗 —— `Specurai.Desktop.Behaviors` 命名空間不存在、找不到 `DataGridCellCopyBehavior` 型別。

- [ ] **Step 4：建立 Behavior 純函式骨架**

新建檔案 `src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs`：

```csharp
using System.Collections.Generic;

namespace Specurai.Desktop.Behaviors;

/// <summary>
/// 為 DataGrid 啟用「按儲存格複製」行為的附加屬性。
/// 互動邏輯（Ctrl+C 攔截、ContextMenu 注入）將於後續 Task 加入。
/// </summary>
public static class DataGridCellCopyBehavior
{
    /// <summary>
    /// 將 Avalonia Binding.Path 字串正規化為純屬性名（去除前後中括號）。
    /// </summary>
    internal static string? NormalizeBindingPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return raw.TrimStart('[').TrimEnd(']');
    }

    /// <summary>
    /// 從 row 物件依路徑取值並轉為字串。
    /// 支援 IDictionary&lt;string, object?&gt;（動態欄位，如 SqlQuery）與一般強型別物件（反射）。
    /// </summary>
    internal static string? GetCellValue(object row, string path)
    {
        if (row is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(path, out var v) ? v?.ToString() : null;
        }

        var prop = row.GetType().GetProperty(path);
        return prop?.GetValue(row)?.ToString();
    }
}
```

- [ ] **Step 5：執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~DataGridCellCopyBehaviorTests"`

Expected：13 個測試全部 PASS。

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs \
        src/Specurai.Desktop/Specurai.Desktop.csproj \
        tests/Specurai.Desktop.Tests/Behaviors/DataGridCellCopyBehaviorTests.cs
git commit -m "feat(desktop): 新增 DataGridCellCopyBehavior 純函式骨架

- NormalizeBindingPath: Avalonia Binding.Path 字串正規化
- GetCellValue: 強型別反射 + Dictionary 雙模式取值
- 13 個單元測試全綠"
```

---

### Task 2：完成 Behavior 互動邏輯

**目的**：在 Task 1 基礎上補齊 `Enable` 附加屬性、生命週期掛勾、ContextMenu、Ctrl+C 攔截、CopyCurrentCell / CopyCurrentRow。互動邏輯不寫單元測試（需 Avalonia headless 環境），於 Task 3 透過 SqlQuery 整合驗證。

**Files:**
- Modify: `src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs`

- [ ] **Step 1：覆寫 Behavior 完整版本**

完全覆寫 `src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs` 為：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Behaviors;

/// <summary>
/// 為 DataGrid 啟用「按儲存格複製」的附加屬性 Behavior。
/// 設 Enable="True" 後：
///   1. Ctrl+C 改為僅複製目前儲存格的值（編輯模式時放行）。
///   2. 右鍵選單提供「複製儲存格」/「複製整列」。
///   3. 僅對 DataGridBoundColumn（Text、CheckBox）生效；DataGridTemplateColumn 不處理。
/// </summary>
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

    // --- 生命週期 ---

    private static void OnEnableChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
        }
        else
        {
            grid.AttachedToVisualTree -= OnAttached;
            grid.DetachedFromVisualTree -= OnDetached;
            Detach(grid);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) Attach(grid);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) Detach(grid);
    }

    private static void Attach(DataGrid grid)
    {
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;

        if (grid.ContextMenu == null)
        {
            var menu = new ContextMenu();
            var cellItem = new MenuItem { Header = "複製儲存格" };
            cellItem.Click += (_, _) => CopyCurrentCell(grid);
            var rowItem = new MenuItem { Header = "複製整列" };
            rowItem.Click += (_, _) => CopyCurrentRow(grid);
            menu.Items.Add(cellItem);
            menu.Items.Add(rowItem);
            grid.ContextMenu = menu;
        }

        grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void Detach(DataGrid grid)
    {
        grid.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        // ContextMenu 留給 GC；不主動清，避免外部曾持有參考
    }

    // --- 鍵盤攔截 ---

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 編輯模式（焦點在內嵌 TextBox / TextPresenter）時放行
        if (e.Source is TextBox or TextPresenter)
            return;

        if (e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control)
            return;

        if (sender is not DataGrid grid)
            return;

        e.Handled = true;
        CopyCurrentCell(grid);
    }

    // --- 複製動作 ---

    private static void CopyCurrentCell(DataGrid grid)
    {
        if (grid.SelectedItem is null) return;
        var path = GetBindingPath(grid.CurrentColumn);
        if (path == null) return;

        var value = GetCellValue(grid.SelectedItem, path) ?? string.Empty;
        SetClipboardText(grid, value);
    }

    private static void CopyCurrentRow(DataGrid grid)
    {
        if (grid.SelectedItem is null) return;

        var values = grid.Columns
            .Select(GetBindingPath)
            .Where(p => p != null)
            .Select(p => GetCellValue(grid.SelectedItem!, p!) ?? string.Empty);

        var text = string.Join("\t", values);
        SetClipboardText(grid, text);
    }

    private static void SetClipboardText(Control grid, string text)
    {
        TopLevel.GetTopLevel(grid)?.Clipboard?.SetTextAsync(text);
    }

    // --- 取繫結路徑 ---

    private static string? GetBindingPath(DataGridColumn? column)
    {
        if (column is DataGridBoundColumn bound &&
            bound.Binding is Binding b)
        {
            return NormalizeBindingPath(b.Path);
        }
        return null;
    }

    // --- 純函式（Task 1 已測試）---

    internal static string? NormalizeBindingPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return raw.TrimStart('[').TrimEnd(']');
    }

    internal static string? GetCellValue(object row, string path)
    {
        if (row is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(path, out var v) ? v?.ToString() : null;
        }

        var prop = row.GetType().GetProperty(path);
        return prop?.GetValue(row)?.ToString();
    }
}
```

- [ ] **Step 2：建置確認編譯通過**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`

Expected：Build succeeded，0 Warning、0 Error。

- [ ] **Step 3：跑既有測試確認沒回歸**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~DataGridCellCopyBehaviorTests"`

Expected：先前 13 個測試仍然 PASS。

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Desktop/Behaviors/DataGridCellCopyBehavior.cs
git commit -m "feat(desktop): 完成 DataGridCellCopyBehavior 互動邏輯

- Enable 附加屬性與 Attach/Detach 生命週期
- ContextMenu 注入「複製儲存格」/「複製整列」
- Ctrl+C Tunnel 攔截，編輯模式（TextBox/TextPresenter）放行
- 僅對 DataGridBoundColumn 生效，TemplateColumn 跳過"
```

---

### Task 3：套用至 SqlQueryDocumentView 並收斂 code-behind

**目的**：第一個 View 套用，同時驗證「Behavior 行為與 SqlQuery 既有體驗一致」並消除舊複製邏輯。

**Files:**
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs`

- [ ] **Step 1：修改 axaml — 加命名空間、加 Enable、移除舊複製設定**

編輯 `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`：

(a) 在第 6 行 `xmlns:mc="..."` 後加一行：
```xml
             xmlns:behaviors="using:Specurai.Desktop.Behaviors"
```

(b) 將原本第 92~107 行的 `<DataGrid x:Name="ResultGrid" ...> ... </DataGrid>` 整段：
```xml
                <DataGrid x:Name="ResultGrid"
                          ItemsSource="{Binding QueryResults}"
                          AutoGenerateColumns="False"
                          IsReadOnly="True"
                          GridLinesVisibility="All"
                          CanUserResizeColumns="True"
                          CanUserReorderColumns="True"
                          CanUserSortColumns="True"
                          ClipboardCopyMode="None">
                    <DataGrid.ContextMenu>
                        <ContextMenu>
                            <MenuItem Header="複製儲存格" Click="OnCopyCellClicked"/>
                            <MenuItem Header="複製整列" Click="OnCopyRowClicked"/>
                        </ContextMenu>
                    </DataGrid.ContextMenu>
                </DataGrid>
```

替換為：
```xml
                <DataGrid x:Name="ResultGrid"
                          behaviors:DataGridCellCopyBehavior.Enable="True"
                          ItemsSource="{Binding QueryResults}"
                          AutoGenerateColumns="False"
                          IsReadOnly="True"
                          GridLinesVisibility="All"
                          CanUserResizeColumns="True"
                          CanUserReorderColumns="True"
                          CanUserSortColumns="True"/>
```

- [ ] **Step 2：修改 code-behind — 移除舊複製邏輯**

完全覆寫 `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs` 為：

```csharp
using System;
using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class SqlQueryDocumentView : UserControl
{
    private SqlQueryDocumentViewModel? _currentVm;

    public SqlQueryDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.ResultColumns.CollectionChanged -= OnResultColumnsChanged;
        }

        _currentVm = DataContext as SqlQueryDocumentViewModel;

        if (_currentVm != null)
        {
            _currentVm.ResultColumns.CollectionChanged += OnResultColumnsChanged;
            UpdateResultGridColumns();
        }
    }

    private void OnResultColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateResultGridColumns();
    }

    private void UpdateResultGridColumns()
    {
        if (_currentVm == null)
            return;

        ResultGrid.Columns.Clear();

        foreach (var col in _currentVm.ResultColumns)
        {
            if (col is DataGridTextColumn textCol)
            {
                ResultGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = textCol.Header,
                    Binding = textCol.Binding,
                    Width = textCol.Width,
                    IsReadOnly = textCol.IsReadOnly
                });
            }
            else
            {
                try { ResultGrid.Columns.Add(col); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
```

- [ ] **Step 3：建置確認**

Run: `dotnet build`

Expected：Build succeeded，0 Error；可能有「未使用 using」警告應已隨 cleanup 消失。

- [ ] **Step 4：跑測試確認沒回歸**

Run: `dotnet test`

Expected：所有測試 PASS（包含先前 604 個 + 新增 13 個 = 617+ 個）。

- [ ] **Step 5：手動驗證 SqlQuery（短煙霧測試）**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`

驗證項目（在 SqlQuery 視窗）：
1. 選任一連線、執行 `SELECT TOP 5 name FROM sys.tables`
2. 點擊任一儲存格 → 按 `Ctrl+C` → 貼上記事本應為單一儲存格值
3. 同一儲存格右鍵 → 「複製儲存格」 → 貼上應為相同值
4. 右鍵 → 「複製整列」 → 貼上應為 tab 分隔的整列

若 4 項皆通過，繼續下一步。

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml \
        src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs
git commit -m "refactor(desktop): SqlQueryDocumentView 改用 DataGridCellCopyBehavior

收斂自訂的 Ctrl+C 攔截與 ContextMenu 邏輯（移除 5 個方法、約 50 行
code-behind），改用通用 DataGridCellCopyBehavior 附加屬性。"
```

---

### Task 4：套用至 TableDetailDocumentView（4 個 DataGrid）

**目的**：套用至最重要的「物件詳情」視窗。此視窗含**可編輯的「說明」欄**，是驗證「編輯模式 Ctrl+C 放行」的關鍵案例。

**Files:**
- Modify: `src/Specurai.Desktop/Views/TableDetailDocumentView.axaml`

- [ ] **Step 1：加命名空間並為 4 個 DataGrid 加 Enable="True"**

編輯 `src/Specurai.Desktop/Views/TableDetailDocumentView.axaml`：

(a) 在第 5 行 `xmlns:mc="..."` 後新增一行：
```xml
             xmlns:behaviors="using:Specurai.Desktop.Behaviors"
```

(b) 為 4 個 `<DataGrid ...>` 開頭逐一加上 `behaviors:DataGridCellCopyBehavior.Enable="True"`。  
分別在以下行的 `<DataGrid` 開頭之後加入該屬性：
- 第 65 行 `<DataGrid x:Name="ColumnsGrid"`（欄位 Grid）
- 第 86 行 `<DataGrid x:Name="IndexDataGrid"`（索引 Grid）
- 第 116 行 `<DataGrid ItemsSource="{Binding Relations}"`（關聯 Grid）
- 第 131 行 `<DataGrid ItemsSource="{Binding Parameters}"`（參數 Grid）

範例（第 65 行）改為：
```xml
                    <DataGrid x:Name="ColumnsGrid"
                              behaviors:DataGridCellCopyBehavior.Enable="True"
                              ItemsSource="{Binding FilteredColumns}"
                              ...
```

- [ ] **Step 2：建置確認**

Run: `dotnet build`

Expected：Build succeeded，0 Error。

- [ ] **Step 3：手動驗證可編輯欄行為**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`

驗證：
1. 開啟任一資料表詳情頁
2. 在「欄位」分頁的「說明」欄**雙擊進入編輯模式**，輸入幾個字並選取部分文字
3. 按 `Ctrl+C` → 貼上應為**選取的部分文字**（編輯模式放行成功）
4. 點擊離開編輯模式（單擊其他列），再對該欄按 `Ctrl+C` → 貼上應為**整格的值**
5. 右鍵 → 「複製儲存格」/「複製整列」運作正常

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Desktop/Views/TableDetailDocumentView.axaml
git commit -m "feat(desktop): TableDetailDocumentView 啟用儲存格複製"
```

---

### Task 5：套用至「欄位/用量分析」三視圖（ColumnSearch、ColumnUsage、UsageAnalysis）

**Files:**
- Modify: `src/Specurai.Desktop/Views/ColumnSearchDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/ColumnUsageDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/UsageAnalysisDocumentView.axaml`

- [ ] **Step 1：ColumnSearchDocumentView（3 個 DataGrid）**

(a) 在 axaml 檔頭命名空間區加：
```xml
             xmlns:behaviors="using:Specurai.Desktop.Behaviors"
```

(b) 對檔內每個 `<DataGrid` 開頭加 `behaviors:DataGridCellCopyBehavior.Enable="True"`。  
用 grep 確認位置：

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/ColumnSearchDocumentView.axaml`

對列出的每一行套用修改。

- [ ] **Step 2：ColumnUsageDocumentView（1 個 DataGrid）**

同上模式套用。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/ColumnUsageDocumentView.axaml`

- [ ] **Step 3：UsageAnalysisDocumentView（4 個 DataGrid）**

同上模式套用。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/UsageAnalysisDocumentView.axaml`

- [ ] **Step 4：建置確認**

Run: `dotnet build`

Expected：Build succeeded。

- [ ] **Step 5：手動煙霧測試**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`

驗證：
1. 開啟「欄位搜尋」、輸入關鍵字搜尋 → 結果 Grid 上 Ctrl+C 複製單一儲存格 OK
2. 在 ColumnSearch 結果中「主要型別」欄（TemplateColumn 彩色徽章）按右鍵「複製儲存格」應**不寫入剪貼簿**（因為是 TemplateColumn，無 BindingPath）
3. 開啟某個欄位的「使用情境」（ColumnUsage）→ Grid 複製運作正常
4. 開啟「使用分析」（UsageAnalysis）→ 4 個 Grid 複製運作正常

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/Views/ColumnSearchDocumentView.axaml \
        src/Specurai.Desktop/Views/ColumnUsageDocumentView.axaml \
        src/Specurai.Desktop/Views/UsageAnalysisDocumentView.axaml
git commit -m "feat(desktop): 欄位搜尋／用量分析視圖啟用儲存格複製"
```

---

### Task 6：套用至 Schema 系列（SchemaCompare、SchemaMigration）

**Files:**
- Modify: `src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`

- [ ] **Step 1：SchemaCompareDocumentView（1 個 DataGrid）**

加命名空間 + Enable。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml`

- [ ] **Step 2：SchemaMigrationDocumentView（2 個 DataGrid）**

加命名空間 + Enable。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`

- [ ] **Step 3：建置確認**

Run: `dotnet build`

Expected：Build succeeded。

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml \
        src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml
git commit -m "feat(desktop): Schema 比較／遷移視圖啟用儲存格複製"
```

---

### Task 7：套用至索引/統計報表（MissingIndex、UnusedIndex、TableStatistics）

**Files:**
- Modify: `src/Specurai.Desktop/Views/MissingIndexReportDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/UnusedIndexReportDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/TableStatisticsDocumentView.axaml`

- [ ] **Step 1：MissingIndexReportDocumentView（1 個 DataGrid）**

加命名空間 + Enable。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/MissingIndexReportDocumentView.axaml`

- [ ] **Step 2：UnusedIndexReportDocumentView（1 個 DataGrid）**

加命名空間 + Enable。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/UnusedIndexReportDocumentView.axaml`

- [ ] **Step 3：TableStatisticsDocumentView（1 個 DataGrid）**

加命名空間 + Enable。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/TableStatisticsDocumentView.axaml`

- [ ] **Step 4：建置確認**

Run: `dotnet build`

Expected：Build succeeded。

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Desktop/Views/MissingIndexReportDocumentView.axaml \
        src/Specurai.Desktop/Views/UnusedIndexReportDocumentView.axaml \
        src/Specurai.Desktop/Views/TableStatisticsDocumentView.axaml
git commit -m "feat(desktop): 索引／統計報表視圖啟用儲存格複製"
```

---

### Task 8：套用至維運視圖（BackupRestore、MaintenancePlan、ImportJob、HealthMonitoring、PerformanceDiagnostics）

**Files:**
- Modify: `src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/ImportJobWindow.axaml`
- Modify: `src/Specurai.Desktop/Views/HealthMonitoringDocumentView.axaml`
- Modify: `src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml`

- [ ] **Step 1：BackupRestoreDocumentView（1 個）**

加命名空間 + Enable。
Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml`

- [ ] **Step 2：MaintenancePlanDocumentView（3 個）**

加命名空間 + Enable。
Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml`

- [ ] **Step 3：ImportJobWindow（1 個）**

加命名空間 + Enable。
Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/ImportJobWindow.axaml`

- [ ] **Step 4：HealthMonitoringDocumentView（3 個）**

加命名空間 + Enable。
Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/HealthMonitoringDocumentView.axaml`

- [ ] **Step 5：PerformanceDiagnosticsDocumentView（8 個）**

加命名空間 + Enable。  
此檔內有 8 個 DataGrid，逐一處理。

Run: `grep -n "<DataGrid\b" src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml`

- [ ] **Step 6：建置確認**

Run: `dotnet build`

Expected：Build succeeded。

- [ ] **Step 7：手動煙霧測試**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`

抽樣驗證：
1. 開啟「效能診斷」（PerformanceDiagnostics），執行任一診斷項，從 8 個 Grid 中任選 3 個驗證 Ctrl+C 複製儲存格運作
2. 開啟「健康監控」（HealthMonitoring），驗證 3 個 Grid
3. 開啟「維護計畫」（MaintenancePlan），驗證 3 個 Grid

- [ ] **Step 8：Commit**

```bash
git add src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml \
        src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml \
        src/Specurai.Desktop/Views/ImportJobWindow.axaml \
        src/Specurai.Desktop/Views/HealthMonitoringDocumentView.axaml \
        src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml
git commit -m "feat(desktop): 維運與診斷視圖啟用儲存格複製"
```

---

### Task 9：最終驗收

**目的**：確認全範圍套用、測試全綠、煙霧測試完成。

- [ ] **Step 1：全專案建置（Release 模式）**

Run: `dotnet build -c Release`

Expected：Build succeeded，0 Warning（若有 nullable / unused using 警告且來自其他既有檔案，記錄但不修；若為新檔造成則修）。

- [ ] **Step 2：全專案測試**

Run: `dotnet test`

Expected：全部 PASS，總計 ≥617 個測試。

- [ ] **Step 3：清點 Enable 套用數**

Run: `grep -rn "DataGridCellCopyBehavior.Enable" src/Specurai.Desktop/Views/ | wc -l`

Expected：35（與 spec 套用清單一致）。

- [ ] **Step 4：清點命名空間宣告**

Run: `grep -rln "xmlns:behaviors=\"using:Specurai.Desktop.Behaviors\"" src/Specurai.Desktop/Views/ | wc -l`

Expected：15（每個 axaml 一次）。

- [ ] **Step 5：完整煙霧測試**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`

照 spec「手動煙霧測試」4 項全跑：
1. SqlQuery：Ctrl+C 與右鍵選單複製儲存格與整列
2. TableDetail「說明」欄編輯模式 Ctrl+C 放行
3. ColumnSearch / UsageAnalysis 的 TemplateColumn 不誤觸
4. PerformanceDiagnostics 8 Grid 抽樣驗證

任一項失敗 → 不要 commit、回到對應 Task 修正再重跑。

- [ ] **Step 6：最終 Commit（無變更時跳過）**

如果全程已逐 Task commit、此步無變更，跳過。否則：

```bash
git status
# 若有任何 leftover 修改，建立最終驗收 commit
git commit -am "chore(desktop): DataGrid 儲存格複製功能驗收完成"
```

---

## 完成標準（對照 Spec 驗收）

- [x] `DataGridCellCopyBehavior.cs` 完成、13 個單元測試全綠（Task 1+2）
- [x] 35 個 DataGrid 全數加上 `Enable="True"`（Task 3+4+5+6+7+8、Task 9 Step 3 計數）
- [x] `SqlQueryDocumentView` 程式碼減量約 50 行（Task 3）
- [x] 手動煙霧測試 4 項全通（Task 9 Step 5）
- [x] `dotnet build` 無新增 warning、`dotnet test` 全綠（Task 9 Step 1+2）
