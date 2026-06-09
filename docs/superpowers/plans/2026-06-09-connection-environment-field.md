# 連線設定「環境」欄位 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在連線設定新增「環境」欄位（Development/Testing/Staging/Production），並對 Production 連線的破壞性操作加上紅色警告橫幅防呆。

**Architecture:** Domain 新增純列舉 `DatabaseEnvironment` 與 `ConnectionProfile.Environment` 屬性（預設 Staging、數字序列化、無序列化屬性以維持 Clean Architecture 與 MoldplanDbSwitcher 相容）。Desktop 在設定表單加環境選擇；Production 防呆集中於唯一確認路徑 `ShowConfirmSaveDialogAsync`，所有破壞性操作自動受惠。

**Tech Stack:** .NET 8、Avalonia 11、CommunityToolkit.Mvvm、xUnit + NSubstitute + FluentAssertions、System.Text.Json。

---

## 檔案結構

| 檔案 | 責任 | 動作 |
|------|------|------|
| `src/Specurai.Domain/Entities/DatabaseEnvironment.cs` | 環境列舉 | Create |
| `src/Specurai.Domain/Entities/ConnectionProfile.cs` | 新增 `Environment` 屬性 | Modify |
| `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs` | 預設值＋序列化往返＋舊資料相容 | Modify |
| `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs` | 表單環境欄位與選項 | Modify |
| `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs` | 環境載入/寫回/重置/選項 | Modify |
| `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml` | 環境 ComboBox | Modify |
| `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` | `IsCurrentProfileProduction`、`CurrentEnvironmentDatabase` | Modify |
| `tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs` | Production 判斷 | Modify |
| `src/Specurai.Desktop/Views/ConfirmDialog.axaml` | 警告橫幅 | Modify |
| `src/Specurai.Desktop/Views/ConfirmDialog.axaml.cs` | 橫幅建構式多載 | Modify |
| `src/Specurai.Desktop/Views/MainWindow.axaml.cs` | 確認對話框升級邏輯 | Modify |

---

## Task 1: Domain 列舉與 ConnectionProfile 屬性

**Files:**
- Create: `src/Specurai.Domain/Entities/DatabaseEnvironment.cs`
- Modify: `src/Specurai.Domain/Entities/ConnectionProfile.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs` 檔案最上方 `using` 區塊加入：

```csharp
using System.Text.Json;
```

在類別內（最後一個 `}` 之前）加入以下測試：

```csharp
[Fact]
public void ConnectionProfile_Environment_預設為Staging()
{
    // Arrange & Act
    var profile = new ConnectionProfile
    {
        Name = "測試",
        Server = "localhost",
        Database = "TestDb"
    };

    // Assert
    profile.Environment.Should().Be(DatabaseEnvironment.Staging);
}

[Fact]
public void ConnectionProfile_可設定Environment為Production()
{
    // Arrange & Act
    var profile = new ConnectionProfile
    {
        Name = "正式",
        Server = "prod",
        Database = "ProdDb",
        Environment = DatabaseEnvironment.Production
    };

    // Assert
    profile.Environment.Should().Be(DatabaseEnvironment.Production);
}

[Fact]
public void ConnectionProfile_序列化往返_應保留Environment()
{
    // Arrange
    var profile = new ConnectionProfile
    {
        Name = "正式",
        Server = "prod",
        Database = "ProdDb",
        Environment = DatabaseEnvironment.Production
    };

    // Act
    var json = JsonSerializer.Serialize(profile);
    var restored = JsonSerializer.Deserialize<ConnectionProfile>(json);

    // Assert
    restored!.Environment.Should().Be(DatabaseEnvironment.Production);
}

[Fact]
public void ConnectionProfile_反序列化舊JSON無Environment欄位_應為Staging()
{
    // Arrange：模擬既有 connections.json 內無 Environment 欄位的連線
    var legacyJson = """
        { "Name": "舊連線", "Server": "localhost", "Database": "OldDb", "AuthType": 0 }
        """;

    // Act
    var profile = JsonSerializer.Deserialize<ConnectionProfile>(legacyJson);

    // Assert
    profile!.Environment.Should().Be(DatabaseEnvironment.Staging);
}

[Fact]
public void DatabaseEnvironment_列舉值順序應為Dev_Test_Staging_Prod()
{
    // Assert
    ((int)DatabaseEnvironment.Development).Should().Be(0);
    ((int)DatabaseEnvironment.Testing).Should().Be(1);
    ((int)DatabaseEnvironment.Staging).Should().Be(2);
    ((int)DatabaseEnvironment.Production).Should().Be(3);
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileTests"`
Expected: 編譯失敗（`DatabaseEnvironment` 與 `Environment` 尚未存在）。

