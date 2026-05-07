# Schema Migration Schema 篩選功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Schema Migration 功能中加入 Schema 欄位及篩選器，讓使用者可依 dbo / Sales / Production 等 Schema 過濾差異清單，同時修正 SQL 產生器依賴字串解析 Schema 名稱所造成的潛在 bug（非 dbo Schema 的資料表建立失敗）。

**Architecture:** 在 Domain 的 `SchemaDifference` 加入 `Schema` 一等屬性；Application 層的 `SchemaCompareService` 填入該屬性，`SqlScriptGenerator` 改用 `diff.Schema` 取代字串解析；Desktop 層的 ViewModel 動態建立 Schema 多選篩選器並在 DataGrid 加入 Schema 欄。

**Tech Stack:** C# / .NET 8, xUnit, NSubstitute, FluentAssertions, CommunityToolkit.Mvvm, Avalonia 11

---

## 受影響檔案對照

| 層 | 動作 | 路徑 |
|---|---|---|
| Domain | **修改** | `src/Specurai.Domain/Entities/SchemaCompare/SchemaDifference.cs` |
| Application | **修改** | `src/Specurai.Application/Services/SchemaCompareService.cs` |
| Application | **修改** | `src/Specurai.Application/Services/SqlScriptGenerator.cs` |
| Desktop | **修改** | `src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs` |
| Desktop | **修改** | `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs` |
| Desktop | **修改** | `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml` |
| Test | **修改** | `tests/Specurai.Application.Tests/Services/SchemaCompareServiceTests.cs` |
| Test | **新增** | `tests/Specurai.Infrastructure.Tests/Services/SqlScriptGeneratorTests.cs` |
| Test | **修改** | `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs` |

---

## Task 1：`SchemaDifference` 加入 `Schema` 屬性

**Files:**
- Modify: `src/Specurai.Domain/Entities/SchemaCompare/SchemaDifference.cs`
- Test: `tests/Specurai.Application.Tests/Services/SchemaCompareServiceTests.cs`

- [ ] **Step 1：在 `SchemaDifference` 加入 `Schema` 屬性**

開啟 `src/Specurai.Domain/Entities/SchemaCompare/SchemaDifference.cs`，在 `ObjectName` 屬性下方加入：

```csharp
/// <summary>
/// Schema 名稱（例如 dbo、Sales、Production）
/// </summary>
public string Schema { get; set; } = "dbo";
```

完整類別結果（只顯示新增部分，其餘不動）：

```csharp
/// <summary>
/// 物件名稱
/// </summary>
public string ObjectName { get; set; } = string.Empty;

/// <summary>
/// Schema 名稱（例如 dbo、Sales、Production）
/// </summary>
public string Schema { get; set; } = "dbo";

/// <summary>
/// 差異類型
/// </summary>
public DifferenceType DifferenceType { get; set; }
```

- [ ] **Step 2：確認現有測試仍可通過**

```bash
dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj
```

預期：全數通過（Schema 有預設值 `"dbo"`，不影響現有測試）。

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Domain/Entities/SchemaCompare/SchemaDifference.cs
git commit -m "feat(domain): SchemaDifference 加入 Schema 屬性"
```

---

## Task 2：`SchemaCompareService` 填入 `Schema`

**Files:**
- Modify: `src/Specurai.Application/Services/SchemaCompareService.cs`
- Test: `tests/Specurai.Application.Tests/Services/SchemaCompareServiceTests.cs`

- [ ] **Step 1：撰寫失敗測試**

在 `SchemaCompareServiceTests.cs` 的 `#region 基本比對測試` 區塊末尾加入以下兩個測試：

```csharp
[Fact]
public async Task CompareAsync_非dbo表格差異_Schema應正確填入()
{
    // Arrange
    var baseSchema = CreateTestSchema("基準環境");
    baseSchema.Tables.Add(new SchemaTable { Schema = "Sales", Name = "Orders" });
    var targetSchema = CreateTestSchema("目標環境");

    // Act
    var result = await _service.CompareAsync(baseSchema, targetSchema);

    // Assert
    var diff = result.Differences.Should().ContainSingle().Subject;
    diff.Schema.Should().Be("Sales");
}

[Fact]
public async Task CompareAsync_非dbo程式物件差異_Schema應正確填入()
{
    // Arrange
    var baseSchema = CreateTestSchema("基準環境");
    baseSchema.Views.Add(new SchemaProgramObject
    {
        Schema = "Production",
        Name = "vw_WorkOrder",
        ObjectType = ProgramObjectType.View,
        Definition = "CREATE VIEW [Production].[vw_WorkOrder] AS SELECT 1 AS Id"
    });
    var targetSchema = CreateTestSchema("目標環境");

    // Act
    var result = await _service.CompareAsync(baseSchema, targetSchema);

    // Assert
    var diff = result.Differences.Should().ContainSingle().Subject;
    diff.Schema.Should().Be("Production");
}
```

