---
description: 發布單一執行檔（跨平台）
---

# 發布應用程式

將應用程式發布為單一執行檔，支援 Windows、macOS、Linux。

## 參數

- `$ARGUMENTS` - 目標平台，可選值：
  - `auto` - 自動偵測目前平台（預設）
  - `win` / `win-x64` / `win-arm64`
  - `mac` / `osx-x64` / `osx-arm64`
  - `linux` / `linux-x64` / `linux-arm64`

## 步驟

### 1. 判斷目標平台

如果參數為 `auto` 或未提供，根據目前作業系統決定：
- Windows → `win-x64`
- macOS → `osx-arm64`（Apple Silicon）或 `osx-x64`
- Linux → `linux-x64`

### 2. 執行發布指令

```bash
dotnet publish src/Specurai.Desktop -c Release -r {rid} --self-contained -p:PublishSingleFile=true
```

其中 `{rid}` 為 Runtime Identifier：

| 參數 | RID |
|------|-----|
| `win`, `win-x64` | win-x64 |
| `win-arm64` | win-arm64 |
| `mac`, `osx-x64` | osx-x64 |
| `mac-arm`, `osx-arm64` | osx-arm64 |
| `linux`, `linux-x64` | linux-x64 |
| `linux-arm64` | linux-arm64 |

### 3. macOS 額外打包

如果目標平台為 macOS（`osx-*`），在 dotnet publish 完成後額外執行：

```bash
./scripts/create-macos-bundle.sh {version} {rid} src/Specurai.Desktop/bin/Release/net8.0/{rid}/publish Releases
```

其中 `{version}` 從 `src/Specurai.Desktop/Specurai.Desktop.csproj` 的 `<Version>` 取得。

這會產生：
- `.app` bundle（macOS 原生應用程式格式）
- `.dmg` 安裝映像檔（使用者可直接點兩下安裝）

## 輸出位置

- **Windows / Linux**：`src/Specurai.Desktop/bin/Release/net8.0/{rid}/publish/`
- **macOS**：`Releases/Specurai-{version}-{rid}.dmg`

## 範例

- `/publish` - 自動偵測平台並發布
- `/publish win` - 發布 Windows x64 版本
- `/publish osx-arm64` - 發布 macOS Apple Silicon 版本
- `/publish linux` - 發布 Linux x64 版本
