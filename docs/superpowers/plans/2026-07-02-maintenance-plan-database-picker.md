# 維護計劃目標／還原資料庫改為伺服器資料庫下拉 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 維護計劃精靈步驟1的「目標資料庫」改為唯讀下拉（只能選伺服器上既有庫）、「還原資料庫」改為可編輯下拉（可挑可打），清單取自目前連線伺服器的使用者資料庫。

**Architecture:** 重用既有 `IDatabaseInfoRepository.GetDatabaseNamesAsync()` 取清單；於 Application 層 `IMaintenancePlanService` 新增 `GetServerDatabasesAsync` 委派它作為 ViewModel 的閘道（VM 不直接碰 repository）；ViewModel 新增 `AvailableDatabases` 集合與開啟時載入方法；View 以 `ComboBox`（目標）與 `AutoCompleteBox`（還原）取代原 `TextBox`。

**Tech Stack:** .NET 8、Clean Architecture、CommunityToolkit.Mvvm、Avalonia 11（Semi.Avalonia）、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- UI 文字、註解、Commit 訊息一律**繁體中文**。
- 遵守 Clean Architecture 分層：Application 只相依 Domain；Desktop 相依 Application/Domain；ViewModel 不含查詢邏輯（透過服務介面）。
- ViewModel 使用 CommunityToolkit.Mvvm；每個 ViewModel 需有無參數設計時建構函式與 DI 建構函式。
- 檔案 UTF-8 無 BOM。
- 測試命名 `[方法]_[條件]_[預期]`（繁體中文）。
- **重用** `IDatabaseInfoRepository.GetDatabaseNamesAsync()`（`SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name`）；**不新增** Domain／Infrastructure 方法，不改其過濾條件。
- 目標資料庫 `ComboBox`：只能選、不可打字；還原資料庫 `AutoCompleteBox`：可挑可打、允許輸入不存在的庫名。
- 既有 `OnDatabaseNameChanged` 自動帶 `{目標}-test` 行為必須保留。

**前置備註：** 步驟1的兩個欄位標籤已於本功能開始前改名（「資料庫名稱」→「目標資料庫」、「測試資料庫名稱」→「還原資料庫」），此變更目前在工作區未提交，Task 3 建立於其上。

---

### Task 1: Application 層新增 `GetServerDatabasesAsync`

**Files:**
- Modify: `src/Specurai.Application/Services/IMaintenancePlanService.cs`
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`
- Test: `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs`

**Interfaces:**
- Consumes: `IDatabaseInfoRepository.GetDatabaseNamesAsync(CancellationToken)`（已存在，回傳 `Task<IReadOnlyList<string>>`）。
- Produces: `IMaintenancePlanService.GetServerDatabasesAsync(CancellationToken)` → `Task<IReadOnlyList<string>>`，供 Task 2 的 ViewModel 呼叫。

- [ ] **Step 1: Write the failing test**

在 `MaintenancePlanServiceTests.cs` 適當位置（可於檔尾 `#region` 前）新增測試方法：

```csharp
    #region GetServerDatabasesAsync

    [Fact]
    public async Task GetServerDatabasesAsync_有資料庫_應回傳repository清單()
    {
        // Arrange
        var expected = new List<string> { "AlphaDB", "BetaDB" };
        _dbInfoRepo.GetDatabaseNamesAsync(Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = await _sut.GetServerDatabasesAsync();

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    #endregion
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~GetServerDatabasesAsync_有資料庫_應回傳repository清單"`
Expected: 編譯失敗（`IMaintenancePlanService` 尚無 `GetServerDatabasesAsync`）。

- [ ] **Step 3: 於介面新增方法**

在 `IMaintenancePlanService.cs` 的 `GetRecoveryModelAsync` 宣告後新增：

```csharp
    /// <summary>
    /// 取得目前連線伺服器上的使用者資料庫名稱清單（供維護計劃選取目標／還原資料庫）
    /// </summary>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<string>> GetServerDatabasesAsync(CancellationToken ct = default);
```

- [ ] **Step 4: 於實作委派 repository**

