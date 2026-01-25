# Repository Guidelines

## 專案結構與模組
程式碼集中於 `src/`，採 Clean Architecture 分層：`TableSpec.Domain`（實體與介面）、`TableSpec.Application`（應用服務）、`TableSpec.Infrastructure`（資料存取與外部整合）、`TableSpec.Desktop`（Avalonia UI 與 ViewModels）。測試位於 `tests/` 下對應的 `*.Tests` 專案；文件在 `docs/`；協作與 CI 設定在 `.github/`。

## 建置、測試與開發指令
- `dotnet build`：建置整個解決方案。
- `dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj`：啟動桌面應用程式。
- `dotnet test`：執行全部測試。
- `dotnet test tests/TableSpec.Application.Tests`：執行特定測試專案。
- `dotnet publish src/TableSpec.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`：產出 Windows 單檔發佈版本。

## 程式風格與命名
依 `.editorconfig`：C#、XAML/AXAML 使用 4 空格縮排；JSON/XML/YAML 使用 2 空格；行尾使用 LF。公開型別採 PascalCase、區域變數採 camelCase、介面以 `I` 前綴，檔名需與型別一致。使用 `dotnet format` 進行格式化。函式級別註解與重要變數註解需使用中文。

## 測試規範
測試框架為 xUnit，常用 NSubstitute 與 FluentAssertions。測試檔命名為 `*Tests.cs`，依層次放置在 `tests/<Project>.Tests/`。新增功能需補足核心流程與錯誤分支測試。

## Commit 與 Pull Request
Commit 訊息以中文、動詞開頭（如「新增」「修正」「整理」），保持簡短且聚焦。PR 需包含變更摘要、測試結果，UI 變更需附截圖；若有 Issue，請附連結。

## 安全與設定
連線設定會儲存在 `%APPDATA%\\TableSpec\\connections.json`，不得提交任何憑證或敏感設定。UI 文字、程式註解與 Commit 訊息皆以繁體中文為主；檔案編碼為 UTF-8（無 BOM）。

## Agent 作業規範
新任務開始前先確認 `todolist.md` 並重置 CONTEXT WINDOW；任務需拆分且可獨立進行，開始與完成都要更新 `todolist.md`。任何模組需求變更需先更新 `spec.md`、`api.md`（RESTful），完成規劃並確認後再進入實作。