- [ ] **Step 3: 建立列舉**

建立 `src/Specurai.Domain/Entities/DatabaseEnvironment.cs`：

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 資料庫連線所屬環境
/// </summary>
public enum DatabaseEnvironment
{
    /// <summary>開發環境</summary>
    Development,

    /// <summary>測試環境</summary>
    Testing,

    /// <summary>預備環境</summary>
    Staging,

    /// <summary>正式環境</summary>
    Production
}
```

- [ ] **Step 4: 新增 ConnectionProfile 屬性**

在 `src/Specurai.Domain/Entities/ConnectionProfile.cs` 的 `IsDefault` 屬性之後（`public bool IsDefault { get; set; }` 下方、類別結尾 `}` 之前）加入：

```csharp

    /// <summary>
    /// 連線所屬環境（預設預備環境）
    /// </summary>
    public DatabaseEnvironment Environment { get; set; } = DatabaseEnvironment.Staging;
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileTests"`
Expected: PASS（全部通過）。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Entities/DatabaseEnvironment.cs src/Specurai.Domain/Entities/ConnectionProfile.cs tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs
git commit -m "feat(domain): 新增 ConnectionProfile 環境欄位"
```

---

## Task 2: 設定表單 ViewModel 支援環境

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs` 類別內加入：

```csharp
[Fact]
public void EnvironmentOptions_應包含四個環境選項()
{
    // Assert
    ConnectionSetupViewModel.EnvironmentOptions.Should().BeEquivalentTo(new[]
    {
        DatabaseEnvironment.Development,
        DatabaseEnvironment.Testing,
        DatabaseEnvironment.Staging,
        DatabaseEnvironment.Production
    });
}

[Fact]
public void 初始狀態_Environment應為Staging()
{
    // Act
    var vm = new ConnectionSetupViewModel();

    // Assert
    vm.Environment.Should().Be(DatabaseEnvironment.Staging);
}

[Fact]
public void 選取Profile_應載入其Environment()
{
    // Arrange
    var profile = new ConnectionProfile
    {
        Id = Guid.NewGuid(),
        Name = "正式",
        Server = "prod",
        Database = "ProdDb",
        Environment = DatabaseEnvironment.Production
    };
    _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
    var vm = new ConnectionSetupViewModel(_connectionManager);

    // Act
    vm.SelectedProfile = profile;

    // Assert
    vm.Environment.Should().Be(DatabaseEnvironment.Production);
}

[Fact]
public void 儲存_應將Environment寫入新Profile()
{
    // Arrange
    _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
    var vm = new ConnectionSetupViewModel(_connectionManager);
    vm.Name = "正式";
    vm.Server = "prod";
    vm.Database = "ProdDb";
    vm.Environment = DatabaseEnvironment.Production;

    // Act
    vm.SaveCommand.Execute(null);

    // Assert
    _connectionManager.Received().AddProfile(
        Arg.Is<ConnectionProfile>(p => p.Environment == DatabaseEnvironment.Production));
}

[Fact]
public void 新增_應將Environment重置為Staging()
{
    // Arrange
    var profile = new ConnectionProfile
    {
        Id = Guid.NewGuid(),
        Name = "正式",
        Server = "prod",
        Database = "ProdDb",
        Environment = DatabaseEnvironment.Production
    };
    _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
    var vm = new ConnectionSetupViewModel(_connectionManager);
    vm.SelectedProfile = profile; // 先載入 Production

    // Act
    vm.NewProfileCommand.Execute(null);

    // Assert
    vm.Environment.Should().Be(DatabaseEnvironment.Staging);
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionSetupViewModelTests"`
Expected: 編譯失敗（`Environment`、`EnvironmentOptions` 尚未存在）。

- [ ] **Step 3: 新增 ViewModel 屬性與選項**

在 `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs` 的 `_isDefault` 欄位之後（約第 40 行 `private bool _isDefault;` 下方）加入：

```csharp

    [ObservableProperty]
    private DatabaseEnvironment _environment = DatabaseEnvironment.Staging;
```

在 `Profiles` / `ExternalProfiles` 集合宣告附近（約第 67 行 `public ObservableCollection<ConnectionProfile> ExternalProfiles { get; } = [];` 下方）加入：

```csharp

    public static IReadOnlyList<DatabaseEnvironment> EnvironmentOptions { get; } =
        Enum.GetValues<DatabaseEnvironment>();
```

- [ ] **Step 4: 載入、寫回、重置 Environment**

在 `OnSelectedProfileChanged` 方法的 `if (value != null)` 區塊內，於 `IsDefault = value.IsDefault;` 之後加入：

```csharp
            Environment = value.Environment;
```

