# Specurai

> 跨平台 SQL Server 資料庫規格查詢與管理工具。提供桌面 GUI（Avalonia）、命令列（CLI）、AI 助手整合（MCP Server）三種介面，共用同一組連線設定。

## 目錄

- [快速開始](#快速開始)
- [主要功能](#主要功能)
- [安裝](#安裝)
  - [桌面應用程式](#桌面應用程式-1)
  - [MCP Server](#mcp-server)
  - [CLI 命令列工具](#cli-命令列工具-1)
- [使用](#使用)
  - [桌面應用程式快捷鍵](#桌面應用程式快捷鍵)
  - [CLI 命令](#cli-命令)
  - [MCP Server 工具清單](#mcp-server-工具清單56-個)
- [技術架構](#技術架構)
- [從原始碼建置](#從原始碼建置)
- [連線設定共用位置](#連線設定共用位置)
- [文件](#文件)
- [授權](#授權)

---

## 快速開始

### 桌面應用程式

從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) 下載：

| 平台 | 檔案 |
|------|------|
| Windows x64 | `Specurai-win-Setup.exe`（安裝版）／`Specurai-win-Portable.zip`（免安裝） |
| macOS Apple Silicon | `Specurai-*-osx-arm64.dmg` |
| macOS Intel | `Specurai-*-osx-x64.dmg` |
| Linux x64 | `Specurai.AppImage` |

> macOS 首次開啟若出現「已損毀」：終端機執行 `xattr -cr /Applications/Specurai.app`。

### MCP Server（讓 AI 直接查資料庫）

macOS 三步完成（Apple Silicon）：

```bash
# 1. 下載 + 解壓
curl -LO https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/Specurai.McpServer-osx-arm64.tar.gz
mkdir -p ~/Tools/SpecuraiMcp && tar xzf Specurai.McpServer-osx-arm64.tar.gz -C ~/Tools/SpecuraiMcp

# 2. 補權限、去 Gatekeeper quarantine
chmod +x ~/Tools/SpecuraiMcp/Specurai.McpServer
xattr -dr com.apple.quarantine ~/Tools/SpecuraiMcp/Specurai.McpServer

# 3. 註冊到 Claude Code
claude mcp add specurai -s user -- ~/Tools/SpecuraiMcp/Specurai.McpServer
```

Windows、Intel Mac、Linux、以及 Claude Desktop/Cursor/Windsurf：見 [MCP Server 安裝](#mcp-server)。

### CLI 命令列工具

```bash
dotnet tool install -g Specurai.Cli
specurai tables list
```

---

## 主要功能

| 類別 | 功能摘要 | 快捷鍵 |
|------|----------|--------|
| 物件瀏覽 | Tables / Views / Procedures / Functions，含欄位、索引、關聯、參數、SQL 定義；MDI 多分頁 | — |
| 多連線管理 | 多組連線儲存、切換、匯入/匯出；CLI 支援 stdin 臨時連線不落地 | Ctrl+L |
| SQL 查詢 | 自訂 SQL、結果匯出 CSV、單欄位複製、查詢分頁連線獨立 | Ctrl+Q |
| 欄位搜尋 | 跨資料表搜尋、型態一致性三級警示、批次更新長度、套用說明 | Ctrl+F |
| 統計分析 | 欄位使用分析、資料表統計（列數、空間、圖表） | Ctrl+U / Ctrl+T |
| 備份與還原 | 完整/差異/交易記錄備份，伺服器端操作、備份驗證、歷史記錄 | Ctrl+Shift+B |
| 結構比對 | 跨環境 Schema 差異偵測、同步腳本、HTML/Excel 報表 | Ctrl+M |
| Schema Migration | 風險評估、多維度篩選、Dry Run、自動回滾、執行報告 | Ctrl+Shift+M |
| 健康監控 | CPU / 記憶體 / 磁碟 / 連線數，自動告警、趨勢圖，SQL Agent 排程 | Ctrl+H |
| 效能診斷 | 等候事件、耗時查詢、索引狀態、錯誤記錄 | Ctrl+P |
| 索引管理 | 缺少索引建議、未使用索引清理 | Ctrl+I / Ctrl+J |
| 維護計劃 | 精靈式建立 SQL Agent Job：備份、Recovery Model、使用者權限、保留天數 | Ctrl+Shift+D |
| Excel 匯出 | 全庫規格一鍵匯出 | Ctrl+E |
| MCP Server | 56 個 AI 工具，共用桌面連線設定，支援所有 MCP 客戶端 | — |
| 自動更新 | 啟動時背景檢查 GitHub Release，新版本以右上角徽章通知；Windows/Linux 一鍵更新，macOS 以對話框提供下載連結與 `xattr` 指令 | — |

詳細操作步驟請見 [docs/UserGuide.md](docs/UserGuide.md)。

---

## 安裝

### 桌面應用程式

見 [快速開始 § 桌面應用程式](#桌面應用程式)。從零開始的完整指引（含 macOS 安全性設定）：[docs/INSTALL.md](docs/INSTALL.md#二安裝桌面應用程式)。

### MCP Server

Specurai MCP Server 讓 AI 助手（Claude Code、Claude Desktop、Cursor、Windsurf 等）透過 [Model Context Protocol](https://modelcontextprotocol.io/) 直接存取資料庫結構。

> **完整指引：** [docs/INSTALL.md](docs/INSTALL.md) 含逐步安裝步驟，AI 助手可直接讀取該文件引導使用者安裝。

兩種安裝方式擇一：

| 方式 | 適合對象 | 前置需求 |
|------|----------|----------|
| [方式一：獨立執行檔](#方式一獨立執行檔免安裝-net) | 一般使用者 | 無 |
| [方式二：dotnet tool](#方式二dotnet-tool需已安裝-net-8-sdk) | 開發者、已有 .NET 環境 | .NET 8.0 SDK |

#### 方式一：獨立執行檔（免安裝 .NET）

從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) 下載對應平台：

| 平台 | 檔案 |
|------|------|
| Windows x64 | `Specurai.McpServer-win-x64.zip` |
| macOS Apple Silicon (M1/M2/M3/M4) | `Specurai.McpServer-osx-arm64.tar.gz` |
| macOS Intel | `Specurai.McpServer-osx-x64.tar.gz` |
| Linux x64 | `Specurai.McpServer-linux-x64.tar.gz` |

> macOS 不確定架構：執行 `uname -m`，`arm64` = Apple Silicon，`x86_64` = Intel。

**Windows**

1. 下載 `Specurai.McpServer-win-x64.zip`
2. 解壓到固定目錄，例如 `C:\Tools\SpecuraiMcp\`
3. 記下執行檔完整路徑：`C:\Tools\SpecuraiMcp\Specurai.McpServer.exe`

**macOS**

```bash
mkdir -p ~/Tools/SpecuraiMcp
tar xzf ~/Downloads/Specurai.McpServer-osx-arm64.tar.gz -C ~/Tools/SpecuraiMcp
chmod +x ~/Tools/SpecuraiMcp/Specurai.McpServer
# 瀏覽器下載的檔案會被打上 quarantine bit，首次執行會被 Gatekeeper 擋下
xattr -dr com.apple.quarantine ~/Tools/SpecuraiMcp/Specurai.McpServer
```

記下路徑：`/Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer`

> **若出現「無法驗證開發者」或「已損毀」：** 確認 `xattr -dr com.apple.quarantine` 已執行。

**Linux**

```bash
mkdir -p ~/Tools/SpecuraiMcp
tar xzf Specurai.McpServer-linux-x64.tar.gz -C ~/Tools/SpecuraiMcp
chmod +x ~/Tools/SpecuraiMcp/Specurai.McpServer
```

下載解壓完成後，跳到 [設定 MCP 客戶端](#設定-mcp-客戶端)。

#### 方式二：dotnet tool（需已安裝 .NET 8 SDK）

前置安裝 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

| 平台 | 安裝方式 |
|------|----------|
| Windows | `winget install Microsoft.DotNet.SDK.8`，或從官網下載 |
| macOS | `brew install dotnet@8`，或從官網下載 `.pkg` |
| Linux (Ubuntu/Debian) | `sudo apt install dotnet-sdk-8.0` |
| Linux (Fedora) | `sudo dnf install dotnet-sdk-8.0` |

驗證：`dotnet --version` 應顯示 `8.x.x`。

安裝 MCP Server：

```bash
dotnet tool install -g Specurai.McpServer
```

macOS / Linux 若出現 PATH 警告：

```bash
# macOS (zsh)
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile && source ~/.zprofile

# Linux (bash)
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc && source ~/.bashrc
```

驗證：執行 `specurai-mcp --help`。更新版本：`dotnet tool update -g Specurai.McpServer`。

#### 設定 MCP 客戶端

依 AI 客戶端選一種設定。

**Claude Code**

```bash
# 方式二（dotnet tool）
claude mcp add specurai -s user -- specurai-mcp

# 方式一（獨立執行檔）
#   Windows
claude mcp add specurai -s user -- "C:\Tools\SpecuraiMcp\Specurai.McpServer.exe"
#   macOS / Linux
claude mcp add specurai -s user -- /Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer
```

驗證：`claude mcp list` 應顯示 `specurai: Connected`。

**Claude Desktop / Cursor / Windsurf**

開啟對應設定檔（不存在需自行建立）：

| 客戶端 | Windows | macOS |
|--------|---------|-------|
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Cursor | `%APPDATA%\Cursor\mcp.json` | `~/Library/Application Support/Cursor/mcp.json` |
| Windsurf | `%APPDATA%\Windsurf\mcp_config.json` | `~/Library/Application Support/Windsurf/mcp_config.json` |

方式二（dotnet tool）：

```json
{
  "mcpServers": {
    "specurai": {
      "command": "specurai-mcp"
    }
  }
}
```

方式一（獨立執行檔）— Windows：

```json
{
  "mcpServers": {
    "specurai": {
      "command": "C:\\Tools\\SpecuraiMcp\\Specurai.McpServer.exe"
    }
  }
}
```

> Windows JSON 內的反斜線需 escape 為 `\\`，或改用正斜線 `C:/Tools/SpecuraiMcp/Specurai.McpServer.exe`。

方式一（獨立執行檔）— macOS / Linux：

```json
{
  "mcpServers": {
    "specurai": {
      "command": "/Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer"
    }
  }
}
```

儲存後重新啟動 AI 客戶端。在客戶端輸入「列出所有連線設定」若回傳清單即代表安裝成功。

#### 疑難排解

| 症狀 | 解決方式 |
|------|----------|
| macOS「無法驗證開發者」或「已損毀」 | 執行 `xattr -dr com.apple.quarantine <執行檔路徑>` |
| macOS `zsh: permission denied` | 執行 `chmod +x <執行檔路徑>` |
| `specurai-mcp: command not found`（方式二） | 將 `$HOME/.dotnet/tools` 加入 PATH，或改用方式一絕對路徑 |
| `dotnet tool install` 找不到套件 | NuGet 套件尚未發布時請改用方式一 |
| AI 客戶端連不到 MCP Server | 確認 JSON 語法、command 路徑絕對化、Windows 路徑用 `\\` 或 `/`、重啟客戶端 |

### CLI 命令列工具

```bash
dotnet tool install -g Specurai.Cli
```

或從原始碼執行：`dotnet run --project src/Specurai.Cli`。

---

## 使用

### 桌面應用程式快捷鍵

| 快捷鍵 | 功能 | 快捷鍵 | 功能 |
|--------|------|--------|------|
| Ctrl+L | 連線設定 | Ctrl+I | 缺少索引報表 |
| Ctrl+D | 切換深色/淺色主題 | Ctrl+J | 未使用索引報表 |
| Ctrl+Q | SQL 查詢 | Ctrl+U | 欄位統計 |
| Ctrl+F | 欄位搜尋 | Ctrl+T | 資料表統計 |
| Ctrl+M | 結構比對 | Ctrl+Shift+D | 資料庫維護計劃 |
| Ctrl+Shift+M | Schema Migration | Ctrl+Shift+B | 備份與還原 |
| Ctrl+H | 健康監控 | Ctrl+E | 匯出 Excel |
| Ctrl+P | 效能診斷 | Ctrl+Shift+E / Ctrl+Shift+I | 匯出/匯入連線設定 |
| F5 | 執行 SQL 查詢 | Ctrl+B | 切換側邊欄 |
| Ctrl+W / Ctrl+Shift+W | 關閉目前/所有分頁 | — | — |

功能詳細操作：[docs/UserGuide.md](docs/UserGuide.md)。

### CLI 命令

**連線設定**

```bash
# 互動式 / 參數式新增
specurai conn add
specurai conn add --name "正式環境" --server 192.168.1.100 --database MyDB --user sa --password P@ss

# 列出、切換、測試
specurai conn list
specurai conn switch "正式環境"
specurai conn test
```

**連線傳入方式（5 種擇一）**

| 方式 | 使用情境 |
|------|----------|
| CLI 參數（`--server/--database/--user/--password`） | 一次性執行 |
| 環境變數（`SPECURAI_SERVER` 等） | CI/CD |
| 連線字串（`--connection-string "..."`） | 特殊認證需求 |
| stdin JSON 持久化匯入（`conn import --stdin`） | 批次部署 |
| stdin JSON 臨時連線（`--conn-stdin`，不落地） | 自動化腳本、不在目標機器留設定 |

範例（`--conn-stdin` 多連線，跨環境比對）：

```bash
echo '[
  {"name":"DEV","server":"dev-srv","database":"MyDB","user":"sa","password":"pw1"},
  {"name":"PROD","server":"prod-srv","database":"MyDB","user":"sa","password":"pw2"}
]' | specurai --conn-stdin schema compare --base DEV --target PROD
```

> `--conn-stdin` 為 **in-memory 臨時 profile**，程序結束即消失，不寫入 `connections.json`；與 `conn import --stdin`（持久化儲存）不同。適合 CI/CD、不想在目標機器留下連線設定的場景。

**常用命令**

```bash
# 物件瀏覽
specurai tables list                          # 列出所有物件
specurai tables list --type TABLE             # 只列資料表
specurai tables columns dbo.Users             # 欄位
specurai tables indexes dbo.Users             # 索引
specurai tables definition dbo.GetUser        # SP 原始碼

# 描述編輯
specurai describe table dbo.Users "使用者資料表"
specurai describe column dbo.Users.Email "電子郵件"

# SQL 查詢與搜尋
specurai sql query "SELECT TOP 10 * FROM dbo.Users"
specurai sql search-columns Email --all-profiles

# 匯出
specurai export excel
specurai export excel --table dbo.Users

# 效能診斷 / 健康監控
specurai perf waits
specurai perf queries --top 10
specurai perf missing-indexes
specurai health status
specurai health alerts --days 7

# Schema 比對
specurai schema compare --base "正式" --target "測試"
specurai schema compare-multi --base "正式" --targets "客戶A,客戶B,客戶C"

# 使用分析
specurai usage scan --years 2
specurai usage compare --base "正式" --targets "客戶A,客戶B"

# Agent Job
specurai jobs list
specurai jobs start <jobId>
```

**JSON 輸出（AI Agent 友善）**

所有命令支援 `--json`，回傳 `{ success, data, metadata }` 結構化結果：

```bash
specurai --json tables list
specurai --json perf waits --top 5
specurai --json schema compare --base "正式" --target "測試"
```

### MCP Server 工具清單（56 個）

⚠️ 標記表示寫入或破壞性操作。

**連線管理**

| 工具 | 說明 |
|------|------|
| `list_connections` | 列出所有連線設定 |
| `switch_connection` | 切換目前連線 |
| `test_connection` | 測試連線 |
| `add_connection` / `update_connection` / `delete_connection` | 連線 CRUD ⚠️ |
| `export_connections` / `import_connections` | 匯出/匯入 JSON ⚠️（import） |

**資料表查詢**

| 工具 | 說明 |
|------|------|
| `list_tables` | 列出物件（可依類型篩選） |
| `get_columns` / `get_indexes` / `get_relations` / `get_parameters` | 欄位、索引、關聯、參數 |
| `get_definition` | 預存程序/函數 SQL 定義 |

**SQL 查詢**

| 工具 | 說明 |
|------|------|
| `execute_readonly_sql` | 唯讀 SQL |
| `search_columns` / `search_columns_multi_database` | 欄位名稱搜尋（單/多資料庫） |
| `get_create_table_sql` | 產生 CREATE TABLE |

**描述管理**

| 工具 | 說明 |
|------|------|
| `update_table_description` / `update_column_description` | 更新物件/欄位描述 |

**效能診斷**

| 工具 | 說明 |
|------|------|
| `get_wait_statistics` | 等候事件 |
| `get_expensive_queries` / `get_expensive_procedures` | 耗時查詢/SP |
| `get_missing_indexes` / `get_unused_indexes` | 索引建議/清理 |
| `get_error_log` | SQL Server 錯誤記錄 |

**健康監控**

| 工具 | 說明 |
|------|------|
| `get_health_install_status` / `get_health_status` | 安裝狀態、健康摘要 |
| `get_health_metrics` / `get_health_alerts` | 即時指標、告警 |
| `install_health_monitoring` / `uninstall_health_monitoring` | 安裝/移除 ⚠️ |
| `export_health_monitoring_sql` | 匯出安裝 SQL |

**統計資訊**

| 工具 | 說明 |
|------|------|
| `get_table_statistics` / `get_exact_row_count` | 表統計、精確列數 |
| `get_column_usage_statistics` | 欄位使用狀態 |

**Agent Job 管理**

| 工具 | 說明 |
|------|------|
| `list_agent_jobs` / `list_non_specurai_jobs` | 列出 Job |
| `get_agent_job_history` | 執行歷史 |
| `set_agent_job_enabled` / `start_agent_job` / `delete_agent_job` | 啟停、執行、刪除 ⚠️ |
| `update_agent_job_schedule` / `import_agent_job` | 排程、匯入 ⚠️ |

**Schema 比對 / 使用分析 / 維護計劃 / 匯出**

| 工具 | 說明 |
|------|------|
| `compare_schemas` / `compare_multiple_schemas` | 1 對 1、1 對多 Schema 比對 |
| `scan_usage` / `compare_usage_multi_environment` | 使用狀態掃描/多環境比對 |
| `generate_drop_table_sql` / `generate_drop_column_sql` | 產生 DROP 腳本（不執行） |
| `check_maintenance_prerequisites` / `check_maintenance_steps` | 維護計劃前置檢查 |
| `generate_maintenance_plan_sql` / `execute_maintenance_plan` | 產生/執行維護計劃 ⚠️（execute） |
| `export_all_to_excel` / `export_table_to_excel` | 全庫/單表 Excel 匯出 |

使用範例（在 Claude Code 中自然語言）：

- 「列出所有資料表」→ `list_tables`
- 「查看 Orders 表的欄位」→ `get_columns`
- 「找出所有包含 Price 的欄位」→ `search_columns`
- 「分析資料庫效能瓶頸」→ `get_wait_statistics` + `get_expensive_queries`

---

## 技術架構

### 分層

```
Domain → Application → Infrastructure
                    ↘ Desktop   (Avalonia UI)
                    ↘ McpServer (stdio console)
                    ↘ Cli       (命令列)
```

| 層級 | 職責 | 主要技術 |
|------|------|----------|
| Domain | 實體、Repository/Service 介面、Enums | Pure C# |
| Application | 業務邏輯、Service 實作 | 僅相依 Domain |
| Infrastructure | 資料存取、外部服務實作 | Dapper、Microsoft.Data.SqlClient、ClosedXML |
| Desktop | Avalonia UI、ViewModel | Avalonia 11.x、Semi.Avalonia、CommunityToolkit.Mvvm |
| McpServer | MCP stdio server | Microsoft.Extensions.Hosting、ModelContextProtocol SDK |
| Cli | 命令列工具 | System.CommandLine |

詳細架構規範：[CLAUDE.md](CLAUDE.md) 與 [.claude/rules/clean-architecture.md](.claude/rules/clean-architecture.md)。

### 資料庫物件涵蓋

資料表、檢視表、預存程序、函數（含欄位、索引、關聯、參數、SQL 定義），以及 SQL Agent Jobs（維護計劃管理）。

---

## 從原始碼建置

需要 .NET 8.0 SDK 與 SQL Server 2008 以上（支援 Windows 驗證或 SQL Server 驗證）。

```bash
# 建置
dotnet build

# 執行（桌面）
dotnet run --project src/Specurai.Desktop

# 執行測試
dotnet test
```

**發布單一執行檔**

```bash
# Windows x64
dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# macOS Apple Silicon
dotnet publish src/Specurai.Desktop -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
# macOS Intel
dotnet publish src/Specurai.Desktop -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
# Linux x64
dotnet publish src/Specurai.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

### 測試

採 TDD、xUnit + NSubstitute + FluentAssertions。測試命名 `[Method]_[Condition]_[Expected]`（繁體中文）。

---

## 連線設定共用位置

桌面應用程式、CLI、MCP Server 共用同一份連線設定檔：

| 平台 | 路徑 |
|------|------|
| Windows | `%APPDATA%\Specurai\connections.json` |
| macOS | `~/Library/Application Support/Specurai/connections.json` |
| Linux | `~/.config/Specurai/connections.json`（若有設 `$XDG_CONFIG_HOME` 則改用之） |

在任一工具中新增的連線，其他兩種介面可直接使用。

---

## 文件

| 文件 | 內容 |
|------|------|
| [docs/UserGuide.md](docs/UserGuide.md) | 桌面應用程式完整使用手冊 |
| [docs/INSTALL.md](docs/INSTALL.md) | 桌面程式與 MCP Server 從零開始安裝指引（AI 助手可讀） |
| [docs/McpServerREADME.md](docs/McpServerREADME.md) | MCP Server 技術細節（隨 NuGet 套件發佈） |
| [CLAUDE.md](CLAUDE.md) | 架構規範與開發指引 |

---

## 授權

[MIT License](LICENSE.txt)

## 貢獻

歡迎提交 Issue 和 Pull Request。
