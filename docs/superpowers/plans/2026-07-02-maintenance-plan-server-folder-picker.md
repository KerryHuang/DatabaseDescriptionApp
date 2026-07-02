# 維護計劃頁伺服器端資料夾選擇器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 維護計劃精靈的「備份路徑」「還原路徑」兩欄各加一顆「瀏覽…」按鈕，沿用備份頁的伺服器端資料夾對話框（folder-only 模式）選取資料夾。

**Architecture:** 沿用既有 `ServerFolderBrowserViewModel`／`ServerFolderBrowserWindow`，新增「僅選資料夾」模式（隱藏檔名欄、樹只顯示資料夾、回傳帶結尾分隔字元的資料夾）。Domain 新增一個純函式 `EnsureTrailingSeparator`。維護計劃 ViewModel 注入 `IBackupService` 並新增兩個瀏覽命令，對 App 目前連線的伺服器執行。

**Tech Stack:** .NET 8、Avalonia 11、CommunityToolkit.Mvvm、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- UI 文字、程式碼註解、Commit 訊息一律使用繁體中文。
- Clean Architecture：Domain 純 C# 無外部相依；查詢邏輯集中於服務層（不寫在 ViewModel）。
- 跨平台：路徑分隔字元由 `ServerPathHelper` 依實際路徑判定，不得硬編 `\`。
- ViewModel／對話框 ViewModel 維持「無參數設計時建構函式 + 執行時建構函式」。
- 沿用既有對話框，不得破壞備份頁現有「資料夾＋檔名」模式（3 參數建構函式呼叫必須維持有效）。
- 瀏覽失敗或無連線不得中斷流程或使 App 崩潰。
- 檔案存 UTF-8 無 BOM。TDD：先寫失敗測試再實作；頻繁 commit。

---

### Task 1: Domain — `ServerPathHelper.EnsureTrailingSeparator`

**Files:**
- Modify: `src/Specurai.Domain/ServerPathHelper.cs`（在 `IsBackupFile` 之後、類別結尾 `}` 之前新增方法）
- Test: `tests/Specurai.Domain.Tests/ServerPathHelperTests.cs`（既有檔案，新增測試方法）

**Interfaces:**
- Produces: `ServerPathHelper.EnsureTrailingSeparator(string path) : string`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Domain.Tests/ServerPathHelperTests.cs` 的類別內（最後一個 `}` 之前）新增：

```csharp
    [Theory]
    [InlineData("D:\\SQLBackup", "D:\\SQLBackup\\")]
    [InlineData("D:\\SQLBackup\\", "D:\\SQLBackup\\")]
    [InlineData("/var/opt/mssql/backup", "/var/opt/mssql/backup/")]
    [InlineData("/var/opt/mssql/backup/", "/var/opt/mssql/backup/")]
    public void EnsureTrailingSeparator_補上或維持結尾分隔字元(string path, string expected)
    {
        ServerPathHelper.EnsureTrailingSeparator(path).Should().Be(expected);
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~EnsureTrailingSeparator"`
Expected: 編譯失敗（`EnsureTrailingSeparator` 不存在）。

- [ ] **Step 3: 實作**

在 `src/Specurai.Domain/ServerPathHelper.cs` 的 `IsBackupFile` 方法之後、類別結尾 `}` 之前新增：

```csharp

    /// <summary>確保路徑結尾帶該平台的分隔字元。</summary>
    public static string EnsureTrailingSeparator(string path)
    {
        var sep = GetSeparator(path);
        return path.EndsWith(sep) ? path : path + sep;
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~EnsureTrailingSeparator"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Domain/ServerPathHelper.cs tests/Specurai.Domain.Tests/ServerPathHelperTests.cs
git commit -m "feat: ServerPathHelper 新增 EnsureTrailingSeparator"
```

---

### Task 2: `ServerFolderBrowserViewModel` folder-only 模式

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs`（整檔替換為下方版本）
- Modify: `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml`（標題綁定 + 檔名列可見性）
- Test: `tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs`（既有檔案，新增 folder-only 測試）

**Interfaces:**
- Consumes: `ServerPathHelper.EnsureTrailingSeparator`（Task 1）；`IBackupService.ListServerDirectoryAsync`、`ServerDirectoryEntry`、`ServerFolderNode`（既有）
- Produces（新增到 `ServerFolderBrowserViewModel`）:
  - 執行時建構函式簽章：`ServerFolderBrowserViewModel(IBackupService backupService, string connectionString, string initialFileName = "", bool folderOnly = false, string initialFolder = "")`
  - `string Title { get; }`、`bool ShowFileName { get; }`
  - folder-only 時 `Confirm` 回傳 `EnsureTrailingSeparator(SelectedPath)`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs` 類別內新增（`BuildService()` 已存在於該檔，回傳 root="" → C:\,D:\；"D:\\" → SQLBackup(dir)+old.bak(file)）：