- [ ] **Step 2：執行測試確認失敗**

```bash
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "Schema應正確填入"
```

預期：FAIL（Schema 尚未填入，值仍為預設 `"dbo"`）。

- [ ] **Step 3：更新 `SchemaCompareService` 填入 `Schema`**

**表格差異**（`CompareTables` 方法中所有 `comparison.Differences.Add(new SchemaDifference { ... })` 呼叫）：

找到 `CompareTables` 方法，四處 `new SchemaDifference` 加入 `Schema = baseTable.Schema,` 或 `Schema = targetTable.Schema,`：

```csharp
// 基準有，目標沒有
comparison.Differences.Add(new SchemaDifference
{
    ObjectType = SchemaObjectType.Table,
    ObjectName = baseTable.FullName,
    Schema = baseTable.Schema,      // ← 新增
    DifferenceType = DifferenceType.Added,
    RiskLevel = RiskLevel.Low,
    Description = $"表格 {baseTable.FullName} 不存在於目標環境，需要新增"
});

// 目標有，基準沒有
comparison.Differences.Add(new SchemaDifference
{
    ObjectType = SchemaObjectType.Table,
    ObjectName = targetTable.FullName,
    Schema = targetTable.Schema,    // ← 新增
    DifferenceType = DifferenceType.Added,
    RiskLevel = RiskLevel.Low,
    Description = $"表格 {targetTable.FullName} 不存在於基準環境，基準需要新增"
});
```

**欄位差異**（`CompareColumns` 方法）：所有 `new SchemaDifference` 加入 `Schema = table.Schema,`（`table` 是 `baseTable` 參數）：

在 `CompareColumns(SchemaTable baseTable, SchemaTable targetTable, SchemaComparison comparison)` 中，每個 `new SchemaDifference { ObjectType = SchemaObjectType.Column, ... }` 加入：

```csharp
Schema = baseTable.Schema,  // ← 新增
```

**索引差異**（`CompareIndexes` 方法）：同上，Schema 來自 `baseTable.Schema`（或 `targetTable.Schema`）：

```csharp
Schema = baseTable.Schema,  // ← 新增
```

**約束差異**（`CompareConstraints` 方法）：同上：

```csharp
Schema = baseTable.Schema,  // ← 新增
```

**程式物件差異**（`CompareProgramObjects` 方法）：

```csharp
// 基準有，目標沒有
comparison.Differences.Add(new SchemaDifference
{
    ObjectType = objectType,
    ObjectName = baseObj.FullName,
    Schema = baseObj.Schema,        // ← 新增
    DifferenceType = DifferenceType.Added,
    RiskLevel = RiskLevel.Low,
    Description = $"{objectType} {baseObj.FullName} 不存在於目標環境"
});

// 目標有，基準沒有（若有）
Schema = targetObj.Schema,          // ← 新增

// 定義不同
comparison.Differences.Add(new SchemaDifference
{
    ObjectType = objectType,
    ObjectName = baseObj.FullName,
    Schema = baseObj.Schema,        // ← 新增
    DifferenceType = DifferenceType.Modified,
    ...
});
```

- [ ] **Step 4：執行測試確認通過**

```bash
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "Schema應正確填入"
```

預期：PASS。

- [ ] **Step 5：執行全部 Application 測試**

```bash
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj
```

