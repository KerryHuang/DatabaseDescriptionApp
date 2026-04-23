# Specurai 自動更新功能設計規格

- **建立日期**：2026-04-23
- **狀態**：設計階段
- **範圍**：Specurai.Desktop 桌面應用程式自動更新機制

## 1. 目標與範圍

### 1.1 目標

為 Specurai 桌面應用程式加入啟動時檢查新版本、非阻擋通知、使用者確認後下載並重啟套用的自動更新流程。

### 1.2 範圍內

- Windows：完整自動更新（Velopack）
- Linux：完整自動更新（Velopack）
- macOS：偵測新版本並顯示含下載連結與安裝指令的對話框，使用者手動完成後續步驟
- 檢查更新入口：啟動時自動檢查 + 「說明 → 檢查更新」手動觸發

### 1.3 範圍外（刻意不做）

- MCP Server 自動更新（不同發布管道，`dotnet tool update` 為主）
- 版本降級 / rollback（Velopack 支援但暫不需要）
- 差分更新設定（Velopack 預設即支援 delta）
- Beta / Pre-release 通道切換（YAGNI，未來需求再加）
- 檢查頻率節流快取（YAGNI，啟動檢查已足夠）
- macOS Velopack 自動更新（需 Apple 公證，成本不對等）

### 1.4 關鍵決策摘要

| 設計點 | 決策 |
|---|---|
| 支援平台 | Windows + Linux 全自動；macOS 提示手動下載 |
| 啟動時 UX | 非阻擋背景檢查，有新版時顯示徽章 |
| 提示位置 | 主視窗右上角（主題切換按鈕旁） |
| 更新流程 | 點擊後顯示 Release Notes → 確認更新 → 背景下載 → 提示重啟 |
| 檢查頻率 | 每次啟動都檢查 + 手動「檢查更新」入口 |
| macOS 降級 | 偵測 + 顯示對話框含下載連結與安裝步驟 |
| 版本通道 | 僅穩定版（略過 prerelease / draft） |
| 錯誤處理 | 靜默失敗，寫 trace log，不打擾使用者 |

## 2. 架構設計

### 2.1 Clean Architecture 分層對齊

```
Domain（最內層）
  └─ Entities/UpdateCheckResult.cs
        ├─ NewVersion    : string
        ├─ ReleaseNotes  : string
        ├─ ReleaseUrl    : string      // macOS 路徑使用
        └─ CanAutoApply  : bool        // Win/Linux=true, macOS=false

Application
  └─ Services/IUpdateService.cs
        ├─ CheckForUpdateAsync()                 : Task<UpdateCheckResult?>
        ├─ DownloadAsync(IProgress<int>)          : Task
        └─ ApplyAndRestart()                      : void

Infrastructure（依平台提供兩個實作）
  ├─ Services/VelopackUpdateService.cs           // Win/Linux
  ├─ Services/GitHubReleaseUpdateService.cs      // macOS
  └─ Services/UpdateServiceFactory.cs            // 依 OS 選實作

Desktop
  ├─ ViewModels/UpdateNotificationViewModel.cs   // 啟動檢查 + 徽章狀態
  ├─ ViewModels/UpdateDialogViewModel.cs         // Release Notes + 下載進度
  ├─ Views/UpdateDialog.axaml                    // Win/Linux 更新對話框
  ├─ Views/MacOsUpdateInstructionsDialog.axaml   // macOS 手動更新對話框
  └─ ViewModels/MainWindowViewModel.cs           // 觸發檢查 + 「檢查更新」命令
```

### 2.2 介面契約

```csharp
// Domain
public sealed class UpdateCheckResult
{
    public required string NewVersion { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string ReleaseUrl { get; init; }
    public required bool CanAutoApply { get; init; }
}

// Application
public interface IUpdateService
{
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default);
    Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    void ApplyAndRestart();
}
```

### 2.3 DI 註冊策略

於 `Specurai.Desktop/Program.cs` 的 `ConfigureServices()`：

```csharp
services.AddSingleton<IUpdateService>(sp =>
    UpdateServiceFactory.Create(/* IHttpClientFactory or raw deps */));
services.AddTransient<UpdateNotificationViewModel>();
services.AddTransient<UpdateDialogViewModel>();
```

`UpdateServiceFactory.Create()` 依 `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)` 決定回傳 `VelopackUpdateService` 或 `GitHubReleaseUpdateService`。

## 3. 使用者流程

### 3.1 Windows / Linux 路徑

```
App 啟動
  └─ MainWindow loaded
      └─ UpdateNotificationViewModel.CheckAsync()   [fire-and-forget]
          ├─ IUpdateService.CheckForUpdateAsync()
          │   └─ Velopack.UpdateManager.CheckForUpdatesAsync()
          └─ if (result != null)
                HasUpdate = true
                NewVersion = result.NewVersion
                → 右上角「⬆ 有新版本 v1.7.0」按鈕出現

使用者點擊按鈕
  └─ UpdateDialogViewModel 開啟
      ├─ 顯示版本號 + Release Notes
      └─ [確認更新]
          ├─ IUpdateService.DownloadAsync(progressHandler)
          │   └─ 進度條 0% → 100%
          └─ 下載完成 → 按鈕變「立即重啟」
              └─ IUpdateService.ApplyAndRestart()
```

### 3.2 macOS 路徑