在 `MaintenancePlanService.cs` 的 `GetRecoveryModelAsync` 實作後新增：

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetServerDatabasesAsync(CancellationToken ct = default)
        => _dbInfoRepo.GetDatabaseNamesAsync(ct);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~GetServerDatabasesAsync"`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Application/Services/IMaintenancePlanService.cs src/Specurai.Application/Services/MaintenancePlanService.cs tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "feat: 維護計劃服務新增 GetServerDatabasesAsync 取得伺服器資料庫清單"
```

---

### Task 2: ViewModel 載入資料庫清單並移除唯讀切換

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelDatabaseTests.cs`（新檔）

**Interfaces:**
- Consumes: `IMaintenancePlanService.GetServerDatabasesAsync()`（Task 1 產出）。
- Produces: `ObservableCollection<string> AvailableDatabases`（供 Task 3 View 綁定）；`public Task LoadAvailableDatabasesAsync()`。

- [ ] **Step 1: Write the failing tests**

建立 `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelDatabaseTests.cs`：

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelDatabaseTests
{
    private static MaintenancePlanDocumentViewModel Build(IMaintenancePlanService plan)
    {
        var job = Substitute.For<IAgentJobService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        var conn = Substitute.For<IConnectionManager>();
        var backup = Substitute.For<IBackupService>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_有資料庫_應填入清單()
    {
        // Arrange
        var plan = Substitute.For<IMaintenancePlanService>();
        plan.GetServerDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "AlphaDB", "BetaDB" });
        var vm = Build(plan);

        // Act
        await vm.LoadAvailableDatabasesAsync();

        // Assert
        vm.AvailableDatabases.Should().BeEquivalentTo("AlphaDB", "BetaDB");
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_查詢拋例外_清單維持空且不丟例外()
    {
        // Arrange
        var plan = Substitute.For<IMaintenancePlanService>();
        plan.GetServerDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new System.Exception("boom"));
        var vm = Build(plan);

        // Act
        var act = async () => await vm.LoadAvailableDatabasesAsync();

        // Assert
        await act.Should().NotThrowAsync();
        vm.AvailableDatabases.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_設計時建構函式無服務_不丟例外且清單空()
    {
        // Arrange
        var vm = new MaintenancePlanDocumentViewModel();

        // Act
        var act = async () => await vm.LoadAvailableDatabasesAsync();

        // Assert
        await act.Should().NotThrowAsync();
        vm.AvailableDatabases.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelDatabaseTests"`
Expected: 編譯失敗（`AvailableDatabases`、`LoadAvailableDatabasesAsync` 尚不存在）。

- [ ] **Step 3: 新增集合屬性**

在 `MaintenancePlanDocumentViewModel.cs` 的「精靈集合」region（`ExecutionLog` 之後）新增：

```csharp
    /// <summary>目前連線伺服器上的使用者資料庫清單（供目標／還原資料庫下拉）</summary>
    public ObservableCollection<string> AvailableDatabases { get; } = [];
```

- [ ] **Step 4: 新增載入方法**

在「伺服器路徑瀏覽」region 內、`DetectServerPlatformAsync` 之後新增：

```csharp
    /// <summary>載入目前連線伺服器上的資料庫清單，供下拉選取。</summary>
    public async Task LoadAvailableDatabasesAsync()
    {
        if (_planService == null) return;

        try
        {
            var databases = await _planService.GetServerDatabasesAsync();
            AvailableDatabases.Clear();
            foreach (var db in databases)
                AvailableDatabases.Add(db);
        }
        catch
        {
            // 載入失敗維持空清單，欄位仍可（還原欄）手動輸入
        }
    }
```

- [ ] **Step 5: 於 DI 建構函式尾端 fire-and-forget 載入**

在 DI 建構函式中，既有兩行之後新增第三行（順序在設定 `DatabaseName` 預設值之後）：

```csharp
        // 進入頁面時自動載入 Job 清單
        _ = LoadJobsAsync();
        _ = DetectServerPlatformAsync();
        _ = LoadAvailableDatabasesAsync();
```

- [ ] **Step 6: 移除 `IsDatabaseNameEditable`**

刪除下列屬性（`SelectedPlatform` 附近）：

```csharp
    /// <summary>資料庫名稱是否可編輯（平台為「其他」時允許手動輸入）</summary>
    public bool IsDatabaseNameEditable => SelectedPlatform == "其他";
```

並於 `OnSelectedPlatformChanged` 內移除對它的通知（若存在）：

```csharp
        OnPropertyChanged(nameof(IsDatabaseNameEditable));
```

（保留 `OnPropertyChanged(nameof(IsPathCustom));`。`IsPathCustom` 仍供路徑欄唯讀綁定使用，不可移除。）

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelDatabaseTests"`
Expected: 3 個測試 PASS。

- [ ] **Step 8: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelDatabaseTests.cs
git commit -m "feat: 維護計劃 ViewModel 載入伺服器資料庫清單並移除資料庫名稱唯讀切換"
```

---

### Task 3: View 以下拉取代文字欄位

**Files:**
- Modify: `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml`

**Interfaces:**
- Consumes: `AvailableDatabases`、`DatabaseName`、`TestDatabaseName`（既有／Task 2 產出）。
- Produces: 無（純 UI）。

**備註：** 標籤文字「目標資料庫」「還原資料庫」已於工作區改好；本 Task 只替換輸入控件。移除對 `IsDatabaseNameEditable` 的綁定（Task 2 已刪該屬性，若殘留綁定會導致執行時繫結錯誤）。

- [ ] **Step 1: 目標資料庫 `TextBox` → `ComboBox`**

在 `MaintenancePlanDocumentView.axaml` 第一列，將：

```xml
                                <TextBlock Text="目標資料庫"/>
                                <TextBox Text="{Binding DatabaseName}" IsReadOnly="{Binding !IsDatabaseNameEditable}"
                                         Watermark="自動從目前連線取得"/>
```

替換為：

```xml
                                <TextBlock Text="目標資料庫"/>
                                <ComboBox ItemsSource="{Binding AvailableDatabases}"
                                          SelectedItem="{Binding DatabaseName}"
                                          HorizontalAlignment="Stretch"
                                          PlaceholderText="從伺服器選擇資料庫"/>
```

- [ ] **Step 2: 還原資料庫 `TextBox` → `AutoCompleteBox`**

在第三列，將：

```xml
                            <TextBlock Text="還原資料庫"/>
                            <TextBox Text="{Binding TestDatabaseName}"/>
```

替換為：

```xml
                            <TextBlock Text="還原資料庫"/>
                            <AutoCompleteBox Text="{Binding TestDatabaseName}"
                                             ItemsSource="{Binding AvailableDatabases}"
                                             FilterMode="Contains"
                                             Watermark="可選既有庫或輸入新測試庫名"/>
```

- [ ] **Step 3: 建置驗證**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: 建置成功、無 XAML 編譯錯誤（`AutoCompleteBox` 為 Avalonia 內建，`FilterMode` 屬 `AutoCompleteFilterMode.Contains`）。

- [ ] **Step 4: 全案測試回歸**

Run: `dotnet test`
Expected: 全數通過（Application 新增測試、Desktop 新增測試、既有測試不受影響）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml
git commit -m "feat: 維護計劃步驟1目標資料庫改唯讀下拉、還原資料庫改可編輯下拉

同時將步驟1兩欄標籤正名為目標資料庫／還原資料庫"
```

（此 commit 一併納入先前工作區的標籤文字改動。）

---

## 手動驗收（實作完成後，非自動化）

1. 連上一台有多個使用者資料庫的 SQL Server，開啟維護計劃頁 → 進入精靈步驟1。
2. 「目標資料庫」下拉應列出伺服器上的使用者資料庫，且預設選中設定檔對應主庫；無法手動打字。
3. 選不同目標庫，「還原資料庫」應自動更新為 `{目標}-test`。
4. 「還原資料庫」可展開挑選既有庫，也可清空後手動輸入一個不存在的庫名。
5. 斷線／無連線時，目標下拉為空、不崩潰；還原欄仍可手動輸入。