預期：全數通過。

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Application/Services/SchemaCompareService.cs
git add tests/Specurai.Application.Tests/Services/SchemaCompareServiceTests.cs
git commit -m "feat(application): SchemaCompareService 填入 SchemaDifference.Schema"
```

---

## Task 3：`SqlScriptGenerator` 改用 `diff.Schema` 取代字串解析

**Files:**
- Modify: `src/Specurai.Application/Services/SqlScriptGenerator.cs`
- Create: `tests/Specurai.Infrastructure.Tests/Services/SqlScriptGeneratorTests.cs`

> **注意**：`SqlScriptGenerator` 在 Application 層，測試應放在 `Specurai.Application.Tests`（或獨立的 Infrastructure.Tests 皆可，但此處放 Application.Tests 較符合分層）。實際上現在的測試是在 Infrastructure.Tests 中沒有此類測試。我們在 `Specurai.Application.Tests` 新增。

- [ ] **Step 1：撰寫失敗測試**

在 `tests/Specurai.Application.Tests/Services/` 新增 `SqlScriptGeneratorTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Application.Tests.Services;

/// <summary>
/// SqlScriptGenerator 測試
/// </summary>
public class SqlScriptGeneratorTests
{
    private readonly SqlScriptGenerator _sut = new();

    private static DatabaseSchema CreateBaseSchema(SchemaTable table)
    {
        var schema = new DatabaseSchema { ConnectionName = "基準" };
        schema.Tables.Add(table);
        return schema;
    }

    [Fact]
    public void Generate_非dbo表格_應使用正確Schema建立()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "Sales",
            Name = "Orders",
            Columns =
            [
                new SchemaColumn { Name = "Id", DataType = "int", IsNullable = false, IsIdentity = true }
            ]
        };
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[Sales].[Orders]",
            Schema = "Sales",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };
        var baseSchema = CreateBaseSchema(table);

        // Act
        var script = _sut.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("CREATE TABLE [Sales].[Orders]");
        script.ApplyScript.Should().NotContain("CREATE TABLE [dbo].[Orders]");
    }

    [Fact]
    public void Generate_非dbo欄位_應使用正確Schema的ALTER_TABLE()
    {
        // Arrange
        var table = new SchemaTable
        {
            Schema = "Production",
            Name = "WorkOrder",
            Columns =
            [
                new SchemaColumn { Name = "Id", DataType = "int", IsNullable = false },
                new SchemaColumn { Name = "Remark", DataType = "nvarchar", MaxLength = 200, IsNullable = true }
            ]
        };
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[Production].[WorkOrder].[Remark]",
            Schema = "Production",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };
        var baseSchema = CreateBaseSchema(table);

        // Act
        var script = _sut.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [Production].[WorkOrder]");
        script.ApplyScript.Should().NotContain("ALTER TABLE [dbo].[WorkOrder]");
    }
}
```

- [ ] **Step 2：執行測試確認失敗**

```bash
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "SqlScriptGeneratorTests"
```

預期：PASS（因為現有的 `ParseTwoParts` 解析 `[Sales].[Orders]` 仍然正確）。

> 若測試通過，代表字串解析在這些情況下已夠用。我們仍要繼續替換，因為 `Schema` 明確存在時優先使用更健壯。

- [ ] **Step 3：重構 `SqlScriptGenerator`，優先使用 `diff.Schema`**

開啟 `src/Specurai.Application/Services/SqlScriptGenerator.cs`。

**修改 `GenerateTableSql`**：

原本：
```csharp
private static string GenerateTableSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    if (diff.DifferenceType != DifferenceType.Added)
        return string.Empty;

    var (schema, tableName) = ParseTwoParts(diff.ObjectName);
    var table = baseSchema.GetTable(schema, tableName);
```

改為：
```csharp
private static string GenerateTableSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    if (diff.DifferenceType != DifferenceType.Added)
        return string.Empty;

    var (_, tableName) = ParseTwoParts(diff.ObjectName);
    var schema = string.IsNullOrEmpty(diff.Schema) ? ParseTwoParts(diff.ObjectName).schema : diff.Schema;
    var table = baseSchema.GetTable(schema, tableName);
```

**修改 `GenerateColumnSql`**：

原本：
```csharp
private static string GenerateColumnSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    var (schema, tableName, columnName) = ParseThreeParts(diff.ObjectName);
    var table = baseSchema.GetTable(schema, tableName);
```

改為：
```csharp
private static string GenerateColumnSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    var (parsedSchema, tableName, columnName) = ParseThreeParts(diff.ObjectName);
    var schema = string.IsNullOrEmpty(diff.Schema) ? parsedSchema : diff.Schema;
    var table = baseSchema.GetTable(schema, tableName);
