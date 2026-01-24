---
description: 發布單一執行檔
---

# 發布應用程式

將應用程式發布為單一執行檔。

## 參數

- `$ARGUMENTS` - 目標平台 (win, mac, linux)，預設為 win

## 步驟

根據目標平台執行對應的發布指令：

### Windows (預設)
```bash
dotnet publish src/TableSpec.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### macOS
```bash
dotnet publish src/TableSpec.Desktop -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
```

### Linux
```bash
dotnet publish src/TableSpec.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## 輸出位置

發布的執行檔位於：
`src/TableSpec.Desktop/bin/Release/net8.0/{runtime}/publish/`

## 範例

- `/publish` - 發布 Windows 版本
- `/publish mac` - 發布 macOS 版本
- `/publish linux` - 發布 Linux 版本