在 `CreateProfileFromForm` 方法的物件初始化中，於 `IsDefault = IsDefault` 之後加入逗號與一行：

```csharp
            IsDefault = IsDefault,
            Environment = Environment
```

（將原本 `IsDefault = IsDefault` 結尾加上逗號，再加 `Environment = Environment`。）

在 `ClearForm` 方法內，於 `IsDefault = false;` 之後加入：

```csharp
        Environment = DatabaseEnvironment.Staging;
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionSetupViewModelTests"`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs
git commit -m "feat(desktop): 設定表單支援環境欄位"
```

---

## Task 3: 設定表單 View 加入環境選擇

**Files:**
- Modify: `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml`

此為 UI 變更，無單元測試（本專案 Desktop.Tests 僅測 ViewModel）；於 Task 7 以 `/run` 手動驗證。

- [ ] **Step 1: 在「資料庫名稱」與「驗證方式」之間插入環境 ComboBox**

在 `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml` 找到「資料庫」區塊結尾（第 93 行 `</StackPanel>`，即 `<!-- 驗證方式 -->` 註解之前），插入：

```xml

                            <!-- 環境 -->
                            <StackPanel Spacing="4">
                                <TextBlock Text="環境"/>
                                <ComboBox HorizontalAlignment="Stretch"
                                          ItemsSource="{Binding EnvironmentOptions}"
                                          SelectedItem="{Binding Environment}"
                                          IsEnabled="{Binding !IsExternalProfileSelected}"/>
                            </StackPanel>
```

- [ ] **Step 2: 建置確認 AXAML 無誤**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded（無 XAML 編譯錯誤）。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml
git commit -m "feat(desktop): 連線設定表單顯示環境選擇器"
```

---

## Task 4: MainWindowViewModel 提供 Production 判斷

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs` 類別內（最後一個 `}` 之前、`MainWindowViewModelTests` 類別範圍內）加入：

```csharp
#region Production 防呆判斷

private MainWindowViewModel CreateVmWithCurrentProfile(ConnectionProfile? current)
{
    _connectionManager.GetCurrentProfile().Returns(current);
    return new MainWindowViewModel(
        _connectionManager,
        _exportService,
        _tableQueryService,
        _sqlQueryRepository,
        _columnTypeRepository,
        _objectTree,
        new UpdateNotificationViewModel());
}

[Fact]
public void IsCurrentProfileProduction_當前為Production_應為True()
{
    // Arrange
    var vm = CreateVmWithCurrentProfile(new ConnectionProfile
    {
        Name = "正式", Server = "prod", Database = "ProdDb",
        Environment = DatabaseEnvironment.Production
    });

    // Assert
    vm.IsCurrentProfileProduction.Should().BeTrue();
    vm.CurrentEnvironmentDatabase.Should().Be("ProdDb");
}

[Fact]
public void IsCurrentProfileProduction_當前為Staging_應為False()
{
    // Arrange
    var vm = CreateVmWithCurrentProfile(new ConnectionProfile
    {
        Name = "預備", Server = "stg", Database = "StgDb",
        Environment = DatabaseEnvironment.Staging
    });

    // Assert
    vm.IsCurrentProfileProduction.Should().BeFalse();
}

[Fact]
public void IsCurrentProfileProduction_無當前連線_應為False()
{
    // Arrange
    var vm = CreateVmWithCurrentProfile(null);

    // Assert
    vm.IsCurrentProfileProduction.Should().BeFalse();
    vm.CurrentEnvironmentDatabase.Should().BeNull();
}

#endregion
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: 編譯失敗（`IsCurrentProfileProduction`、`CurrentEnvironmentDatabase` 尚未存在）。

- [ ] **Step 3: 新增計算屬性**

在 `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` 的 `ConfirmSaveCallback` 屬性之後（約第 90 行 `public Func<string, Task<bool>>? ConfirmSaveCallback { get; set; }` 下方）加入：

```csharp

    /// <summary>
    /// 目前連線是否為正式環境（Production），供破壞性操作防呆使用。
    /// </summary>
    public bool IsCurrentProfileProduction =>
        _connectionManager?.GetCurrentProfile()?.Environment == DatabaseEnvironment.Production;

    /// <summary>
    /// 目前連線的資料庫名稱（供 Production 警告橫幅顯示）。
    /// </summary>
    public string? CurrentEnvironmentDatabase =>
        _connectionManager?.GetCurrentProfile()?.Database;
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(desktop): MainWindowViewModel 提供 Production 環境判斷"
```

---

## Task 5: ConfirmDialog 支援警告橫幅