```csharp
    [Fact]
    public void FolderOnly_ShowFileName為false()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        vm.ShowFileName.Should().BeFalse();
        vm.Title.Should().Be("選擇伺服器資料夾");
    }

    [Fact]
    public void FileMode_ShowFileName為true()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        vm.ShowFileName.Should().BeTrue();
        vm.Title.Should().Be("尋找備份資料夾");
    }

    [Fact]
    public async Task FolderOnly_展開節點只保留資料夾()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        await vm.LoadRootAsync();
        var dNode = vm.RootNodes[1]; // D:\
        await dNode.LoadChildrenAsync();
        dNode.Children.Should().OnlyContain(c => c.IsDirectory);
        dNode.Children.Should().ContainSingle(c => c.Name == "SQLBackup");
    }

    [Fact]
    public void FolderOnly_Confirm回傳帶結尾分隔字元的資料夾()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true)
        {
            SelectedPath = "D:\\SQLBackup"
        };
        bool? closed = null;
        vm.RequestClose += ok => closed = ok;

        vm.ConfirmCommand.Execute(null);

        vm.ResultPath.Should().Be("D:\\SQLBackup\\");
        closed.Should().BeTrue();
    }

    [Fact]
    public void FolderOnly_未選資料夾_錯誤不關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        bool closed = false;
        vm.RequestClose += _ => closed = true;

        vm.ConfirmCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void FolderOnly_initialFolder預帶SelectedPath且去尾分隔字元()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true, initialFolder: "D:\\SQLBackup\\");
        vm.SelectedPath.Should().Be("D:\\SQLBackup");
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ServerFolderBrowserViewModelTests"`
Expected: 編譯失敗（新建構函式參數 / `ShowFileName` / `Title` 不存在）。

- [ ] **Step 3: 整檔替換 `ServerFolderBrowserViewModel.cs`**

將 `src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs` 全部內容替換為：

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Domain;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 伺服器端資料夾瀏覽對話框 ViewModel
/// </summary>
public partial class ServerFolderBrowserViewModel : ObservableObject
{
    private readonly IBackupService? _backupService;
    private readonly string _connectionString;
    private readonly bool _folderOnly;

    public ObservableCollection<ServerFolderNode> RootNodes { get; } = [];

    [ObservableProperty]
    private ServerFolderNode? _selectedNode;

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>視窗標題</summary>
    public string Title { get; }

    /// <summary>是否顯示「檔案名稱」欄（folder-only 模式隱藏）</summary>
    public bool ShowFileName => !_folderOnly;

    /// <summary>確定後的完整路徑（檔案模式為資料夾+檔名；folder-only 為資料夾）</summary>
    public string? ResultPath { get; private set; }

    /// <summary>要求關閉視窗：true = 確定、false = 取消</summary>
    public event Action<bool>? RequestClose;

    /// <summary>設計時建構函式</summary>
    public ServerFolderBrowserViewModel()
    {
        _connectionString = string.Empty;
        Title = "尋找備份資料夾";
    }

    /// <summary>執行時建構函式</summary>
    public ServerFolderBrowserViewModel(
        IBackupService backupService,
        string connectionString,
        string initialFileName = "",
        bool folderOnly = false,
        string initialFolder = "")
    {
        _backupService = backupService;
        _connectionString = connectionString;
        _fileName = initialFileName;
        _folderOnly = folderOnly;
        Title = folderOnly ? "選擇伺服器資料夾" : "尋找備份資料夾";
        if (folderOnly && !string.IsNullOrEmpty(initialFolder))
            _selectedPath = initialFolder.TrimEnd('\\', '/');
    }

    /// <summary>載入根節點（各磁碟）</summary>
    public async Task LoadRootAsync()
    {
        if (_backupService is null) return;
        try
        {
            var roots = await _backupService.ListServerDirectoryAsync(_connectionString, string.Empty);
            RootNodes.Clear();
            foreach (var r in roots)
                RootNodes.Add(new ServerFolderNode(r, LoadChildrenAsync));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽伺服器目錄：{ex.Message}";
        }
    }

