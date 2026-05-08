---
description: 執行桌面應用程式。Use when 使用者說「執行」「啟動」「跑」「run」「launch」「啟動應用程式」「打開桌面 App」「dotnet run」或要求啟動 Specurai 桌面程式。
---

# 執行應用程式

啟動 Specurai 桌面應用程式。

## 步驟

1. 執行應用程式：
   ```bash
   dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj
   ```

2. 應用程式會在新視窗中開啟。

## 注意事項

- 需要先設定資料庫連線才能使用完整功能
- 連線設定儲存於使用者 AppData 目錄