**Files:**
- Modify: `src/Specurai.Desktop/Views/ConfirmDialog.axaml`
- Modify: `src/Specurai.Desktop/Views/ConfirmDialog.axaml.cs`

此為 UI 元件變更，無單元測試；於 Task 7 以 `/run` 手動驗證。

- [ ] **Step 1: 在 AXAML 加入警告橫幅**

將 `src/Specurai.Desktop/Views/ConfirmDialog.axaml` 的 `<StackPanel Margin="20" Spacing="15">` 內容改為（在 `MessageText` 之前插入橫幅）：

```xml
    <StackPanel Margin="20" Spacing="15">
        <Border x:Name="WarningBanner"
                IsVisible="False"
                Background="#22FF3B30"
                BorderBrush="#FF3B30"
                BorderThickness="1"
                CornerRadius="4"
                Padding="10,8">
            <TextBlock x:Name="WarningText"
                       Foreground="#FF3B30"
                       FontWeight="Bold"
                       TextWrapping="Wrap"/>
        </Border>
        <TextBlock x:Name="MessageText"
                   TextWrapping="Wrap"
                   FontSize="14"/>
        <StackPanel Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Spacing="10">
            <Button Content="是" Width="80" Click="OnYesClick"/>
            <Button Content="否" Width="80" Click="OnNoClick"/>
        </StackPanel>
    </StackPanel>
```

- [ ] **Step 2: 在 code-behind 加入橫幅建構式多載**

將 `src/Specurai.Desktop/Views/ConfirmDialog.axaml.cs` 的 `ConfirmDialog(string message)` 建構式之後加入新多載：

```csharp
    public ConfirmDialog(string message, string? warningBanner) : this(message)
    {
        if (!string.IsNullOrEmpty(warningBanner))
        {
            WarningText.Text = warningBanner;
            WarningBanner.IsVisible = true;
        }
    }
```

- [ ] **Step 3: 建置確認**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded。

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Desktop/Views/ConfirmDialog.axaml src/Specurai.Desktop/Views/ConfirmDialog.axaml.cs
git commit -m "feat(desktop): ConfirmDialog 支援警告橫幅"
```

---

## Task 6: MainWindow 確認流程升級為 Production 防呆

**Files:**
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml.cs`

此為 View 串接，無單元測試；於 Task 7 以 `/run` 手動驗證。

- [ ] **Step 1: 修改 ShowConfirmSaveDialogAsync**

將 `src/Specurai.Desktop/Views/MainWindow.axaml.cs` 的 `ShowConfirmSaveDialogAsync` 方法（第 92-97 行）整段替換為：

```csharp
    private async Task<bool> ShowConfirmSaveDialogAsync(string message)
    {
        string? banner = null;
        if (DataContext is MainWindowViewModel vm && vm.IsCurrentProfileProduction)
            banner = $"⚠ 正式環境 (Production)：{vm.CurrentEnvironmentDatabase}";

        var dialog = new ConfirmDialog(message, banner);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }
```

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Desktop/Views/MainWindow.axaml.cs
git commit -m "feat(desktop): Production 連線破壞性操作顯示警告橫幅"
```

---

## Task 7: 整體驗證與程式碼審查

**Files:** 無（驗證任務）

- [ ] **Step 1: 完整建置**

Run: `dotnet build`
Expected: Build succeeded，無警告新增。

- [ ] **Step 2: 完整測試**

Run: `dotnet test`
Expected: 全部通過（含本次新增的 Domain／Desktop 測試）。

- [ ] **Step 3: 手動驗證 UI**

執行桌面程式（`/run` 或 `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`），確認：
- 連線設定表單在「資料庫名稱」與「驗證方式」之間出現「環境」下拉選單，含四個選項。
- 既有連線載入時環境顯示為 Staging（除非已另存）。
- 新增連線預設 Staging；可切換、儲存後重新選取仍保留所選環境。
- 將某連線設為 Production 並連線後，執行任一破壞性操作（例如未使用索引報表的 DROP）時，確認對話框頂端出現紅色「⚠ 正式環境 (Production)：<資料庫名>」橫幅；非 Production 連線則無橫幅。

- [ ] **Step 4: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本次所有變更（依 CLAUDE.md <law> 要求），再回報完成。

- [ ] **Step 5: 驗證 moldplan-change-database 相容性（讀檔不破）**

確認 `MoldplanDbSwitcher` 無需變更：開啟 `C:\Users\zihao\source\repos\moldplan-change-database\src\MoldplanDbSwitcher\Services\ConnectionSourceService.cs`，確認其反序列化使用 `PropertyNameCaseInsensitive` 且忽略未知欄位（System.Text.Json 預設行為）。本任務不修改該專案，只在計畫中記錄已確認向前相容。
