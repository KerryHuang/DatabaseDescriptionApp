# MVVM 模式規範

本專案使用 CommunityToolkit.Mvvm 實作 MVVM 模式。

## ViewModel 基本結構

所有 ViewModel 繼承自 `ViewModelBase`：

```csharp
public partial class MyViewModel : ViewModelBase
{
    // ...
}
```

## 可觀察屬性

使用 `[ObservableProperty]` 特性自動產生屬性：

```csharp
[ObservableProperty]
private string _name;

// 自動產生 Name 屬性和 OnNameChanged 部分方法
```

監聽屬性變更：

```csharp
partial void OnNameChanged(string value)
{
    // 屬性變更時執行
}
```

## 命令

使用 `[RelayCommand]` 特性自動產生命令：

```csharp
[RelayCommand]
private async Task SaveAsync()
{
    // 自動產生 SaveCommand
}

[RelayCommand(CanExecute = nameof(CanSave))]
private void Delete()
{
    // 自動產生 DeleteCommand，並連結 CanExecute
}

private bool CanSave => !string.IsNullOrEmpty(Name);
```

## 設計時支援

提供無參數建構函式供設計時使用：

```csharp
public MyViewModel()
{
    // Design-time constructor
}

public MyViewModel(IMyService service)
{
    _service = service;
}
```

## View 與 ViewModel 對應

- `MainWindow.axaml` → `MainWindowViewModel.cs`
- `ConnectionSetupWindow.axaml` → `ConnectionSetupViewModel.cs`
- View 透過 DI 取得 ViewModel 實例
