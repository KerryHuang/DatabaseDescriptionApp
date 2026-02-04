# .claude 目錄說明

本目錄包含 Claude Code 的專案配置，提供指令碼、技能、代理、Hook 和規則等自動化工具。

## 目錄結構

```
.claude/
├── agents/          # 專門代理（程式碼審查、文件撰寫、測試執行）
├── commands/        # 斜線指令（/build、/test 等）
├── hooks/           # 自動化 Hook（建置驗證、測試驗證）
├── rules/           # 程式碼規則（架構、命名、模式）
├── skills/          # 技能範本（建立 Entity、Service 等）
├── settings.json    # 共用設定（權限）
└── settings.local.json  # 本機設定
```

---

## 斜線指令（commands/）

在 Claude Code 中輸入斜線指令即可執行。

| 指令 | 說明 | 參數 |
|------|------|------|
| `/build` | 建置整個解決方案 | 無 |
| `/test` | 執行單元測試 | 可選：測試篩選條件（如 `TableQueryService`） |
| `/run` | 啟動桌面應用程式 | 無 |
| `/publish` | 發布為單一執行檔 | 可選：目標平台（如 `win`、`osx-arm64`、`linux`） |
| `/commit` | 建立 Git commit（繁體中文訊息） | 可選：commit 訊息 |
| `/push` | 推送程式碼並建立版本 tag | 可選：版本號（如 `v1.0.5`） |

### 使用範例

```
/build                          # 建置解決方案
/test                           # 執行所有測試
/test TableQueryService         # 執行特定測試
/run                            # 啟動應用程式
/publish                        # 自動偵測平台並發布
/publish osx-arm64              # 發布 macOS Apple Silicon 版本
/commit                         # 自動產生 commit 訊息
/commit 修正連線測試的錯誤處理    # 指定 commit 訊息
/push                           # 推送並自動建議版本號
/push v1.1.0                    # 推送並建立指定版本 tag
```

---

## 技能（skills/）

技能是 Claude Code 在特定情境下自動使用的範本和流程指引。

| 技能 | 說明 | 參數 |
|------|------|------|
| `new-entity` | 建立新的 Domain Entity | Entity 名稱（如 `TriggerInfo`） |
| `new-repository` | 建立新的 Repository（介面 + 實作） | Repository 名稱（如 `Trigger`） |
| `new-service` | 建立新的 Application Service | Service 名稱（如 `SchemaCompareService`） |
| `new-viewmodel` | 建立新的 ViewModel | ViewModel 名稱（如 `TriggerDetail`） |
| `new-view` | 建立新的 Avalonia View + ViewModel | View 名稱（如 `SchemaCompareWindow`） |
| `new-test` | 建立新的單元測試 | 要測試的類別名稱（如 `TableQueryService`） |
| `new-feature` | 端對端建立新功能（Entity → Repository → ViewModel） | 功能名稱（如 `Trigger`） |
| `debug-issue` | 系統性除錯（TDD 方式追蹤根因） | 無（依問題描述執行） |

### 技能會建立的檔案

| 技能 | 建立的檔案位置 |
|------|---------------|
| `new-entity` | `src/TableSpec.Domain/Entities/{Name}.cs` |
| `new-repository` | `src/TableSpec.Domain/Interfaces/I{Name}Repository.cs`、`src/TableSpec.Infrastructure/Repositories/{Name}Repository.cs` |
| `new-service` | `src/TableSpec.Application/Services/I{Name}.cs`、`src/TableSpec.Application/Services/{Name}.cs` |
| `new-viewmodel` | `src/TableSpec.Desktop/ViewModels/{Name}ViewModel.cs` |
| `new-view` | `src/TableSpec.Desktop/Views/{Name}.axaml`、`Views/{Name}.axaml.cs`、`ViewModels/{Name}ViewModel.cs` |
| `new-test` | `tests/TableSpec.{Layer}.Tests/{Name}Tests.cs` |
| `new-feature` | 上述所有對應檔案 + DI 註冊 + 測試 |

---

## 代理（agents/）

代理是具備特定職責的專門助手，可在需要時呼叫。

| 代理 | 說明 | 使用時機 |
|------|------|---------|
| `code-reviewer` | 程式碼審查代理 | 完成功能實作後，審查是否符合 Clean Architecture、MVVM 模式和繁體中文規範。整合 Codex CLI 進行 AI 輔助審查。 |
| `documentation-writer` | 文件撰寫代理 | 需要撰寫或更新 XML 文件註解、README 或技術文件時。 |
| `test-runner` | 測試執行代理 | 需要執行測試、分析覆蓋率或撰寫新測試案例時。 |

---

## Hook（hooks/）

Hook 是在特定事件觸發時自動執行的指令。

| Hook | 觸發事件 | 說明 | 失敗處理 | 狀態 |
|------|---------|------|---------|------|
| `post-edit-build` | 編輯 `.cs` 檔案後 | 自動執行 `dotnet build --no-restore` 驗證建置 | 警告（不阻擋） | 停用 |
| `pre-commit-build` | Git commit 前 | 執行 `dotnet build --no-restore -warnaserror` 確保建置成功 | 阻擋 commit | 啟用 |
| `pre-commit-test` | Git commit 前 | 執行 `dotnet test --no-build --verbosity minimal` 確保測試通過 | 阻擋 commit | 啟用 |

---

## 規則（rules/）

規則是 Claude Code 在執行任務時自動載入的約束條件。

| 規則 | 說明 |
|------|------|
| `chinese-conventions` | 繁體中文慣例：UI 文字、程式碼註解、Git commit 訊息皆使用繁體中文 |
| `clean-architecture` | Clean Architecture 分層相依性規則和程式碼放置原則 |
| `cross-platform-scripts` | 跨平台腳本規範：路徑分隔符、換行符號、環境變數等 |
| `mvvm-patterns` | MVVM 模式規範：`[ObservableProperty]`、`[RelayCommand]`、ViewModelBase 繼承 |
| `repository-pattern` | Repository 模式規範：介面定義、Dapper 實作、連線字串工廠模式 |

---

## 設定檔

| 檔案 | 說明 |
|------|------|
| `settings.json` | 共用設定，定義 Claude Code 可自動執行的指令權限（如 `dotnet build`、`git` 操作等） |
| `settings.local.json` | 本機設定，不納入版本控制 |
