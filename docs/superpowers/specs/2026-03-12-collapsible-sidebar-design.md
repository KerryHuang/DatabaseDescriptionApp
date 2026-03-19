# 可收合側邊欄設計

## 概述

左側物件面板可透過標題列按鈕或鍵盤快捷鍵切換顯示/隱藏，效果類似 VS Code 的側邊欄。收合時面板完全消失，展開時平滑推擠右側內容區。狀態在應用程式重啟後保留。

## 需求

- 標題列新增一個切換按鈕（`☰` 漢堡選單圖示）
- 支援 `Ctrl+B` 鍵盤快捷鍵切換
- 點擊按鈕或按快捷鍵切換左側面板的顯示/隱藏
- 收合時左側面板完全消失（寬度為 0），內容區佔滿視窗
- 展開時左側面板推擠右側內容區（CompactInline 行為）
- 展開/收合過程有平滑動畫（約 200ms，CubicEaseOut）
- GridSplitter 隨面板同步顯示/隱藏
- 應用程式重啟後記住收合/展開狀態

## 設計

### ViewModel 變更

**MainWindowViewModel.cs：**

```csharp
[ObservableProperty]
private bool _isSidebarOpen = true;

[RelayCommand]
private void ToggleSidebar()
{
    IsSidebarOpen = !IsSidebarOpen;
}
```

狀態變更時透過現有的設定機制（或新增簡易設定檔）持久化 `IsSidebarOpen` 值。

### View 變更

**MainWindow.axaml：**

1. 標題列區域左側新增切換按鈕：

```xml
<Button Command="{Binding ToggleSidebarCommand}"
        Content="☰"
        ToolTip.Tip="切換側邊欄 (Ctrl+B)" />
```

2. 註冊 `Ctrl+B` 快捷鍵綁定 `ToggleSidebarCommand`

3. 動畫實作方式：不直接動畫 `ColumnDefinition.Width`（`GridLength` 不支援動畫），改為在左側面板的容器 `Border`/`Panel` 上設定 `Width` 屬性搭配 `Transitions`：
   - 展開：`Width="280"`（與現有預設一致）
   - 收合：`Width="0"`
   - `ColumnDefinition` 改為 `Width="Auto"` 讓它跟隨容器寬度

```xml
<Border.Transitions>
    <Transitions>
        <DoubleTransition Property="Width" Duration="0:0:0.2" Easing="CubicEaseOut" />
    </Transitions>
</Border.Transitions>
```

4. GridSplitter 的 `IsVisible` 綁定 `IsSidebarOpen`

5. 左側面板設定 `ClipToBounds="True"` 避免收合動畫時內容溢出

### 狀態持久化

透過現有的應用程式設定機制儲存 `IsSidebarOpen`，啟動時讀取還原狀態。

### 不需要改動的部分

- `ObjectTreeViewModel`、分組邏輯、搜尋功能維持不變
- 不引入 `SplitView` 控制項，維持現有 Grid 佈局
- 不新增窄邊欄或圖示列

## 檔案影響範圍

| 檔案 | 變更類型 |
|------|----------|
| `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` | 新增 `IsSidebarOpen` 屬性、`ToggleSidebarCommand`、狀態持久化 |
| `src/Specurai.Desktop/Views/MainWindow.axaml` | 標題列按鈕、快捷鍵、寬度綁定、動畫、GridSplitter 可見性 |
| `src/Specurai.Desktop/Views/MainWindow.axaml.cs` | 可能需要快捷鍵註冊的 code-behind |

## 測試計畫

- 驗證 `IsSidebarOpen` 預設為 `true`
- 驗證 `ToggleSidebarCommand` 正確切換狀態
- 驗證快速連續切換不會造成異常
- 驗證狀態持久化：關閉時為收合，重啟後仍為收合
- 手動驗證展開/收合動畫效果（約 200ms）
- 手動驗證收合時內容區佔滿、展開時推擠內容區
- 手動驗證 `Ctrl+B` 快捷鍵正常運作
