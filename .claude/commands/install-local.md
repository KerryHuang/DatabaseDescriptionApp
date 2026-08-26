---
description: 從目前原始碼建置並安裝 Specurai 桌面 App／MCP Server／CLI 到本機（macOS 專用）。Use when 使用者說「安裝到本機」「install local」「更新我的 app」「更新本機應用程式」「裝到 Applications」「裝 MCP」「裝 CLI」或要求把最新程式碼更新到本機已安裝的元件。
---

# 本機安裝／更新

把目前原始碼建置成 self-contained 版本安裝到本機，讓使用者不需要 `dotnet run` 就能使用。

**僅支援 macOS。** Windows／Linux 請改用 `/publish`。

## 三個元件

| 元件 | 安裝位置 | 使用方式 |
|------|----------|----------|
| 桌面應用程式 | `/Applications/Specurai.app` | Spotlight、Launchpad、Dock |
| MCP Server | `~/Tools/SpecuraiMcp/Specurai.McpServer` | Claude Code 等 MCP 客戶端 |
| CLI | `~/.local/bin/specurai`（連結至 `~/.local/share/specurai/cli/`） | 終端執行 `specurai` |

三者共用 `~/Library/Application Support/Specurai/connections.json`，安裝後直接沿用既有連線設定。

## 參數

`$ARGUMENTS` 可包含元件選項與版本號：

- `--app` / `--mcp` / `--cli` - 只安裝指定元件，可組合；**未指定時三者全裝**
- 版本號（選填），例如 `1.24.0`
  - 未提供時自動取用最新 git tag（去掉開頭的 `v`）
  - 找不到 tag 時退回 `1.0.0`

## 步驟

執行安裝腳本即可，其餘細節腳本已處理：

```bash
./scripts/install-local-macos.sh {選項} {版本號}
```

腳本會依序完成：

1. 檢查平台為 macOS，並依 `uname -m` 決定 `osx-arm64` 或 `osx-x64`
2. 解析版本號（參數 > `VERSION` 環境變數 > 最新 git tag > `1.0.0`）
3. 對每個選定元件執行 `dotnet publish -c Release --self-contained` 到暫存目錄
4. **桌面 App**：以 `SKIP_DMG=1` 呼叫 `scripts/create-macos-bundle.sh` 打包（跳過 `.dmg`）；若舊版正在執行，先 `osascript quit` 請求正常結束，逾時 10 秒才強制終止；打包成功後才置換
5. **MCP／CLI**：以 rename 原子置換執行檔（直接覆蓋執行中的執行檔會因 `ETXTBSY` 失敗）
6. 清除 quarantine 屬性（ad-hoc 建置未經 Apple 公證，否則會被 Gatekeeper 攔截）
7. CLI 另建立 `~/.local/bin/specurai` 符號連結；若該目錄不在 PATH 會提示設定
8. MCP 若尚未註冊到 Claude Code，提示執行 `claude mcp add`

完成後回報各元件安裝路徑與大小。

## 注意事項

- 三者全裝約 5–8 分鐘（各約 100–135MB，publish 有增量快取）
- 全程使用暫存目錄，不在專案留下 `publish/` 等產物
- 使用者資料與 `.app` 分離，重裝不影響已設定的連線
- 安裝後是**當下原始碼的靜態快照**，之後改程式碼不會自動同步，需重跑本指令
- **MCP Server 更新後需重開 Claude Code** 才會載入新版本

## 與其他指令的差異

| 指令 | 用途 |
|------|------|
| `/run` | 開發除錯用，Debug 版、有 console 輸出 |
| `/publish` | 產生散布用的安裝包（`.dmg` / 單一執行檔） |
| `/install-local` | 更新本機自用的桌面 App、MCP Server、CLI |

## 範例

- `/install-local` - 三個元件全部更新
- `/install-local --mcp` - 只更新 MCP Server
- `/install-local --app --cli` - 只更新桌面 App 和 CLI
- `/install-local 1.24.0` - 三者全裝並指定版本號
