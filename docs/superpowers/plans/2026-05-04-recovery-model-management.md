# Recovery Model 管理功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增獨立 MDI 文件頁，讓使用者查詢所有資料庫的 Recovery Model 並透過下拉選單批次修改，儲存前需確認。

**Architecture:** 遵循 Clean Architecture：Domain 定義 Entity 與 Repository 介面 → Application 定義並實作 Service → Infrastructure 實作 Repository → Desktop ViewModel + View。

**Tech Stack:** .NET 8、Dapper、Avalonia 11、CommunityToolkit.Mvvm、xUnit、NSubstitute、FluentAssertions

---

## 檔案結構

| 動作 | 路徑 |
|------|------|
| 新增 | `src/Specurai.Domain/Entities/DatabaseRecoveryModel.cs` |
| 新增 | `src/Specurai.Domain/Interfaces/IDatabaseRecoveryModelRepository.cs` |
| 新增 | `src/Specurai.Application/Services/IDatabaseRecoveryModelService.cs` |
| 新增 | `src/Specurai.Application/Services/DatabaseRecoveryModelService.cs` |
| 新增 | `src/Specurai.Infrastructure/Repositories/DatabaseRecoveryModelRepository.cs` |
| 新增 | `src/Specurai.Desktop/ViewModels/RecoveryModelRowViewModel.cs` |
| 新增 | `src/Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs` |
| 新增 | `src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml` |
| 新增 | `src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs` |
| 修改 | `src/Specurai.Desktop/Program.cs` |
| 修改 | `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` |
| 修改 | `src/Specurai.Desktop/Views/MainWindow.axaml` |
| 新增 | `tests/Specurai.Domain.Tests/Entities/DatabaseRecoveryModelTests.cs` |
| 新增 | `tests/Specurai.Application.Tests/Services/DatabaseRecoveryModelServiceTests.cs` |
| 新增 | `tests/Specurai.Desktop.Tests/ViewModels/RecoveryModelDocumentViewModelTests.cs` |

---

## Task 1：Domain Entity 與 Repository 介面

**Files:**
- 新增：`src/Specurai.Domain/Entities/DatabaseRecoveryModel.cs`
- 新增：`src/Specurai.Domain/Interfaces/IDatabaseRecoveryModelRepository.cs`
- 新增：`tests/Specurai.Domain.Tests/Entities/DatabaseRecoveryModelTests.cs`

- [ ] **Step 1：撰寫失敗測試**

```csharp
// tests/Specurai.Domain.Tests/Entities/DatabaseRecoveryModelTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class DatabaseRecoveryModelTests
{
    [Fact]
    public void Constructor_應設定屬性()
    {
        var entity = new DatabaseRecoveryModel
        {
            DatabaseName = "leadtech",
            RecoveryModel = "FULL"
        };

        entity.DatabaseName.Should().Be("leadtech");
        entity.RecoveryModel.Should().Be("FULL");
    }
}
```

- [ ] **Step 2：執行測試確認失敗**

```
dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~DatabaseRecoveryModelTests" -v minimal
```

預期：`FAILED`（找不到 `DatabaseRecoveryModel` 類別）

- [ ] **Step 3：建立 Entity**

```csharp
// src/Specurai.Domain/Entities/DatabaseRecoveryModel.cs
namespace Specurai.Domain.Entities;

/// <summary>
/// 資料庫 Recovery Model 資訊
/// </summary>
public class DatabaseRecoveryModel
{
    public required string DatabaseName { get; init; }
    public required string RecoveryModel { get; init; }
}
```

- [ ] **Step 4：建立 Repository 介面**

```csharp
// src/Specurai.Domain/Interfaces/IDatabaseRecoveryModelRepository.cs
using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// 資料庫 Recovery Model 資料存取介面
/// </summary>
public interface IDatabaseRecoveryModelRepository
{
    Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default);
    Task SetRecoveryModelAsync(string databaseName, string recoveryModel, CancellationToken ct = default);
}
```

- [ ] **Step 5：執行測試確認通過**

```
dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~DatabaseRecoveryModelTests" -v minimal
```

預期：`PASSED`

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Domain/Entities/DatabaseRecoveryModel.cs \
        src/Specurai.Domain/Interfaces/IDatabaseRecoveryModelRepository.cs \
        tests/Specurai.Domain.Tests/Entities/DatabaseRecoveryModelTests.cs
