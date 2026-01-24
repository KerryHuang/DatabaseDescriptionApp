---
description: 建立新的 ViewModel
---

# 建立新的 ViewModel

在 Desktop 層建立新的 ViewModel，遵循 MVVM 模式。

## 參數

- `$ARGUMENTS` - ViewModel 名稱（例如：`TriggerDetail`，會產生 `TriggerDetailViewModel`）

## 步驟

1. 確認 ViewModel 名稱，如果未提供則詢問使用者

2. 在 `src/TableSpec.Desktop/ViewModels/` 建立 `{Name}ViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// {Name} ViewModel
/// </summary>
public partial class {Name}ViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Design-time constructor
    public {Name}ViewModel()
    {
    }

    // Runtime constructor with DI
    public {Name}ViewModel(/* 注入服務 */)
    {
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // TODO: 實作載入邏輯
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

3. 提醒使用者：
   - 在 `Program.cs` 註冊 ViewModel
   - 建立對應的 View（.axaml 檔案）

## 規範

- 繼承 `ViewModelBase`
- 使用 `partial class` 配合 source generator
- 使用 `[ObservableProperty]` 定義可觀察屬性
- 使用 `[RelayCommand]` 定義命令
- 提供無參數建構函式供設計時使用
