# 維護計劃頁：路徑欄位加入伺服器端資料夾選擇器 設計文件

- **日期**：2026-07-02
- **狀態**：設計已核准，待撰寫實作計畫
- **影響範圍**：Domain（新增一個純函式）、Desktop（沿用備份頁對話框、維護計劃 ViewModel/View）

## 1. 背景與目標

維護計劃精靈「步驟 1：基本設定」有兩個路徑欄位——**備份路徑**（預設 `D:\SQLBackup\`）與**還原路徑**（預設 `D:\sql_data\`），目前只能手動輸入。使用者希望比照剛完成的備份頁，加入 SSMS 式的**伺服器端**資料夾選擇器（`xp_dirtree`）。

本次要達成：備份路徑、還原路徑兩欄各加一顆「瀏覽…」按鈕，開啟伺服器端資料夾樹，選定資料夾後帶回欄位。

## 2. 現況調查重點（實作前已確認）

| 項目 | 位置 | 說明 |
|------|------|------|
| 維護計劃 ViewModel | `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs` | `BackupPath`（第 95 行）、`RestorePath`（第 99 行）皆為**資料夾路徑**（結尾帶分隔字元）。`SelectedPlatform`（Windows/Linux/其他）於 `OnSelectedPlatformChanged`（第 114-129 行）自動填入各平台預設路徑。已注入 `IConnectionManager`，建構時用 `GetCurrentProfile()`（第 301 行）取得目前連線。**尚未注入 `IBackupService`。** |
| 目前連線 | — | 維護計劃精靈作業對象為 App 目前連線（`_connectionManager.GetCurrentProfile()`），可取得連線字串供 `xp_dirtree` 使用。 |
| 現有對話框（可重用） | `src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs`、`Views/ServerFolderBrowserWindow.axaml(.cs)` | 備份頁已建立的伺服器端資料夾瀏覽對話框，惰性 `xp_dirtree` 樹狀載入。**目前為「資料夾＋檔名」模式**，`Confirm` 回傳 `ServerPathHelper.Combine(資料夾, 檔名)`。 |
| 伺服器查詢服務 | `IBackupService.ListServerDirectoryAsync` / `GetServerVolumesAsync`（Domain 介面，Infrastructure 實作 `MssqlBackupService`） | 已可跨平台列出磁碟與目錄。 |
| 路徑輔助 | `src/Specurai.Domain/ServerPathHelper.cs` | 已有 `Combine`/`GetSeparator`/`GetFileName`/`IsBackupFile`。 |

**關鍵差異**：維護計劃的兩個路徑是**資料夾**（如 `D:\SQLBackup\`），而現有對話框回傳「資料夾＋檔名」。因此需要對話框支援「僅選資料夾」模式。

## 3. 設計決策（與使用者確認）

| 決策 | 選定 |
|------|------|
| 對話框做法 | **沿用** `ServerFolderBrowserViewModel`，新增「僅選資料夾（folder-only）」模式（DRY，只維護一個對話框） |
| 套用欄位 | **備份路徑、還原路徑兩欄都加**「瀏覽…」 |
| 瀏覽對象 | App **目前連線**的伺服器；「平台」下拉只管預設路徑自動填入，與瀏覽獨立 |

## 4. 元件設計

### 4.1 `ServerFolderBrowserViewModel` 新增 folder-only 模式

建構函式新增選用參數（備份頁現有 3 參數呼叫不受影響）：

```csharp
public ServerFolderBrowserViewModel(
    IBackupService backupService,
    string connectionString,
    string initialFileName = "",
    bool folderOnly = false,
    string initialFolder = "")
