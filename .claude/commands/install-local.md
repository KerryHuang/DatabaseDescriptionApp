---
description: 從目前原始碼建置並安裝 Specurai.app 到本機 /Applications（macOS 專用）。Use when 使用者說「安裝到本機」「install local」「更新我的 app」「更新本機應用程式」「裝到 Applications」或要求把最新程式碼更新到本機已安裝的桌面 App。
---

# 本機安裝／更新應用程式

把目前原始碼建置成 self-contained 的 `Specurai.app`，安裝到 `/Applications`，讓使用者可透過 Spotlight、Launchpad 或 Dock 直接開啟，不需要 `dotnet run`。

**僅支援 macOS。** Windows／Linux 請改用 `/publish`。

## 參數

- `$ARGUMENTS` - 版本號（選填），例如 `1.24.0`
  - 未提供時自動取用最新 git tag（去掉開頭的 `v`）
  - 找不到 tag 時退回 `1.0.0`

## 步驟

執行安裝腳本即可，其餘細節腳本已處理：

```bash
./scripts/install-local-macos.sh {version}
```

腳本會依序完成：

1. 檢查平台為 macOS，並依 `uname -m` 決定 `osx-arm64` 或 `osx-x64`
2. 解析版本號（參數 > `VERSION` 環境變數 > 最新 git tag > `1.0.0`）
3. `dotnet publish -c Release --self-contained` 到暫存目錄
4. 以 `SKIP_DMG=1` 呼叫 `scripts/create-macos-bundle.sh` 打包 `.app`（跳過 `.dmg`）
5. 若舊版正在執行，先 `osascript quit` 請求正常結束，逾時 10 秒才強制終止
6. 打包成功後才置換 `/Applications/Specurai.app`
7. 清除 quarantine 屬性（ad-hoc 簽署未經 Apple 公證，否則會被 Gatekeeper 攔截）

完成後回報安裝路徑與大小。

## 注意事項

- 耗時約 2–3 分鐘（self-contained 約 135MB，publish 有增量快取）
- 全程使用暫存目錄，不在專案留下 `publish/` 等產物
- 使用者資料位於 `~/Library/Application Support/Specurai`，與 `.app` 分離，重裝不影響已設定的連線
- 安裝後的 App 是**當下原始碼的靜態快照**，之後改程式碼不會自動同步，需重跑本指令

## 與其他指令的差異

| 指令 | 用途 |
|------|------|
| `/run` | 開發除錯用，Debug 版、有 console 輸出 |
| `/publish` | 產生散布用的安裝包（`.dmg` / 單一執行檔） |
| `/install-local` | 更新本機自用的 `/Applications/Specurai.app` |

## 範例

- `/install-local` - 以最新 git tag 的版本號安裝
- `/install-local 1.24.0` - 指定版本號安裝
