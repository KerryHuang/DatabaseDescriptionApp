---
description: 建立新的 Avalonia View
---

# 建立新的 View

在 Desktop 層建立新的 Avalonia AXAML View 和對應的 ViewModel。

## 參數

- `$ARGUMENTS` - View 名稱（例如：`SchemaCompareWindow`）

## 步驟

1. 確認 View 名稱，如果未提供則詢問使用者

2. 在 `src/TableSpec.Desktop/Views/` 建立 AXAML 檔案 `{ViewName}.axaml`

3. 在 `src/TableSpec.Desktop/Views/` 建立 Code-behind 檔案 `{ViewName}.axaml.cs`

4. 在 `src/TableSpec.Desktop/ViewModels/` 建立 ViewModel 檔案 `{ViewName}ViewModel.cs`

5. 在 `src/TableSpec.Desktop/Program.cs` 的 `ConfigureServices()` 方法中註冊 ViewModel

## AXAML 範本 (Window)

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:TableSpec.Desktop.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
        x:Class="TableSpec.Desktop.Views.{ViewName}"
        x:DataType="vm:{ViewName}ViewModel"
        Title="{視窗標題}"
        Width="800"
        Height="600"
        WindowStartupLocation="CenterOwner">

    <Design.DataContext>
        <vm:{ViewName}ViewModel />
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- 工具列 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="10">
            <Button Content="按鈕" Command="{Binding SomeCommand}" />
        </StackPanel>

        <!-- 主要內容 -->
        <Border Grid.Row="1" Margin="10">
            <!-- 內容區域 -->
        </Border>

        <!-- 狀態列 -->
        <Border Grid.Row="2" Background="{DynamicResource SemiGrey1}">
            <TextBlock Text="{Binding StatusMessage}" Margin="10,5" />
        </Border>
    </Grid>
</Window>
```

## Code-behind 範本

```csharp
using Avalonia.Controls;

namespace TableSpec.Desktop.Views;

/// <summary>
/// {View 說明}
/// </summary>
public partial class {ViewName} : Window
{
    public {ViewName}()
    {
        InitializeComponent();
    }
}
```

## ViewModel 範本

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// {ViewModel 說明}
/// </summary>
public partial class {ViewName}ViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = "就緒";

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public {ViewName}ViewModel()
    {
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public {ViewName}ViewModel(IService service)
    {
        // 初始化依賴
    }

    [RelayCommand]
    private void Some()
    {
        // 命令實作
    }
}
```

## DI 註冊範本

```csharp
// 在 ConfigureServices() 中加入
services.AddTransient<{ViewName}ViewModel>();
```

## 規範

- View 和 ViewModel 成對建立
- 使用 `x:DataType` 強型別綁定
- 提供 `Design.DataContext` 供設計時預覽
- UI 文字使用繁體中文
- ViewModel 提供無參數建構函式供設計時使用