git commit -m "feat(domain): 新增 DatabaseRecoveryModel entity 與 repository 介面"
```

---

## Task 2：Application Service

**Files:**
- 新增：`src/Specurai.Application/Services/IDatabaseRecoveryModelService.cs`
- 新增：`src/Specurai.Application/Services/DatabaseRecoveryModelService.cs`
- 新增：`tests/Specurai.Application.Tests/Services/DatabaseRecoveryModelServiceTests.cs`

- [ ] **Step 1：撰寫失敗測試**

```csharp
// tests/Specurai.Application.Tests/Services/DatabaseRecoveryModelServiceTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

public class DatabaseRecoveryModelServiceTests
{
    private readonly IDatabaseRecoveryModelRepository _repository;
    private readonly DatabaseRecoveryModelService _sut;

    public DatabaseRecoveryModelServiceTests()
    {
        _repository = Substitute.For<IDatabaseRecoveryModelRepository>();
        _sut = new DatabaseRecoveryModelService(_repository);
    }

    [Fact]
    public async Task GetAllAsync_應委派至Repository()
    {
        var expected = new List<DatabaseRecoveryModel>
        {
            new() { DatabaseName = "master", RecoveryModel = "SIMPLE" },
            new() { DatabaseName = "leadtech", RecoveryModel = "FULL" }
        };
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.GetAllAsync();

        result.Should().BeEquivalentTo(expected);
        await _repository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_應只對有變更的資料庫呼叫SetRecoveryModelAsync()
    {
        var changes = new[]
        {
            ("leadtech", "SIMPLE"),
            ("moldplan", "FULL")
        };

        await _sut.SaveChangesAsync(changes);

        await _repository.Received(1).SetRecoveryModelAsync("leadtech", "SIMPLE", Arg.Any<CancellationToken>());
        await _repository.Received(1).SetRecoveryModelAsync("moldplan", "FULL", Arg.Any<CancellationToken>());
        await _repository.Received(2).SetRecoveryModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_空清單_不呼叫Repository()
    {
        await _sut.SaveChangesAsync([]);

        await _repository.DidNotReceive().SetRecoveryModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2：執行測試確認失敗**

```
dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~DatabaseRecoveryModelServiceTests" -v minimal
```

預期：`FAILED`（找不到 `DatabaseRecoveryModelService`）

- [ ] **Step 3：建立 Service 介面**

```csharp
// src/Specurai.Application/Services/IDatabaseRecoveryModelService.cs
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 資料庫 Recovery Model 管理服務介面
/// </summary>
public interface IDatabaseRecoveryModelService
{
    Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default);
    Task SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)> changes, CancellationToken ct = default);
}
```

- [ ] **Step 4：建立 Service 實作**

```csharp
// src/Specurai.Application/Services/DatabaseRecoveryModelService.cs
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// 資料庫 Recovery Model 管理服務
/// </summary>
public class DatabaseRecoveryModelService : IDatabaseRecoveryModelService
{
    private readonly IDatabaseRecoveryModelRepository _repository;

    public DatabaseRecoveryModelService(IDatabaseRecoveryModelRepository repository)
    {
        _repository = repository;
    }

    public Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public async Task SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)> changes, CancellationToken ct = default)
    {
        foreach (var (databaseName, newRecoveryModel) in changes)
            await _repository.SetRecoveryModelAsync(databaseName, newRecoveryModel, ct);
    }
}
```

- [ ] **Step 5：執行測試確認通過**

```
dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~DatabaseRecoveryModelServiceTests" -v minimal
```

預期：`PASSED`（3 個測試全通過）

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Application/Services/IDatabaseRecoveryModelService.cs \
        src/Specurai.Application/Services/DatabaseRecoveryModelService.cs \
        tests/Specurai.Application.Tests/Services/DatabaseRecoveryModelServiceTests.cs
git commit -m "feat(application): 新增 DatabaseRecoveryModelService"
```

---

## Task 3：Infrastructure Repository

**Files:**
- 新增：`src/Specurai.Infrastructure/Repositories/DatabaseRecoveryModelRepository.cs`

> 注意：Infrastructure Repository 需要真實資料庫連線，不撰寫自動化測試，僅手動驗證（與其他 Repository 一致）。

- [ ] **Step 1：建立 Repository 實作**

```csharp
// src/Specurai.Infrastructure/Repositories/DatabaseRecoveryModelRepository.cs
using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// 資料庫 Recovery Model 資料存取 Repository
/// </summary>
public class DatabaseRecoveryModelRepository : IDatabaseRecoveryModelRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public DatabaseRecoveryModelRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return [];

        const string sql = @"
SELECT
    name AS DatabaseName,
    recovery_model_desc AS RecoveryModel
FROM sys.databases
ORDER BY name;";

        await using var conn = new SqlConnection(connectionString);
        return await conn.QueryAsync<DatabaseRecoveryModel>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task SetRecoveryModelAsync(string databaseName, string recoveryModel, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        // databaseName 僅來自 sys.databases 查詢結果，不接受使用者直接輸入
        var sql = recoveryModel == "SIMPLE"
            ? $"ALTER DATABASE [{databaseName}] SET RECOVERY SIMPLE;"
            : $"ALTER DATABASE [{databaseName}] SET RECOVERY FULL;";

        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }
}
```

- [ ] **Step 2：確認建置通過**

```
dotnet build src/Specurai.Infrastructure --nologo -clp:ErrorsOnly
```

預期：`Build succeeded`

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/DatabaseRecoveryModelRepository.cs
git commit -m "feat(infrastructure): 新增 DatabaseRecoveryModelRepository"
```

---

## Task 4：Desktop ViewModel

**Files:**
- 新增：`src/Specurai.Desktop/ViewModels/RecoveryModelRowViewModel.cs`
- 新增：`src/Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs`
- 新增：`tests/Specurai.Desktop.Tests/ViewModels/RecoveryModelDocumentViewModelTests.cs`

- [ ] **Step 1：撰寫失敗測試**

```csharp
// tests/Specurai.Desktop.Tests/ViewModels/RecoveryModelDocumentViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Tests.ViewModels;

public class RecoveryModelDocumentViewModelTests
{
    private readonly IDatabaseRecoveryModelService _service;

    public RecoveryModelDocumentViewModelTests()
    {
        _service = Substitute.For<IDatabaseRecoveryModelService>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new RecoveryModelDocumentViewModel();

        vm.Should().NotBeNull();
        vm.Title.Should().Be("Recovery Model 管理");
        vm.Icon.Should().Be("🔧");
        vm.DocumentType.Should().Be("RecoveryModel");
        vm.Rows.Should().BeEmpty();
        vm.IsLoading.Should().BeFalse();
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void HasChanges_無變更時_應為false()
    {
        var vm = new RecoveryModelDocumentViewModel();

        vm.HasChanges.Should().BeFalse();
    }
}

public class RecoveryModelRowViewModelTests
{
    [Fact]
    public void IsDirty_未變更時_應為false()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");

        row.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsDirty_變更SelectedRecoveryModel後_應為true()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");

        row.SelectedRecoveryModel = "SIMPLE";

        row.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_變更後還原原值_應為false()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");
        row.SelectedRecoveryModel = "SIMPLE";

        row.SelectedRecoveryModel = "FULL";

        row.IsDirty.Should().BeFalse();
    }
}
```

- [ ] **Step 2：執行測試確認失敗**

```
dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~RecoveryModel" -v minimal
```

預期：`FAILED`（找不到 ViewModel 類別）

- [ ] **Step 3：建立 RecoveryModelRowViewModel**

```csharp
// src/Specurai.Desktop/ViewModels/RecoveryModelRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Recovery Model 清單中的單一資料庫列
/// </summary>
public partial class RecoveryModelRowViewModel : ViewModelBase
{
    public string DatabaseName { get; }
    public string OriginalRecoveryModel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedRecoveryModel;

    public bool IsDirty => SelectedRecoveryModel != OriginalRecoveryModel;

    public RecoveryModelRowViewModel(string databaseName, string recoveryModel)
    {
        DatabaseName = databaseName;
        OriginalRecoveryModel = recoveryModel;
        _selectedRecoveryModel = recoveryModel;
    }
}
```

- [ ] **Step 4：建立 RecoveryModelDocumentViewModel**

```csharp
// src/Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Recovery Model 管理文件 ViewModel
/// </summary>
public partial class RecoveryModelDocumentViewModel : DocumentViewModel
{
    private readonly IDatabaseRecoveryModelService? _service;

    public override string DocumentType => "RecoveryModel";
    public override string DocumentKey => DocumentType;

    public ObservableCollection<RecoveryModelRowViewModel> Rows { get; } = [];

    public bool HasChanges => Rows.Any(r => r.IsDirty);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>確認對話框回呼（由 MainWindowViewModel 設定）</summary>
    public Func<string, Task<bool>>? ConfirmCallback { get; set; }

    public RecoveryModelDocumentViewModel()
    {
        Title = "Recovery Model 管理";
        Icon = "🔧";
    }

    public RecoveryModelDocumentViewModel(IDatabaseRecoveryModelService service) : this()
    {
        _service = service;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_service == null) return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var items = await _service.GetAllAsync(ct);
            Rows.Clear();

            foreach (var item in items)
                Rows.Add(new RecoveryModelRowViewModel(item.DatabaseName, item.RecoveryModel));

            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = $"已載入 {Rows.Count} 個資料庫";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_service == null) return;

        var dirty = Rows.Where(r => r.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var summary = string.Join("\n", dirty.Select(r =>
            $"  • {r.DatabaseName}：{r.OriginalRecoveryModel} → {r.SelectedRecoveryModel}"));
        var message = $"即將變更以下 {dirty.Count} 個資料庫的 Recovery Model：\n{summary}";

        if (ConfirmCallback != null)
        {
            var confirmed = await ConfirmCallback(message);
            if (!confirmed) return;
        }

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var changes = dirty.Select(r => (r.DatabaseName, r.SelectedRecoveryModel));
            await _service.SaveChangesAsync(changes, ct);
            await LoadAsync(ct);
            StatusMessage = $"已成功變更 {dirty.Count} 個資料庫";
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnIsLoadingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    public void NotifyHasChanges() => OnPropertyChanged(nameof(HasChanges));
}
```

- [ ] **Step 5：執行測試確認通過**

```
dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~RecoveryModel" -v minimal
```

預期：`PASSED`（5 個測試全通過）

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/RecoveryModelRowViewModel.cs \
        src/Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs \
        tests/Specurai.Desktop.Tests/ViewModels/RecoveryModelDocumentViewModelTests.cs
git commit -m "feat(desktop): 新增 RecoveryModelDocumentViewModel 與 RowViewModel"
```

---

## Task 5：Desktop View（AXAML）

**Files:**
- 新增：`src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml`
- 新增：`src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs`

- [ ] **Step 1：建立 code-behind**

```csharp
// src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class RecoveryModelDocumentView : UserControl
{
    public RecoveryModelDocumentView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2：建立 AXAML View**

```xml
<!-- src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Specurai.Desktop.ViewModels"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="500"
             x:Class="Specurai.Desktop.Views.RecoveryModelDocumentView"
             x:DataType="vm:RecoveryModelDocumentViewModel">

    <Design.DataContext>
        <vm:RecoveryModelDocumentViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto">

        <!-- 工具列 -->
        <Border Grid.Row="0"
                Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}"
                Padding="10,8">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Command="{Binding LoadCommand}">
                    <StackPanel Orientation="Horizontal" Spacing="5">
                        <TextBlock Text="🔄" FontSize="14"/>
                        <TextBlock Text="重新整理"/>
                    </StackPanel>
                </Button>
                <Separator/>
                <TextBlock VerticalAlignment="Center" IsVisible="{Binding HasChanges}">
                    <Run Text="已變更 "/>
                    <Run Text="{Binding DirtyCount}"/>
                    <Run Text=" 筆"/>
                </TextBlock>
                <Button Command="{Binding SaveCommand}"
                        IsEnabled="{Binding HasChanges}">
                    <StackPanel Orientation="Horizontal" Spacing="5">
                        <TextBlock Text="💾" FontSize="14"/>
                        <TextBlock Text="儲存變更"/>
                    </StackPanel>
                </Button>
            </StackPanel>
        </Border>

        <!-- DataGrid -->
        <DataGrid Grid.Row="1"
                  x:Name="RecoveryModelGrid"
                  ItemsSource="{Binding Rows}"
                  AutoGenerateColumns="False"
                  CanUserReorderColumns="False"
                  CanUserResizeColumns="True"
                  IsReadOnly="False"
                  LoadingRow="RecoveryModelGrid_LoadingRow"
                  SelectionChanged="RecoveryModelGrid_SelectionChanged">
            <DataGrid.Columns>
                <DataGridTextColumn Header="資料庫名稱"
                                    Binding="{Binding DatabaseName}"
                                    IsReadOnly="True"
                                    Width="*"/>
                <DataGridTemplateColumn Header="Recovery Model" Width="180">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate x:DataType="vm:RecoveryModelRowViewModel">
                            <ComboBox SelectedItem="{Binding SelectedRecoveryModel}"
                                      HorizontalAlignment="Stretch"
                                      Margin="4,2">
                                <ComboBox.Items>
                                    <x:String>SIMPLE</x:String>
                                    <x:String>FULL</x:String>
                                </ComboBox.Items>
                            </ComboBox>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <!-- 狀態列 -->
        <Border Grid.Row="2"
                Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}"
                Padding="10,4">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0"
                           Text="{Binding StatusMessage}"
                           VerticalAlignment="Center"
                           FontSize="12"
                           Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"/>
                <ProgressBar Grid.Column="1"
                             IsIndeterminate="True"
                             IsVisible="{Binding IsLoading}"
                             Width="100" Height="4"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 3：更新 code-behind 加入 LoadingRow 事件（行著色）**

將 `RecoveryModelDocumentView.axaml.cs` 改為：

```csharp
// src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs
using Avalonia.Controls;
using Avalonia.Media;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class RecoveryModelDocumentView : UserControl
{
    public RecoveryModelDocumentView()
    {
        InitializeComponent();
    }

    private void RecoveryModelGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is RecoveryModelRowViewModel row)
        {
            e.Row.Foreground = row.IsDirty
                ? new SolidColorBrush(Color.Parse("#f38ba8"))
                : null;
        }
    }
}
```

- [ ] **Step 4：在 RecoveryModelRowViewModel 的 OnSelectedRecoveryModelChanged 通知父 ViewModel**

說明：`HasChanges` 依賴 `Rows.Any(r => r.IsDirty)`，需要在 Row 變更時通知父 ViewModel。改用事件通知模式。

在 `RecoveryModelRowViewModel.cs` 新增：

```csharp
public event EventHandler? DirtyChanged;

partial void OnSelectedRecoveryModelChanged(string value)
{
    DirtyChanged?.Invoke(this, EventArgs.Empty);
}
```

在 `RecoveryModelDocumentViewModel.cs`：

1. 新增計算屬性（與 `HasChanges` 放在一起）：

```csharp
public int DirtyCount => Rows.Count(r => r.IsDirty);
```

2. `NotifyHasChanges()` 同時通知兩個屬性：

```csharp
public void NotifyHasChanges()
{
    OnPropertyChanged(nameof(HasChanges));
    OnPropertyChanged(nameof(DirtyCount));
}
```

3. `LoadAsync` 中，`Rows.Add` 前先訂閱事件：

```csharp
var row = new RecoveryModelRowViewModel(item.DatabaseName, item.RecoveryModel);
row.DirtyChanged += (_, _) => NotifyHasChanges();
Rows.Add(row);
```

4. 移除 `OnIsLoadingChanged` 方法（`SaveCommand` 的 CanExecute 依賴 `HasChanges`，無需監聽 `IsLoading`）。

- [ ] **Step 5：code-behind 加入 SelectionChanged 事件（行著色 refresh）**

因 DataGrid 在 ComboBox 值改變時不會自動觸發 LoadingRow，需訂閱 SelectionChanged 重整行顏色。將 code-behind 改為：

```csharp
// src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs
using Avalonia.Controls;
using Avalonia.Media;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class RecoveryModelDocumentView : UserControl
{
    public RecoveryModelDocumentView()
    {
        InitializeComponent();
    }

    private void RecoveryModelGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        => ApplyRowColor(e.Row);

    private void RecoveryModelGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
            foreach (var item in grid.ItemsSource?.OfType<RecoveryModelRowViewModel>() ?? [])
            {
                var container = grid.ContainerFromItem(item) as DataGridRow;
                if (container != null) ApplyRowColor(container);
            }
    }

    private static void ApplyRowColor(DataGridRow row)
    {
        if (row.DataContext is RecoveryModelRowViewModel vm)
            row.Foreground = vm.IsDirty ? new SolidColorBrush(Color.Parse("#f38ba8")) : null;
    }
}
```

- [ ] **Step 7：確認建置通過**

```
dotnet build src/Specurai.Desktop --nologo -clp:ErrorsOnly
```

預期：`Build succeeded`

- [ ] **Step 8：Commit**

```bash
git add src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml \
        src/Specurai.Desktop/Views/RecoveryModelDocumentView.axaml.cs \
        src/Specurai.Desktop/ViewModels/RecoveryModelRowViewModel.cs \
        src/Specurai.Desktop/ViewModels/RecoveryModelDocumentViewModel.cs
git commit -m "feat(desktop): 新增 RecoveryModelDocumentView"
```

---

## Task 6：DI 註冊、選單整合、程式進入點

**Files:**
- 修改：`src/Specurai.Desktop/Program.cs`
- 修改：`src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`
- 修改：`src/Specurai.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1：在 Program.cs 註冊 Repository、Service、ViewModel**

在 `ConfigureServices()` 中加入（建議放在其他 Repository 附近）：

```csharp
// Recovery Model
services.AddSingleton<IDatabaseRecoveryModelRepository>(sp =>
    new DatabaseRecoveryModelRepository(
        () => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
services.AddSingleton<IDatabaseRecoveryModelService>(sp =>
    new DatabaseRecoveryModelService(
        sp.GetRequiredService<IDatabaseRecoveryModelRepository>()));
services.AddTransient<RecoveryModelDocumentViewModel>(sp =>
    new RecoveryModelDocumentViewModel(
        sp.GetRequiredService<IDatabaseRecoveryModelService>()));
```

同時補上所需 using（若尚未有）：

```csharp
using Specurai.Infrastructure.Repositories;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
```

- [ ] **Step 2：在 MainWindowViewModel.cs 新增 OpenRecoveryModel 命令**

在 `MainWindowViewModel` 類別中新增（參考 `OpenMaintenancePlan` 的模式）：

```csharp
[RelayCommand]
private async Task OpenRecoveryModel()
{
    var existing = Documents.OfType<RecoveryModelDocumentViewModel>().FirstOrDefault();
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var doc = App.Services?.GetRequiredService<RecoveryModelDocumentViewModel>()
        ?? new RecoveryModelDocumentViewModel();

    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        doc.ConfirmCallback = async (message) =>
        {
            var result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "確認變更 Recovery Model",
                    message,
                    ButtonEnum.YesNo,
                    Icon.Warning)
                .ShowWindowDialogAsync(desktop.MainWindow!);
            return result == ButtonResult.Yes;
        };
    }

    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
    await doc.LoadCommand.ExecuteAsync(null);
}
```

> 注意：若專案尚未使用 `MsBox.Avalonia`，請改用現有的 `ConfirmSaveCallback` 模式（參考 `UsageAnalysisDocumentViewModel`）。確認現有專案用什麼 MessageBox 套件後調整。

- [ ] **Step 3：確認現有 MessageBox 套件**

```
grep -r "MessageBox\|MsBox" src/Specurai.Desktop --include="*.cs" -l
```

若找到 `MsBox.Avalonia`，使用 Step 2 的寫法。
若找到其他套件或自訂實作，參照相同模式。

- [ ] **Step 4：在 MainWindow.axaml 新增選單項目**

在 `<MenuItem Header="資料庫維護計劃(_N)" .../>` 之後新增：

```xml
<MenuItem Header="Recovery Model 管理(_R)" Command="{Binding OpenRecoveryModelCommand}" IsEnabled="{Binding IsConnected}"
          ToolTip.Tip="查看並調整所有資料庫的 Recovery Model">
    <MenuItem.Icon>
        <TextBlock Text="🔧" FontSize="14"/>
    </MenuItem.Icon>
</MenuItem>
```

- [ ] **Step 5：確認建置通過**

```
dotnet build --nologo -clp:ErrorsOnly
```

預期：`Build succeeded`

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Desktop/Program.cs \
        src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs \
        src/Specurai.Desktop/Views/MainWindow.axaml
git commit -m "feat(desktop): 整合 Recovery Model 管理至主選單與 DI"
```

---

## Task 7：執行全套測試並驗證

- [ ] **Step 1：執行全部測試**

```
dotnet test --nologo
```

預期輸出：所有測試通過，無失敗

- [ ] **Step 2：執行應用程式手動驗證**

```
dotnet run --project src/Specurai.Desktop
```

驗證清單：
1. 連線後，主選單「工具」下出現「Recovery Model 管理(_R)」
2. 點擊後開啟文件頁，自動載入資料庫清單
3. 狀態列顯示「已載入 N 個資料庫」
4. 修改任一 ComboBox → 該列文字變紅、工具列顯示「已變更 1 筆」、「儲存變更」按鈕啟用
5. 按「儲存變更」→ 出現確認對話框，列出變更項目
6. 確認後執行，完成後重新載入清單，變更項目紅色消失
7. 重複開啟同一頁不會產生第二個頁籤

- [ ] **Step 3：最終 Commit（若有任何修正）**

```bash
git add -p  # 逐一確認修正內容
git commit -m "fix(desktop): 調整 Recovery Model 細節"
```
