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
dotnet publish src/TableSpec.Desktop -c Release -r {rid} --self-contained -p:PublishSingleFile=true
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

## 輸出位置

發布的執行檔位於：
```
src/TableSpec.Desktop/bin/Release/net8.0/{rid}/publish/
```

## 範例

- `/publish` - 自動偵測平台並發布
- `/publish win` - 發布 Windows x64 版本
- `/publish osx-arm64` - 發布 macOS Apple Silicon 版本
- `/publish linux` - 發布 Linux x64 版本
