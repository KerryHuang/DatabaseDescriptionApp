# Schema 篩選：資料表名稱 & 欄位名稱 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在「結構比對」與「Schema Migration」兩個視圖中，新增資料表名稱與欄位名稱兩個文字篩選欄位，篩選為純記憶體 LINQ 操作。

**Architecture:** Schema Compare 的篩選邏輯放在 `FilteredDifferences` computed property；Schema Migration 的篩選邏輯放在 `ApplyFilter()` 方法。兩個視圖各新增兩個 `[ObservableProperty]`（`FilterTableName`、`FilterColumnName`），移除 Migration 原有的 `FilterObjectName`。欄位名稱篩選只對 `ObjectType == Column` 的列生效，非欄位列不受影響。

**Tech Stack:** C# 12、CommunityToolkit.Mvvm source generators、Avalonia 11、xUnit、NSubstitute、FluentAssertions

---

## 檔案異動總覽

| 動作 | 檔案 |
|---|---|
| 修改 | `src/Specurai.Desktop/ViewModels/SchemaCompareDocumentViewModel.cs` |
| 修改 | `src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml` |
| 修改 | `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs` |
| 修改 | `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml` |
| 修改 | `tests/Specurai.Desktop.Tests/ViewModels/SchemaCompareDocumentViewModelTests.cs` |
| 修改 | `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs` |

---

### Task 1：Schema Compare ViewModel — 新增篩選屬性與邏輯

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SchemaCompareDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SchemaCompareDocumentViewModelTests.cs`

ObjectName 格式：
- 表格：`dbo.Orders`
- 欄位：`dbo.Orders.[CustomerName]`（欄位名稱在最後 `.[` 與 `]` 之間）

- [ ] **Step 1：撰寫失敗測試**

在 `SchemaCompareDocumentViewModelTests.cs` 中加入以下測試（找到現有的測試類別，在其中新增）：

```csharp
[Fact]
public void FilterTableName_設定關鍵字_只顯示ObjectName包含該關鍵字的差異()
{
    var vm = new SchemaCompareDocumentViewModel();
    var comparison = new SchemaComparison
    {
        BaseEnvironment = "Base",
        TargetEnvironment = "Target",
        Differences =
        [
            new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Orders", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
            new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Customers", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
        ]
    };
    vm.ComparisonResults.Add(comparison);
    vm.SelectedComparison = comparison;

    vm.FilterTableName = "Orders";

    vm.FilteredDifferences.Should().ContainSingle()
        .Which.ObjectName.Should().Be("dbo.Orders");
}

[Fact]
public void FilterColumnName_設定關鍵字_只篩選欄位列且非欄位列維持顯示()
{
    var vm = new SchemaCompareDocumentViewModel();
    var comparison = new SchemaComparison
    {
        BaseEnvironment = "Base",
        TargetEnvironment = "Target",
        Differences =
        [
            new SchemaDifference { ObjectType = SchemaObjectType.Table,  ObjectName = "dbo.Orders",                  RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[CustomerName]",   RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[OrderDate]",      RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
        ]
    };
    vm.ComparisonResults.Add(comparison);
    vm.SelectedComparison = comparison;

    vm.FilterColumnName = "Customer";

    // 表格列不受影響，欄位列只保留符合的
    vm.FilteredDifferences.Should().HaveCount(2);
    vm.FilteredDifferences.Should().Contain(d => d.ObjectType == SchemaObjectType.Table);
    vm.FilteredDifferences.Should().Contain(d => d.ObjectName == "dbo.Orders.[CustomerName]");
    vm.FilteredDifferences.Should().NotContain(d => d.ObjectName == "dbo.Orders.[OrderDate]");
}

[Fact]
public void FilterTableName與FilterColumnName_同時設定_取交集()
{
    var vm = new SchemaCompareDocumentViewModel();
    var comparison = new SchemaComparison
    {
        BaseEnvironment = "Base",
        TargetEnvironment = "Target",
        Differences =
        [
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[CustomerName]",   RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Customers.[CustomerName]", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added },
        ]
    };
    vm.ComparisonResults.Add(comparison);
    vm.SelectedComparison = comparison;

    vm.FilterTableName = "Orders";
    vm.FilterColumnName = "Customer";

    vm.FilteredDifferences.Should().ContainSingle()
        .Which.ObjectName.Should().Be("dbo.Orders.[CustomerName]");
}
```

- [ ] **Step 2：執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FilterTableName|FilterColumnName" -v minimal
```

預期：FAIL（`FilterTableName`、`FilterColumnName` 屬性不存在）

- [ ] **Step 3：實作 ViewModel**

