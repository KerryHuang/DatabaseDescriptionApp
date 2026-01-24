# 繁體中文慣例

本專案使用繁體中文作為主要語言。

## UI 文字

- 所有 UI 標籤、按鈕、訊息使用繁體中文
- 錯誤訊息使用繁體中文
- 狀態列訊息使用繁體中文

## 程式碼註解

- XML 文件註解使用繁體中文
- 行內註解使用繁體中文
- TODO/FIXME 註解使用繁體中文

## Git Commit

- Commit 訊息使用繁體中文
- 描述變更的目的和內容

## 範例

```csharp
/// <summary>
/// 資料庫物件查詢 Repository 實作
/// </summary>
public class TableRepository : ITableRepository
{
    // 取得連線字串
    private readonly Func<string?> _connectionStringProvider;
}
```

```csharp
StatusMessage = "正在載入物件清單...";
StatusMessage = "已載入 {count} 個物件";
StatusMessage = "匯出失敗: {error}";
```