```
App 啟動
  └─ GitHubReleaseUpdateService.CheckForUpdateAsync()
      ├─ GET https://api.github.com/repos/{owner}/{repo}/releases/latest
      ├─ 略過 prerelease == true 或 draft == true
      ├─ 比較 tag_name（去掉 "v"）與目前 assembly version
      └─ 若較新 → UpdateCheckResult { CanAutoApply = false }

使用者點擊按鈕
  └─ MacOsUpdateInstructionsDialog
      ├─ 顯示 Release Notes
      ├─ [下載 .dmg] → 開瀏覽器至 ReleaseUrl
      └─ 可複製的安裝步驟：
            1. 開啟 .dmg，將 Specurai 拖到 Applications
            2. xattr -cr /Applications/Specurai.app
```

### 3.3 手動觸發路徑

「說明 → 檢查更新」選單項目：

- 若已有偵測結果 → 開啟對應對話框
- 若尚未檢查 / 先前失敗 → 重跑 `CheckForUpdateAsync()`，顯示旋轉指示器
- 若無新版 → 顯示「已是最新版本 vX.Y.Z」Toast 或對話框

## 4. 錯誤處理

| 情境 | 處理 |
|---|---|
| 離線 / DNS 失敗 | catch `HttpRequestException` → `Trace.WriteLine` → `HasUpdate` 保持 false |
| GitHub API 429（rate limit） | catch → log → 靜默失敗 |
| 逾時（設 10 秒） | catch `TaskCanceledException` → log → 靜默失敗 |
| 下載中斷 | 對話框顯示「下載失敗，請稍後再試」，保留關閉按鈕 |
| Velopack 在 dev 環境執行 | `UpdateManager.IsInstalled == false` → 直接回 null（不動作） |
| macOS 收到 prerelease | 於 JSON 解析時過濾，視為「無新版」 |
| 使用者版本號異常（e.g. 1.0.0 vs tag v1.6.0） | 採用 `System.Version` 解析比對，若解析失敗則 log 並視為無更新 |

## 5. 測試策略

### 5.1 單元測試

| 元件 | 測試重點 | Mock |
|---|---|---|
| `GitHubReleaseUpdateService` | 有新版 / 無新版 / prerelease 須跳過 / 404 / 429 / 版本解析異常 | `HttpMessageHandler` |
| `UpdateServiceFactory` | 不同 OS 回傳正確實作型別 | — |
| `UpdateNotificationViewModel` | 檢查成功顯示徽章 / 檢查失敗不顯示 / 重複檢查去重 | `IUpdateService` |
| `UpdateDialogViewModel` | 下載進度更新 / 按鈕狀態切換 / 錯誤顯示 | `IUpdateService` |

### 5.2 手動煙霧測試

`VelopackUpdateService` 因 Velopack 內部型別不易 mock，採手動驗證：

1. 本機跑 `vpk pack -v 1.0.0-test` 產出舊版安裝包並安裝
2. 再跑 `vpk pack -v 1.1.0-test` 產出新版並放到本地 feed
3. 啟動舊版 App，驗證檢查、下載、重啟流程

### 5.3 測試覆蓋目標

- 新增單元測試目標：`GitHubReleaseUpdateService` 6 案例、`UpdateNotificationViewModel` 3 案例、`UpdateDialogViewModel` 3 案例、`UpdateServiceFactory` 2 案例（共 14 案例）
- 符合現有專案 TDD 慣例（xUnit + NSubstitute + FluentAssertions）

## 6. 實作前注意事項

1. **csproj 版本號過時**：`Specurai.Desktop.csproj` 目前 `<Version>1.0.0</Version>`，實際 tag 已到 v1.6.0。Velopack runtime 版本由 `vpk pack -v` 注入 assembly metadata，執行時不受 csproj 影響；但為避免開發環境 `Assembly.GetName().Version` 顯示錯誤，實作時同步讓 `release.yml` 於 `dotnet publish` 加上 `-p:Version=${{ steps.get-version.outputs.version }}`。

2. **Linux 打包格式**：`release.yml` 目前 `vpk pack` 於 Linux 產出的實際格式（AppImage / nupkg / .deb）需在實作階段實跑一次 release 驗證，並視情況補足。

3. **GitHub API 未認證速率**：若日後碰到 rate limit，再加 `User-Agent: Specurai-Updater/{version}` 與 `If-None-Match` ETag。

4. **Velopack GithubSource 設定**：`new GithubSource("https://github.com/{owner}/{repo}", accessToken: null, prerelease: false)`，repo 資訊可透過常數或 `appsettings.json` 集中管理。

5. **MainWindow AXAML 變更**：於頂部 `DockPanel.Dock="Top"` 的 Grid 右側欄（Column 1，目前是主題切換）新增一個 Button，`IsVisible` 綁 `UpdateNotificationViewModel.HasUpdate`。

## 7. 不在此次實作但需記錄的未來選項

- Apple 公證 + Velopack macOS 自動更新（需 Apple Developer 帳號 NT$3,000/年）
- Beta 通道（`Velopack.GithubSource` 的 `prerelease: true` + 使用者設定開關）
- 檢查頻率快取（若 GitHub API 碰到 rate limit）
- 更新失敗自動回滾（Velopack 支援）
- MCP Server 更新偵測整合（不同發布管道）

## 8. 驗收標準

- [ ] Windows 版啟動後右上角正確顯示「⬆ 有新版本」按鈕
- [ ] Linux 版相同行為
- [ ] macOS 版啟動後正確偵測新版，點擊按鈕顯示含下載連結與 xattr 指令的對話框
- [ ] 離線狀態啟動不跳錯誤、不拖慢 UI
- [ ] 「說明 → 檢查更新」可手動觸發檢查
- [ ] 於 Release Notes 對話框可看到 GitHub Release body 內容
- [ ] 完成下載後「立即重啟」確實重啟並套用新版本
- [ ] 所有新增的 ViewModel 皆提供設計時建構函式
- [ ] 新增單元測試全數通過，整體測試總數 ≥ 618（604 + 14）