    private async Task<IReadOnlyList<ServerDirectoryEntry>> LoadChildrenAsync(string path)
    {
        if (_backupService is null) return [];
        try
        {
            var entries = await _backupService.ListServerDirectoryAsync(_connectionString, path);
            // folder-only 模式：只顯示資料夾
            return _folderOnly ? entries.Where(e => e.IsDirectory).ToList() : entries;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽「{path}」：{ex.Message}";
            return [];
        }
    }

    partial void OnSelectedNodeChanged(ServerFolderNode? value)
    {
        if (value is null || value.IsPlaceholder) return;

        if (value.IsDirectory)
        {
            SelectedPath = value.FullPath;
        }
        else
        {
            // 選到現有備份檔：帶入其所在資料夾與檔名
            SelectedPath = ParentOf(value.FullPath);
            FileName = value.Name;
        }
    }

    private static string ParentOf(string fullPath)
    {
        var sep = ServerPathHelper.GetSeparator(fullPath);
        var trimmed = fullPath.TrimEnd(sep);
        var idx = trimmed.LastIndexOf(sep);
        if (idx < 0) return fullPath;

        var parent = trimmed[..idx];
        // 磁碟根目錄（例如 "D:"）補回分隔字元 → "D:\"
        if (parent.Length == 2 && char.IsLetter(parent[0]) && parent[1] == ':')
            return parent + sep;
        // Unix 根目錄
        if (parent.Length == 0) return sep.ToString();
        return parent;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath) || (!_folderOnly && string.IsNullOrWhiteSpace(FileName)))
        {
            ErrorMessage = _folderOnly ? "請選擇資料夾" : "請選擇資料夾並輸入檔案名稱";
            return;
        }
        ResultPath = _folderOnly
            ? ServerPathHelper.EnsureTrailingSeparator(SelectedPath)
            : ServerPathHelper.Combine(SelectedPath, FileName);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
```

- [ ] **Step 4: 修改 AXAML（標題綁定 + 檔名列可見性）**

在 `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml`：

其一，將第 9 行的 `Title="尋找備份資料夾"` 改為：

```xml
        Title="{Binding Title}"
```

其二，將第 42-43 行（檔案名稱列的 TextBlock 與 TextBox）改為（各加 `IsVisible="{Binding ShowFileName}"`）：

```xml
            <TextBlock Grid.Row="1" Grid.Column="0" Text="檔案名稱：" VerticalAlignment="Center" Margin="0,0,10,6"
                       IsVisible="{Binding ShowFileName}"/>
            <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding FileName}" Margin="0,0,0,6"
                     IsVisible="{Binding ShowFileName}"/>
```

- [ ] **Step 5: 執行測試確認通過（含既有回歸）**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ServerFolderBrowserViewModelTests"`
Expected: PASS（新 folder-only 測試 + 既有檔案模式測試全綠）。

- [ ] **Step 6: 建置 Desktop 確認 AXAML 編譯**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs
git commit -m "feat: 伺服器資料夾對話框新增 folder-only 模式"
```

---

### Task 3: `MaintenancePlanDocumentViewModel` 注入服務與瀏覽命令

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/Program.cs:71-76`（DI 註冊加 `IBackupService`）
- Test: `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelBrowseTests.cs`（新檔）

**Interfaces:**
- Consumes: `ServerFolderBrowserViewModel(..., folderOnly: true, initialFolder: ...)`、`ServerFolderBrowserWindow`（Task 2）；`IBackupService`、`IConnectionManager`
- Produces（新增到 `MaintenancePlanDocumentViewModel`）: `BrowseBackupPathCommand`、`BrowseRestorePathCommand`；DI 建構函式新增最後一個參數 `IBackupService backupService`

- [ ] **Step 1: 寫失敗測試**