```

- 新增欄位 `bool _folderOnly`；folder-only 時以 `initialFolder`（去尾分隔字元後）預帶 `SelectedPath`，讓使用者不導覽直接確定也有值。
- 新增 `Title` 屬性（綁定視窗標題）：folder-only →「選擇伺服器資料夾」，否則「尋找備份資料夾」。
- 新增計算屬性 `ShowFileName => !_folderOnly`。
- **檔案節點過濾**：folder-only 時，載入子項的 loader（`LoadChildrenAsync(path)`）只保留 `IsDirectory` 的項目（`.Where(e => e.IsDirectory)`），使樹只顯示資料夾。根節點（磁碟）本即為資料夾，不受影響。
- `Confirm` 分支：
  - folder-only：僅驗證 `SelectedPath` 非空；`ResultPath = ServerPathHelper.EnsureTrailingSeparator(SelectedPath)`。
  - 檔案模式（原行為）：驗證 `SelectedPath` 與 `FileName` 皆非空；`ResultPath = ServerPathHelper.Combine(SelectedPath, FileName)`。

### 4.2 `ServerFolderBrowserWindow.axaml`

- 「檔案名稱」列的 `IsVisible` 綁定 `ShowFileName`。
- 視窗 `Title` 綁定 `Title` 屬性（原為靜態字串）。
- 其餘結構不變。

### 4.3 Domain：`ServerPathHelper.EnsureTrailingSeparator(string path)`

```csharp
/// <summary>確保路徑結尾帶該平台的分隔字元。</summary>
public static string EnsureTrailingSeparator(string path)
{
    var sep = GetSeparator(path);
    return path.EndsWith(sep) ? path : path + sep;
}
```

（`EndsWith(char)` 於 .NET 8 可用。）

### 4.4 `MaintenancePlanDocumentViewModel`

- 建構函式注入 `IBackupService`（新增參數），並保存為欄位；同步更新 `Program.cs` 的 DI 註冊（加 `sp.GetRequiredService<IBackupService>()`）。設計時建構函式維持無參數。
- 新增兩個命令 `BrowseBackupPathCommand`、`BrowseRestorePathCommand`。共用一個私有方法，差別在讀寫哪個屬性：

```
private async Task BrowsePathAsync(bool isBackup)
{
    取 currentProfile = _connectionManager.GetCurrentProfile()
    若 null → StatusMessage = "請先選擇連線"; return
    connectionString = _connectionManager.GetConnectionString(currentProfile.Id)
    若空 → StatusMessage = "無法取得連線字串"; return

    initialFolder = isBackup ? BackupPath : RestorePath
    vm = new ServerFolderBrowserViewModel(_backupService, connectionString,
             folderOnly: true, initialFolder: initialFolder)
    dialog = new ServerFolderBrowserWindow(vm)
    owner = 取 MainWindow（IClassicDesktopStyleApplicationLifetime），null → return
    confirmed = await dialog.ShowDialog<bool>(owner)
    若 confirmed 且 vm.ResultPath 非空 →
        isBackup ? BackupPath = vm.ResultPath : RestorePath = vm.ResultPath
}
```

（開啟對話框沿用備份頁 Task 4 的視窗取得方式。）

### 4.5 `MaintenancePlanDocumentView.axaml`

- 備份路徑、還原路徑兩欄各加一顆「瀏覽…」按鈕，分別綁 `BrowseBackupPathCommand`、`BrowseRestorePathCommand`。維持既有版面（兩欄並排）；調整所在 Grid 欄位定義容納按鈕。

## 5. 錯誤處理

| 情境 | 行為 |
|------|------|
| 無目前連線 / 連線字串為空 | `StatusMessage` 提示，不開視窗、不崩潰 |
| `xp_dirtree` 權限不足 / 例外 | 對話框內既有機制顯示 `ErrorMessage`；使用者仍可手動輸入路徑 |
| 使用者未選資料夾即確定 | folder-only 驗證擋下並顯示對話框錯誤訊息 |

## 6. 測試

- **Domain**：`ServerPathHelper.EnsureTrailingSeparator`——已帶分隔字元不重複、未帶則補、Windows(`\`)/Unix(`/`) 兩種。
- **對話框 ViewModel（folder-only）**：`ShowFileName == false`；loader 過濾掉檔案節點（僅回傳資料夾）；`Confirm` 於僅選資料夾（無檔名）時成功並回傳帶結尾分隔字元的路徑；`initialFolder` 有預帶 `SelectedPath`。
- **對話框 ViewModel（檔案模式回歸）**：既有行為不變（`Combine(資料夾,檔名)`，需檔名）。
- **維護計劃 VM**：無目前連線時 browse 命令設定 `StatusMessage` 且不丟例外（開視窗的 happy path 靠建置＋手動驗證，與備份頁一致）。
- 命名 `[方法]_[條件]_[預期]`（繁體中文），xUnit + NSubstitute + FluentAssertions。

## 7. 範圍外（YAGNI）

- 不改維護計劃的 SQL 產生／執行邏輯。
- 不改「平台」下拉的預設路徑行為。
- 對話框不自動展開到 `initialFolder` 對應的樹節點（僅預帶 `SelectedPath` 文字；惰性樹維持手動展開）。