```

並在後面用 `schema` 的地方保持一致（`ALTER TABLE [{schema}].[{tableName}]`）。

**修改 `GenerateIndexSql`**：

```csharp
private static string GenerateIndexSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    if (diff.DifferenceType != DifferenceType.Added)
        return string.Empty;

    var (parsedSchema, tableName, indexName) = ParseThreeParts(diff.ObjectName);
    var schema = string.IsNullOrEmpty(diff.Schema) ? parsedSchema : diff.Schema;
    var table = baseSchema.GetTable(schema, tableName);
```

**修改 `GenerateConstraintSql`**：

```csharp
private static string GenerateConstraintSql(SchemaDifference diff, DatabaseSchema baseSchema)
{
    if (diff.DifferenceType != DifferenceType.Added)
        return string.Empty;

    var (parsedSchema, tableName, constraintName) = ParseThreeParts(diff.ObjectName);
    var schema = string.IsNullOrEmpty(diff.Schema) ? parsedSchema : diff.Schema;
    var table = baseSchema.GetTable(schema, tableName);
```

**修改 `GenerateProgramObjectSql`**：

```csharp
private static string GenerateProgramObjectSql(
    SchemaDifference diff,
    Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> lookup)
{
    var (parsedSchema, objName) = ParseTwoParts(diff.ObjectName);
    var schema = string.IsNullOrEmpty(diff.Schema) ? parsedSchema : diff.Schema;

    if (!lookup.TryGetValue((schema, objName, diff.ObjectType), out var obj) || obj.Definition == null)
        return $"-- 無法找到物件定義：{diff.ObjectName}";
```

- [ ] **Step 4：執行測試確認通過**

```bash
dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj
```

預期：全數通過。

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Application/Services/SqlScriptGenerator.cs
git add tests/Specurai.Application.Tests/Services/SqlScriptGeneratorTests.cs
git commit -m "fix(application): SqlScriptGenerator 優先使用 diff.Schema 取代字串解析"
```

---

## Task 4：`MigrationDifferenceRowViewModel` 加入 `SchemaText`

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs`

- [ ] **Step 1：加入 `SchemaText` computed property**

開啟 `src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs`，在 `ObjectTypeText` 上方加入：

```csharp
public string SchemaText => Difference.Schema;
```

- [ ] **Step 2：執行 Desktop 測試確認不破壞現有測試**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj
```

預期：全數通過。

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs
git commit -m "feat(desktop): MigrationDifferenceRowViewModel 加入 SchemaText 屬性"
```

---

## Task 5：`SchemaMigrationDocumentViewModel` 加入 Schema 篩選器

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs`
- Modify: `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs`

- [ ] **Step 1：撰寫失敗測試**

在 `SchemaMigrationDocumentViewModelTests.cs` 加入：

```csharp
[Fact]
public void SchemaFilters_初始狀態_應為空()
{
    // Arrange
    _connectionManager.GetAllProfiles().Returns([]);

    // Act
    var vm = new SchemaMigrationDocumentViewModel(
        _migrationService, _scriptGenerator, _executor, _connectionManager);

    // Assert
    vm.SchemaFilters.Should().BeEmpty();
}

[Fact]
public void SchemaFilters_分析後有非dbo差異_應包含對應Schema選項()
{
    // Arrange
    _connectionManager.GetAllProfiles().Returns([]);
    var vm = new SchemaMigrationDocumentViewModel(
        _migrationService, _scriptGenerator, _executor, _connectionManager);

    var diffs = new List<SchemaDifference>
    {
        new() { ObjectType = SchemaObjectType.Table, ObjectName = "[dbo].[Users]", Schema = "dbo",
                DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
        new() { ObjectType = SchemaObjectType.Table, ObjectName = "[Sales].[Orders]", Schema = "Sales",
                DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low }
    };

    var analysis = new MigrationAnalysis
    {
        BaseSchema = new DatabaseSchema { ConnectionName = "基準" },
        TargetSchema = new DatabaseSchema { ConnectionName = "目標" },
        Comparison = new SchemaComparison
        {
            BaseEnvironment = "基準",
            TargetEnvironment = "目標",
            ComparedAt = DateTime.Now,
            Differences = diffs
        }
    };
    _migrationService.AnalyzeAsync(
        Arg.Any<string>(), Arg.Any<string>(),
        Arg.Any<string>(), Arg.Any<string>(),
        Arg.Any<CancellationToken>()).Returns(analysis);

    // 設定連線 profile
    var baseProfile = new ConnectionProfile { Name = "基準", Server = "s", Database = "db" };
    var targetProfile = new ConnectionProfile { Name = "目標", Server = "s", Database = "db2" };
    _connectionManager.GetAllProfiles().Returns([baseProfile, targetProfile]);
    _connectionManager.GetConnectionString(baseProfile.Id).Returns("Server=s;Database=db");
    _connectionManager.GetConnectionString(targetProfile.Id).Returns("Server=s;Database=db2");

    var vm = new SchemaMigrationDocumentViewModel(
        _migrationService, _scriptGenerator, _executor, _connectionManager);
    vm.SelectedBaseProfile = vm.ConnectionProfiles[0];
    vm.SelectedTargetProfile = vm.ConnectionProfiles[1];

    // Act
    vm.AnalyzeCommand.ExecuteAsync(null).GetAwaiter().GetResult();

    // Assert
    vm.SchemaFilters.Should().HaveCount(2);
    vm.SchemaFilters.Select(f => f.Label).Should().Contain("dbo");
    vm.SchemaFilters.Select(f => f.Label).Should().Contain("Sales");
}
```

- [ ] **Step 2：執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "SchemaFilters"
```

預期：FAIL（`vm.SchemaFilters` 屬性不存在）。

- [ ] **Step 3：在 ViewModel 加入 Schema 篩選器屬性與邏輯**

開啟 `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs`。

**（a）在現有篩選屬性區塊加入 SchemaFilters：**

找到：
```csharp
[ObservableProperty] private IReadOnlyList<FilterOptionViewModel> _differenceTypeFilters = [];
```

在其後加入：
```csharp
[ObservableProperty] private IReadOnlyList<FilterOptionViewModel> _schemaFilters = [];
```

找到：
```csharp
[ObservableProperty] private string _differenceTypeFilterLabel = "差異類型 ▾";
```

在其後加入：
```csharp
[ObservableProperty] private string _schemaFilterLabel = "Schema ▾";
```

**（b）加入 `RebuildSchemaFilters` 方法**（放在 `RebuildDifferenceTypeFilters` 方法旁）：

```csharp
private void RebuildSchemaFilters()
{
    var labels = DifferenceRows
        .Select(r => r.SchemaText)
        .Distinct()
        .OrderBy(x => x)
        .ToArray();
    SchemaFilters = CreateFilters(labels);
    foreach (var f in SchemaFilters)
        f.SelectionChanged += _ => ApplyFilter();
    SchemaFilterLabel = "Schema ▾";
}
```

**（c）在 `AnalyzeAsync` 中呼叫 `RebuildSchemaFilters`**：

找到 `RebuildDifferenceTypeFilters()` 的呼叫，在其後加入：

```csharp
RebuildDifferenceTypeFilters();
RebuildSchemaFilters();   // ← 新增
ApplyFilter();
```

**（d）在 `SubscribeFilterEvents` 中不需要變更**（SchemaFilters 在 Rebuild 時已訂閱）。

**（e）在 `ApplyFilter` 加入 Schema 篩選邏輯**：

找到：
```csharp
private void ApplyFilter()
{
    var activeRisk = RiskLevelFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
    var activeType = ObjectTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
    var activeDiff = DifferenceTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();

    RiskFilterLabel = activeRisk.Count == 0 ? "風險 ▾" : $"風險（{activeRisk.Count}）▾";
    ObjectTypeFilterLabel = activeType.Count == 0 ? "物件類型 ▾" : $"物件類型（{activeType.Count}）▾";
    DifferenceTypeFilterLabel = activeDiff.Count == 0 ? "差異類型 ▾" : $"差異類型（{activeDiff.Count}）▾";
```

改為：
```csharp
private void ApplyFilter()
{
    var activeRisk = RiskLevelFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
    var activeType = ObjectTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
    var activeDiff = DifferenceTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
    var activeSchema = SchemaFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();

    RiskFilterLabel = activeRisk.Count == 0 ? "風險 ▾" : $"風險（{activeRisk.Count}）▾";
    ObjectTypeFilterLabel = activeType.Count == 0 ? "物件類型 ▾" : $"物件類型（{activeType.Count}）▾";
    DifferenceTypeFilterLabel = activeDiff.Count == 0 ? "差異類型 ▾" : $"差異類型（{activeDiff.Count}）▾";
    SchemaFilterLabel = activeSchema.Count == 0 ? "Schema ▾" : $"Schema（{activeSchema.Count}）▾";
```

接著在篩選查詢中加入 Schema 篩選（放在 `activeDiff` 篩選後）：

找到：
```csharp
        if (activeDiff.Count > 0)
            query = query.Where(r => activeDiff.Contains(r.DifferenceTypeText));
```

在其後加入：
```csharp
        if (activeSchema.Count > 0)
            query = query.Where(r => activeSchema.Contains(r.SchemaText));
```

**（f）在 `ClearFilters` 中加入 Schema 篩選清除**：

找到：
```csharp
foreach (var f in RiskLevelFilters.Concat(ObjectTypeFilters).Concat(DifferenceTypeFilters))
    f.IsSelected = false;
```

改為：
```csharp
foreach (var f in RiskLevelFilters.Concat(ObjectTypeFilters).Concat(DifferenceTypeFilters).Concat(SchemaFilters))
    f.IsSelected = false;
```

- [ ] **Step 4：執行測試確認通過**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "SchemaFilters"
```

預期：PASS。

- [ ] **Step 5：執行全部 Desktop 測試**

```bash
dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj
```

預期：全數通過。

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs
git add tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs
git commit -m "feat(desktop): SchemaMigrationDocumentViewModel 加入 Schema 篩選器"
```

---

## Task 6：View 加入 Schema 欄位與篩選下拉

**Files:**
- Modify: `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`

- [ ] **Step 1：在 DataGrid 加入 Schema 欄（放在「物件類型」欄後方、「物件名稱」欄前方）**

開啟 `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`。

找到：
```xml
<!-- 物件名稱 -->
<DataGridTextColumn Header="物件名稱"
                    Binding="{Binding Difference.ObjectName}"
                    Width="220"
                    IsReadOnly="True"/>
```

在其前方插入：
```xml
<!-- Schema -->
<DataGridTextColumn Header="Schema"
                    Binding="{Binding SchemaText}"
                    Width="90"
                    IsReadOnly="True"/>
```

- [ ] **Step 2：在篩選列加入 Schema 多選下拉**

找到差異類型多選的 `</StackPanel>` 結束標籤（第 169 行附近）：

```xml
                <!-- 差異類型多選 -->
                <StackPanel Orientation="Horizontal" Margin="0,0,8,0">
                    ...
                </StackPanel>
```

在差異類型多選的 `</StackPanel>` 後、`<Button Command="{Binding ClearFiltersCommand}"` 前插入：

```xml
                <!-- Schema 多選 -->
                <StackPanel Orientation="Horizontal" Margin="0,0,8,0">
                    <ToggleButton x:Name="SchemaFilterBtn"
                                  Content="{Binding SchemaFilterLabel}"
                                  FontSize="12" Padding="6,2"/>
                    <Popup IsOpen="{Binding #SchemaFilterBtn.IsChecked}"
                           PlacementTarget="{Binding #SchemaFilterBtn}"
                           Placement="Bottom" IsLightDismissEnabled="True">
                        <Border Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}"
                                BorderBrush="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                                BorderThickness="1" Padding="4">
                            <ItemsControl ItemsSource="{Binding SchemaFilters}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="vm:FilterOptionViewModel">
                                        <CheckBox IsChecked="{Binding IsSelected}" Content="{Binding Label}"
                                                  Padding="4,2" FontSize="12"/>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </Border>
                    </Popup>
                </StackPanel>
```

- [ ] **Step 3：建置確認無編譯錯誤**

```bash
dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj
```

預期：建置成功，無錯誤。

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml
git commit -m "feat(desktop): Schema Migration 加入 Schema 欄位與篩選下拉"
```

---

## Task 7：執行全部測試並收尾

- [ ] **Step 1：執行全部測試**

```bash
dotnet test
```

預期：全數通過。

- [ ] **Step 2：建置確認**

```bash
dotnet build
```

預期：建置成功。