在 `SchemaCompareDocumentViewModel.cs` 的 `#region 風險篩選` 區塊內，在現有風險篩選屬性**之後**新增：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(FilteredDifferences))]
private string _filterTableName = string.Empty;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(FilteredDifferences))]
private string _filterColumnName = string.Empty;
```

將 `FilteredDifferences` getter 替換為：

```csharp
public IReadOnlyList<SchemaDifference> FilteredDifferences
{
    get
    {
        if (SelectedComparison == null)
            return Array.Empty<SchemaDifference>();

        return SelectedComparison.Differences
            .Where(d =>
                (ShowLowRisk && d.RiskLevel == RiskLevel.Low) ||
                (ShowMediumRisk && d.RiskLevel == RiskLevel.Medium) ||
                (ShowHighRisk && d.RiskLevel == RiskLevel.High) ||
                (ShowForbidden && d.RiskLevel == RiskLevel.Forbidden))
            .Where(d => string.IsNullOrEmpty(FilterTableName) ||
                        d.ObjectName.Contains(FilterTableName, StringComparison.OrdinalIgnoreCase))
            .Where(d => string.IsNullOrEmpty(FilterColumnName) ||
                        d.ObjectType != SchemaObjectType.Column ||
                        ExtractColumnName(d.ObjectName).Contains(FilterColumnName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

private static string ExtractColumnName(string objectName)
{
    var start = objectName.LastIndexOf(".[", StringComparison.Ordinal);
    if (start < 0) return objectName;
    var end = objectName.LastIndexOf(']');
    if (end <= start + 2) return objectName;
    return objectName.Substring(start + 2, end - start - 2);
}
```

- [ ] **Step 4：執行測試確認通過**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FilterTableName|FilterColumnName" -v minimal
```

預期：PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SchemaCompareDocumentViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/SchemaCompareDocumentViewModelTests.cs
git commit -m "feat(schema-compare): 新增資料表名稱與欄位名稱篩選邏輯"
```

---

### Task 2：Schema Compare View — 新增篩選列 UI

**Files:**
- Modify: `src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml`

- [ ] **Step 1：在 AXAML 中新增篩選列**

目前 `SchemaCompareDocumentView.axaml` 的主 Grid 定義為 `RowDefinitions="Auto,*,Auto"`。
將其改為 `RowDefinitions="Auto,Auto,*,Auto"`，並將原本 `Grid.Row="1"` 的主內容區改為 `Grid.Row="2"`，原 `Grid.Row="2"` 的狀態列改為 `Grid.Row="3"`。

然後在工具列（`Grid.Row="0"`）之後、主內容區之前，插入新的篩選列（`Grid.Row="1"`）：

```xml
<!-- 篩選列 -->
<Border Grid.Row="1" Padding="10,5"
        Background="{DynamicResource SystemControlBackgroundAltHighBrush}">
    <WrapPanel Orientation="Horizontal" VerticalAlignment="Center">
        <TextBlock Text="資料表名稱：" VerticalAlignment="Center" FontSize="12" Margin="0,0,4,0"/>
        <TextBox Text="{Binding FilterTableName}"
                 Watermark="輸入資料表名稱關鍵字..."
                 Width="200" FontSize="12"
                 VerticalContentAlignment="Center"
                 Margin="0,0,20,0"/>
        <TextBlock Text="欄位名稱：" VerticalAlignment="Center" FontSize="12" Margin="0,0,4,0"/>
        <TextBox Text="{Binding FilterColumnName}"
                 Watermark="輸入欄位名稱關鍵字..."
                 Width="200" FontSize="12"
                 VerticalContentAlignment="Center"/>
    </WrapPanel>
</Border>
```

- [ ] **Step 2：建置確認無錯誤**

```bash
dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj
```

預期：Build succeeded，0 errors

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml
git commit -m "feat(schema-compare): 新增資料表名稱與欄位名稱篩選列 UI"
```

---

### Task 3：Schema Migration ViewModel — 替換篩選屬性與邏輯

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs`

- [ ] **Step 1：撰寫失敗測試**

在 `SchemaMigrationDocumentViewModelTests.cs` 中找到現有測試類別，新增：

```csharp
[Fact]
public void FilterTableName_設定關鍵字_FilteredRows只顯示ObjectName包含該關鍵字的列()
{
    var vm = new SchemaMigrationDocumentViewModel();
    vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
        new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Orders", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
    vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
        new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Customers", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

    vm.FilterTableName = "Orders";

    vm.FilteredRows.Should().ContainSingle()
        .Which.Difference.ObjectName.Should().Be("dbo.Orders");
}

[Fact]
public void FilterColumnName_設定關鍵字_欄位列篩選且非欄位列維持()
{
    var vm = new SchemaMigrationDocumentViewModel();
    vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
        new SchemaDifference { ObjectType = SchemaObjectType.Table,  ObjectName = "dbo.Orders",                RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
    vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
        new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[CustomerName]", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
    vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
        new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[OrderDate]",    RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

    vm.FilterColumnName = "Customer";

    vm.FilteredRows.Should().HaveCount(2);
    vm.FilteredRows.Should().Contain(r => r.Difference.ObjectType == SchemaObjectType.Table);
    vm.FilteredRows.Should().Contain(r => r.Difference.ObjectName == "dbo.Orders.[CustomerName]");
    vm.FilteredRows.Should().NotContain(r => r.Difference.ObjectName == "dbo.Orders.[OrderDate]");
}
```

> 注意：`SchemaMigrationDocumentViewModel` 設計時建構函式不呼叫 `SubscribeFilterEvents()`，所以測試需直接操作 `DifferenceRows` 後手動觸發 `ApplyFilter()`（或確認屬性變更會自動觸發）。若測試中 `FilteredRows` 未更新，在 ViewModel 的設計時建構函式中補呼叫 `SubscribeFilterEvents()`，或測試結尾加 `vm.ApplyFilter()` （需將 `ApplyFilter` 改為 `internal`）。

- [ ] **Step 2：執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FilterTableName|FilterColumnName" -v minimal
```

預期：新增的 Migration 測試 FAIL

- [ ] **Step 3：實作 ViewModel**

在 `SchemaMigrationDocumentViewModel.cs` 中：

1. 移除現有的 `FilterObjectName` 屬性及 `partial void OnFilterObjectNameChanged` 方法：
   - 刪除：`[ObservableProperty] private string _filterObjectName = string.Empty;`
   - 刪除：`partial void OnFilterObjectNameChanged(string value) => ApplyFilter();`

2. 在篩選屬性區塊新增兩個屬性：

```csharp
[ObservableProperty] private string _filterTableName = string.Empty;
[ObservableProperty] private string _filterColumnName = string.Empty;

partial void OnFilterTableNameChanged(string value) => ApplyFilter();
partial void OnFilterColumnNameChanged(string value) => ApplyFilter();
```

3. 將 `ApplyFilter()` 方法中的 `FilterObjectName` 篩選條件替換：

原本這段：
```csharp
if (!string.IsNullOrEmpty(FilterObjectName))
    query = query.Where(r => r.Difference.ObjectName.Contains(FilterObjectName, StringComparison.OrdinalIgnoreCase));
```

替換為：
```csharp
if (!string.IsNullOrEmpty(FilterTableName))
    query = query.Where(r => r.Difference.ObjectName.Contains(FilterTableName, StringComparison.OrdinalIgnoreCase));

if (!string.IsNullOrEmpty(FilterColumnName))
    query = query.Where(r =>
        r.Difference.ObjectType != SchemaObjectType.Column ||
        ExtractColumnName(r.Difference.ObjectName).Contains(FilterColumnName, StringComparison.OrdinalIgnoreCase));
```

4. 在 `SchemaMigrationDocumentViewModel` 類別中新增輔助方法（與 SchemaCompare 相同邏輯）：

```csharp
private static string ExtractColumnName(string objectName)
{
    var start = objectName.LastIndexOf(".[", StringComparison.Ordinal);
    if (start < 0) return objectName;
    var end = objectName.LastIndexOf(']');
    if (end <= start + 2) return objectName;
    return objectName.Substring(start + 2, end - start - 2);
}
```

5. 同時更新 `ClearFilters()` 方法，將 `FilterObjectName = string.Empty;` 替換為：

```csharp
FilterTableName = string.Empty;
FilterColumnName = string.Empty;
```

- [ ] **Step 4：執行測試確認通過**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FilterTableName|FilterColumnName" -v minimal
```

預期：PASS

- [ ] **Step 5：執行全部測試確認無回歸**

```bash
dotnet test --verbosity minimal
```

預期：所有測試通過

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs
git commit -m "feat(schema-migration): 以資料表名稱與欄位名稱篩選取代物件名稱篩選"
```

---

### Task 4：Schema Migration View — 替換篩選列 UI

**Files:**
- Modify: `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`

- [ ] **Step 1：替換 TextBox**

找到現有的 `FilterObjectName` TextBox：

```xml
<TextBox Text="{Binding FilterObjectName}" Width="150" FontSize="12"
         Watermark="搜尋物件名稱…" VerticalContentAlignment="Center"
         Margin="0,0,8,0"/>
```

替換為：

```xml
<TextBlock Text="資料表名稱：" VerticalAlignment="Center" FontSize="12" Margin="0,0,4,0"/>
<TextBox Text="{Binding FilterTableName}"
         Watermark="輸入資料表名稱關鍵字..."
         Width="180" FontSize="12"
         VerticalContentAlignment="Center"
         Margin="0,0,16,0"/>
<TextBlock Text="欄位名稱：" VerticalAlignment="Center" FontSize="12" Margin="0,0,4,0"/>
<TextBox Text="{Binding FilterColumnName}"
         Watermark="輸入欄位名稱關鍵字..."
         Width="180" FontSize="12"
         VerticalContentAlignment="Center"
         Margin="0,0,8,0"/>
```

- [ ] **Step 2：建置確認無錯誤**

```bash
dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj
```

預期：Build succeeded，0 errors

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml
git commit -m "feat(schema-migration): 更新篩選列 UI 為資料表名稱與欄位名稱兩個欄位"
```

---

### Task 5：最終驗證

- [ ] **Step 1：執行完整測試**

```bash
dotnet test --verbosity minimal
```

預期：所有測試通過，無回歸

- [ ] **Step 2：建置整個方案**

```bash
dotnet build
```

預期：Build succeeded，0 errors，0 warnings（與原本相同）