Create `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelBrowseTests.cs`:

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelBrowseTests
{
    private static MaintenancePlanDocumentViewModel Build(IConnectionManager conn)
    {
        var job = Substitute.For<IAgentJobService>();
        var plan = Substitute.For<IMaintenancePlanService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        var backup = Substitute.For<IBackupService>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task BrowseBackupPath_無目前連線_設定狀態訊息()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = Build(conn);

        await vm.BrowseBackupPathCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("請先選擇連線");
    }

    [Fact]
    public async Task BrowseRestorePath_無目前連線_設定狀態訊息()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = Build(conn);

        await vm.BrowseRestorePathCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("請先選擇連線");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelBrowseTests"`
Expected: 編譯失敗（建構函式參數數量不符 / 命令不存在）。

- [ ] **Step 3: 新增 using 與欄位**

在 `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs` 頂端 using 區（第 12 行 `using Specurai.Domain.Enums;` 之後）新增：

```csharp
using Specurai.Domain.Interfaces;
using Avalonia.Controls.ApplicationLifetimes;
using Specurai.Desktop.Views;
```

在欄位宣告區（第 24 行 `private readonly IConnectionManager? _connectionManager;` 之後）新增：

```csharp
    private readonly IBackupService? _backupService;
```

- [ ] **Step 4: 修改 DI 建構函式**

將 DI 建構函式（第 281-285 行的參數列與其 body 起始）改為新增第 5 個參數並指派。把：

```csharp
    public MaintenancePlanDocumentViewModel(
        IAgentJobService jobService,
        IMaintenancePlanService planService,
        IMaintenancePlanSqlGenerator sqlGenerator,
        IConnectionManager connectionManager)
    {
        _jobService = jobService;
        _planService = planService;
        _sqlGenerator = sqlGenerator;
        _connectionManager = connectionManager;
```

改為：

```csharp
    public MaintenancePlanDocumentViewModel(
        IAgentJobService jobService,
        IMaintenancePlanService planService,
        IMaintenancePlanSqlGenerator sqlGenerator,
        IConnectionManager connectionManager,
        IBackupService backupService)
    {
        _jobService = jobService;
        _planService = planService;
        _sqlGenerator = sqlGenerator;
        _connectionManager = connectionManager;
        _backupService = backupService;
```

（其餘 body 不變。）

- [ ] **Step 5: 新增瀏覽命令**

在 `#region 自動帶入`（第 323 行）之前，或建構函式 `#endregion`（第 321 行）之後，新增：

```csharp
    #region 伺服器路徑瀏覽

    [RelayCommand]
    private async Task BrowseBackupPathAsync() => await BrowsePathAsync(isBackup: true);

    [RelayCommand]
    private async Task BrowseRestorePathAsync() => await BrowsePathAsync(isBackup: false);

    private async Task BrowsePathAsync(bool isBackup)
    {
        if (_backupService == null || _connectionManager == null)
        {
            StatusMessage = "請先選擇連線";
            return;
        }

        var profile = _connectionManager.GetCurrentProfile();
        if (profile == null)
        {
            StatusMessage = "請先選擇連線";
            return;
        }

        var connectionString = _connectionManager.GetConnectionString(profile.Id);
        if (string.IsNullOrEmpty(connectionString))
        {
            StatusMessage = "無法取得連線字串";
            return;
        }

        var initialFolder = isBackup ? BackupPath : RestorePath;
        var dialogViewModel = new ServerFolderBrowserViewModel(
            _backupService, connectionString, folderOnly: true, initialFolder: initialFolder);
        var dialog = new ServerFolderBrowserWindow(dialogViewModel);

        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (owner == null) return;

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (confirmed && !string.IsNullOrEmpty(dialogViewModel.ResultPath))
        {
            if (isBackup) BackupPath = dialogViewModel.ResultPath;
            else RestorePath = dialogViewModel.ResultPath;
        }
    }

    #endregion
```

- [ ] **Step 6: 更新 DI 註冊**

將 `src/Specurai.Desktop/Program.cs:71-76` 的維護計劃註冊改為（新增第 5 個引數）：

```csharp
        services.AddTransient<MaintenancePlanDocumentViewModel>(sp =>
            new MaintenancePlanDocumentViewModel(
                sp.GetRequiredService<IAgentJobService>(),
                sp.GetRequiredService<IMaintenancePlanService>(),
                sp.GetRequiredService<IMaintenancePlanSqlGenerator>(),
                sp.GetRequiredService<IConnectionManager>(),
                sp.GetRequiredService<IBackupService>()));
```

（`IBackupService` 已在 Program.cs 有註冊供備份頁使用，無需另註冊。若編譯報缺少命名空間，確認頂端已有 `using Specurai.Domain.Interfaces;`。）

- [ ] **Step 7: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelBrowseTests"`
Expected: PASS。

- [ ] **Step 8: 執行 Desktop 全部測試確認無回歸**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 全部通過。

- [ ] **Step 9: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs src/Specurai.Desktop/Program.cs tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelBrowseTests.cs
git commit -m "feat: 維護計劃頁注入 IBackupService 並新增備份/還原路徑瀏覽命令"
```

---

### Task 4: `MaintenancePlanDocumentView.axaml` 加入瀏覽按鈕

**Files:**
- Modify: `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml:205-218`

**Interfaces:**
- Consumes: `BrowseBackupPathCommand`、`BrowseRestorePathCommand`（Task 3）

- [ ] **Step 1: 修改路徑列版面**

將 `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml` 第 205-218 行整段（第二列：備份路徑 + 還原路徑）替換為：

```xml
                        <!-- 第二列：備份路徑 + 還原路徑 -->
                        <Grid ColumnDefinitions="*,16,*">
                            <StackPanel Grid.Column="0" Spacing="4">
                                <TextBlock Text="備份路徑"/>
                                <Grid ColumnDefinitions="*,Auto">
                                    <TextBox Grid.Column="0" Text="{Binding BackupPath}"
                                             IsReadOnly="{Binding !IsPathCustom}"
                                             Watermark="例如: D:\SQLBackup\"/>
                                    <Button Grid.Column="1" Content="瀏覽…" Command="{Binding BrowseBackupPathCommand}"
                                            Margin="6,0,0,0" ToolTip.Tip="瀏覽 SQL Server 伺服器端資料夾"/>
                                </Grid>
                            </StackPanel>
                            <StackPanel Grid.Column="2" Spacing="4">
                                <TextBlock Text="還原路徑"/>
                                <Grid ColumnDefinitions="*,Auto">
                                    <TextBox Grid.Column="0" Text="{Binding RestorePath}"
                                             IsReadOnly="{Binding !IsPathCustom}"
                                             Watermark="例如: D:\sql_data\"/>
                                    <Button Grid.Column="1" Content="瀏覽…" Command="{Binding BrowseRestorePathCommand}"
                                            Margin="6,0,0,0" ToolTip.Tip="瀏覽 SQL Server 伺服器端資料夾"/>
                                </Grid>
                            </StackPanel>
                        </Grid>
```

> 說明：瀏覽按鈕不設 `IsEnabled` 綁定，任何平台皆可用；即使路徑 TextBox 於非「其他」平台為唯讀，透過瀏覽仍可設定自訂路徑（程式設值不受 `IsReadOnly` 限制）。

- [ ] **Step 2: 建置整個解決方案確認通過**

Run: `dotnet build`
Expected: Build succeeded。

> 若 Desktop DLL 被執行中的桌面程式鎖定導致建置失敗，先關閉該程式再重試。

- [ ] **Step 3: 執行全部測試確認無回歸**

Run: `dotnet test`
Expected: 全部通過。

- [ ] **Step 4: 手動煙霧驗證（選用但建議）**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`
步驟：開啟「維護計劃」→ 精靈步驟 1 → 點「備份路徑」旁「瀏覽…」→ 出現伺服器資料夾樹（只有資料夾、無檔名欄）→ 選資料夾 → 確定後路徑帶回且結尾有分隔字元 → 對「還原路徑」重複驗證。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml
git commit -m "feat: 維護計劃頁備份/還原路徑加入伺服器端資料夾瀏覽按鈕"
```

---

## 完成後

- [ ] 執行 `superpowers:requesting-code-review` 進行程式碼審查（專案憲章要求）。
- [ ] 依審查結果修正，全部測試綠燈後回報完成。

## Self-Review 對照（spec → task）

| Spec 需求 | 對應 Task |
|-----------|-----------|
| §4.1 folder-only 模式（建構函式、Title、ShowFileName、檔案過濾、Confirm 分支、預帶 SelectedPath）| Task 2 |
| §4.2 AXAML 檔名列可見性 + 標題綁定 | Task 2 |
| §4.3 EnsureTrailingSeparator | Task 1 |
| §4.4 維護計劃 VM 注入 IBackupService + 兩個瀏覽命令 + Program.cs | Task 3 |
| §4.5 View 兩顆瀏覽按鈕 | Task 4 |
| §5 錯誤處理（無連線 StatusMessage、xp_dirtree 容錯、未選資料夾擋下）| Task 3（無連線）＋ Task 2（Confirm 驗證、既有 try/catch）|
| §6 測試 | Task 1/2/3 |
| 不破壞備份頁檔案模式（3 參數呼叫）| Task 2（選用參數；FileMode 回歸測試）|
