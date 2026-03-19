# 關於 Specurai 分頁設計

> 建立日期：2026-01-25

## 一、概述

將「說明 → 關於 Specurai」功能從狀態列訊息改為 MDI 分頁，提供完整的應用程式資訊、系統資訊、相依套件清單及授權條款。

## 二、頁面結構

### 2.1 佈局設計

```
┌─────────────────────────────────────────────────────────────┐
│  🗃️  Specurai                                              │
│      資料庫規格查詢工具                                       │
│                                                             │
│      版本：1.0.0                                             │
│      © 2024-2026 KerryHuang                                 │
│                                                             │
│      [🔗 GitHub]  [📄 授權條款]                              │
└─────────────────────────────────────────────────────────────┘

┌────────────────────────────┐  ┌────────────────────────────┐
│  📊 系統資訊                │  │  📦 相依套件                │
│  ────────────────────────  │  │  ────────────────────────  │
│  作業系統：Windows 11      │  │  Avalonia - 跨平台 UI 框架  │
│  .NET 版本：8.0.x          │  │  Semi.Avalonia - UI 主題   │
│  架構：x64                 │  │  CommunityToolkit.Mvvm     │
│                            │  │  Microsoft.Data.SqlClient  │
│                            │  │  Dapper - 輕量 ORM         │
│                            │  │  ClosedXML - Excel 匯出    │
│                            │  │  LiveChartsCore - 圖表元件 │
└────────────────────────────┘  └────────────────────────────┘
```

### 2.2 區塊說明

| 區塊 | 內容 |
|------|------|
| 應用程式資訊卡片 | 名稱、描述、版本、版權、操作按鈕 |
| 系統資訊 | 作業系統版本、.NET Runtime 版本、系統架構 |
| 相依套件 | 主要套件名稱與用途說明 |

## 三、技術實作

### 3.1 檔案結構

遵循 Clean Architecture 分層：

| 檔案 | 位置 | 說明 |
|------|------|------|
| `AboutDocumentView.axaml` | `src/Specurai.Desktop/Views/` | AXAML 視圖 |
| `AboutDocumentView.axaml.cs` | `src/Specurai.Desktop/Views/` | Code-behind |
| `AboutDocumentViewModel.cs` | `src/Specurai.Desktop/ViewModels/` | ViewModel |

### 3.2 ViewModel 設計

```csharp
public partial class AboutDocumentViewModel : DocumentViewModel
{
    // 應用程式資訊
    public string AppName => "Specurai";
    public string AppDescription => "資料庫規格查詢工具";
    public string Version { get; }      // 從 Assembly 讀取
    public string Copyright => "© 2024-2026 KerryHuang";

    // 系統資訊
    public string OsVersion { get; }        // Environment.OSVersion
    public string DotNetVersion { get; }    // Environment.Version
    public string Architecture { get; }     // RuntimeInformation.ProcessArchitecture

    // 相依套件
    public IReadOnlyList<DependencyInfo> Dependencies { get; }

    // 授權條款
    public string LicenseText { get; }

    [ObservableProperty]
    private bool _showLicense;

    // 命令
    [RelayCommand]
    private void OpenGitHub();      // 開啟瀏覽器

    [RelayCommand]
    private void ToggleLicense();   // 切換授權條款顯示
}

public record DependencyInfo(string Name, string Description);
```

### 3.3 DocumentViewModel 整合

| 屬性 | 值 |
|------|-----|
| `DocumentType` | `"About"` |
| `DocumentKey` | `"About"` （單一實例） |
| `Title` | `"關於"` |
| `Icon` | `"ℹ️"` |
| `CanClose` | `true` |

## 四、相依套件清單

| 套件名稱 | 用途說明 |
|---------|---------|
| Avalonia | 跨平台 UI 框架 |
| Semi.Avalonia | UI 主題樣式 |
| CommunityToolkit.Mvvm | MVVM 基礎設施 |
| Microsoft.Data.SqlClient | SQL Server 連線 |
| Dapper | 輕量 ORM |
| ClosedXML | Excel 匯出 |
| LiveChartsCore | 圖表元件 |

## 五、需修改的現有檔案

### 5.1 MainWindowViewModel.cs

修改 `ShowAbout()` 方法，改為開啟 MDI 分頁：

```csharp
[RelayCommand]
private void ShowAbout()
{
    var existing = Documents.FirstOrDefault(d => d.DocumentKey == "About");
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var aboutVm = new AboutDocumentViewModel();
    Documents.Add(aboutVm);
    SelectedDocument = aboutVm;
}
```

### 5.2 MainWindow.axaml

在 `TabControl.ContentTemplate` 中加入 DataTemplate：

```xml
<DataTemplate DataType="{x:Type vm:AboutDocumentViewModel}">
    <views:AboutDocumentView/>
</DataTemplate>
```

### 5.3 LICENSE.txt

修正版權資訊：

```diff
- Copyright (c) [year] [fullname]
+ Copyright (c) 2024-2026 KerryHuang
```

## 六、GitHub 連結

- Repository：https://github.com/KerryHuang/DatabaseDescriptionApp

## 七、授權條款

採用 MIT License，完整內容從 `LICENSE.txt` 讀取並顯示於「授權條款」展開區塊。
