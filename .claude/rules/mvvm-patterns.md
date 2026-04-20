---
paths:
  - "**/*.axaml"
  - "**/*ViewModel.cs"
  - "**/*View.axaml.cs"
  - "**/*Window.axaml.cs"
---

# MVVM 模式規範

本專案使用 CommunityToolkit.Mvvm 實作 MVVM 模式。

## ViewModel 結構

- 所有 ViewModel 繼承自 `ViewModelBase`（`partial class`）
- 可觀察屬性：使用 `[ObservableProperty]` 特性（私有欄位 `_camelCase`）
- 命令：使用 `[RelayCommand]` 特性；`CanExecute` 連結布林屬性
- 屬性變更側效應：實作 `partial void OnXxxChanged(T value)`

## 設計時支援

每個 ViewModel 必須提供**無參數建構函式**（設計時用）和**DI 建構函式**（執行時用）。

## View 與 ViewModel 對應

- View 透過 DI 取得 ViewModel 實例
- AXAML Binding 盡量使用 `x:DataType` 編譯時綁定
- 避免 `MultiBinding` + `BoolConverters.And`，改用 ViewModel 計算屬性

## AXAML 特殊情境

- DataGrid 按鈕命令：`#DataGridName.((vm:Type)DataContext).CommandName`
- 行著色：code-behind `LoadingRow` 事件處理
