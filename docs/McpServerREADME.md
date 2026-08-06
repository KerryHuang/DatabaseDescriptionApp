# Specurai MCP Server

透過 [Model Context Protocol](https://modelcontextprotocol.io/) 讓 AI 助手直接存取 SQL Server 資料庫結構、執行唯讀 SQL、診斷效能、監控健康狀態。

支援 Claude Code、Claude Desktop、Cursor、Windsurf 等所有 MCP 客戶端。

## 安裝

### 方式一：獨立執行檔（免安裝 .NET）

從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) 下載對應平台：

| 平台 | 檔案 |
|------|------|
| Windows x64 | `Specurai.McpServer-win-x64.zip` |
| macOS Apple Silicon | `Specurai.McpServer-osx-arm64.tar.gz` |
| macOS Intel | `Specurai.McpServer-osx-x64.tar.gz` |
| Linux x64 | `Specurai.McpServer-linux-x64.tar.gz` |

macOS 解壓後需處理 Gatekeeper：

```bash
chmod +x Specurai.McpServer
xattr -dr com.apple.quarantine Specurai.McpServer
```

### 方式二：dotnet tool

需 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

- Windows：`winget install Microsoft.DotNet.SDK.8`
- macOS：`brew install dotnet@8`
- Linux：`sudo apt install dotnet-sdk-8.0`（Ubuntu/Debian）或 `sudo dnf install dotnet-sdk-8.0`（Fedora）

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

安裝後即可用 `specurai-mcp` 指令。

## 設定 AI 客戶端

### Claude Code

```bash
# dotnet tool
claude mcp add specurai -s user -- specurai-mcp

# 獨立執行檔
claude mcp add specurai -s user -- /絕對路徑/Specurai.McpServer
```

### Claude Desktop / Cursor / Windsurf

設定檔位置：

| 客戶端 | Windows | macOS | Linux |
|--------|---------|-------|-------|
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` | `~/.config/Claude/claude_desktop_config.json` |
| Cursor | `%APPDATA%\Cursor\mcp.json` | `~/Library/Application Support/Cursor/mcp.json` | `~/.config/Cursor/mcp.json` |
| Windsurf | `%APPDATA%\Windsurf\mcp_config.json` | `~/Library/Application Support/Windsurf/mcp_config.json` | `~/.config/Windsurf/mcp_config.json` |

加入：

```json
{
  "mcpServers": {
    "specurai": {
      "command": "specurai-mcp"
    }
  }
}
```

> 若使用獨立執行檔，`command` 改為絕對路徑。Windows JSON 內反斜線需 escape 為 `\\`，或改用 `/`。

## 連線設定

首次使用透過 AI 對話設定，或在桌面應用程式中設定。所有介面（桌面、CLI、MCP）共用同一份：

| 平台 | 路徑 |
|------|------|
| Windows | `%APPDATA%\Specurai\connections.json` |
| macOS | `~/Library/Application Support/Specurai/connections.json` |
| Linux | `~/.config/Specurai/connections.json` |

## 可用工具（58 個）

⚠️ 表示寫入或破壞性操作。

| 分類 | 工具 |
|------|------|
| 連線管理 | `list_connections`（含 `IsEnabled`）、`switch_connection`（不選用已停用的連線）、`test_connection`、`add_connection` ⚠️、`update_connection` ⚠️、`delete_connection` ⚠️、`export_connections`、`import_connections` ⚠️ |
| 資料表查詢 | `list_tables`、`get_columns`、`get_indexes`、`get_relations`、`get_parameters`、`get_definition` |
| SQL 查詢 | `execute_readonly_sql`（ScriptDom AST 白名單驗證，僅放行 SELECT，擋 CTE-DML、多句批次、SELECT INTO、EXEC）、`dry_run_sql`、`execute_sql` ⚠️（僅限非正式環境；預設 `confirm=false` 僅預演，`confirm:true` 才 COMMIT 寫入；Production 一律拒絕）、`search_columns`、`search_columns_multi_database`、`get_create_table_sql` |
| 描述管理 | `update_table_description`、`update_column_description` |
| 效能診斷 | `get_wait_statistics`、`get_expensive_queries`、`get_expensive_procedures`、`get_missing_indexes`、`get_unused_indexes`、`get_error_log` |
| 健康監控 | `get_health_install_status`、`get_health_status`、`get_health_metrics`、`get_health_alerts`、`install_health_monitoring` ⚠️、`uninstall_health_monitoring` ⚠️、`export_health_monitoring_sql` |
| 統計資訊 | `get_table_statistics`、`get_exact_row_count`、`get_column_usage_statistics` |
| Agent Job | `list_agent_jobs`、`list_non_specurai_jobs`、`get_agent_job_history`、`set_agent_job_enabled` ⚠️、`start_agent_job` ⚠️、`delete_agent_job` ⚠️、`update_agent_job_schedule` ⚠️、`import_agent_job` ⚠️ |
| Schema 比對 | `compare_schemas`、`compare_multiple_schemas` |
| 使用狀態分析 | `scan_usage`、`compare_usage_multi_environment`、`generate_drop_table_sql`、`generate_drop_column_sql` |
| 維護計劃 | `check_maintenance_prerequisites`、`check_maintenance_steps`、`generate_maintenance_plan_sql`、`execute_maintenance_plan` ⚠️ |
| 匯出 | `export_all_to_excel`、`export_table_to_excel` |

停用的連線不會被 `switch_connection` 與比對／移轉類工具選用，指定停用連線會回「連線「X」已停用，請先在連線設定中啟用。」；啟用／停用僅能在桌面應用程式的連線設定畫面操作。

## SQL 執行工具詳細說明

### execute_sql

執行單一 DML 語句（INSERT、UPDATE、DELETE）。

**安全機制：**

- 僅限非正式環境（Production 連線一律拒絕）
- 預設 `confirm=false`：在交易內執行後自動回滾（僅預演，同步回報影響筆數與前後資料對照）
- `confirm=true`：執行後 COMMIT 至資料庫
- 單一交易，執行失敗即回滾

**參數：**

- `sql`（string）：單一 DML 語句
- `confirm`（boolean，預設 `false`）：是否 COMMIT 至資料庫

**輸出欄位**（語法驗證失敗時）：

- `Valid`（boolean）：false
- `RejectReason`（string）：拒絕原因
- `SyntaxErrors`（object array）：逐行語法錯誤明細（每項含 Line、Column、Message）
- `Committed`（boolean）：false
- `DatabaseChanged`（boolean）：false

**輸出欄位**（語法正確、預演或執行成功時）：

- `Valid`（boolean）：true
- `StatementType`（string）：DML 類型（INSERT、UPDATE、DELETE）
- `AffectedRowCount`（int）：影響的行數
- `ExecutionError`（string，可為 null）：執行過程中的錯誤訊息（無誤為 null）
- `PreviewColumns`（string array，可為 null）：預覽資料表的欄位名稱
- `PreviewRows`（object array，可為 null）：預覽資料表的前 100 筆行資料
- `PreviewTruncated`（boolean）：預覽資料是否超過 100 筆
- `Warnings`（string array）：警告訊息（如模糊的 WHERE 條件）
- `Committed`（boolean，可為 null）：是否已 COMMIT。三態規則：結果確定時 = 實際是否 COMMIT；COMMIT 過程失敗時 = null
- `CommitUncertain`（boolean）：COMMIT 結果是否不確定（如網路中斷），為 true 時 `Committed` 欄位無法判斷
- `DatabaseChanged`（boolean，可為 null）：資料庫是否已修改。三態規則：結果確定時 = 實際狀態；COMMIT 過程失敗時 = null
- `Hint`（string，可為 null）：提示訊息（預演時提示需加 confirm:true 實際執行；成功 COMMIT 時為 null）

### execute_ddl

執行白名單物件級 DDL 批次（CREATE、ALTER、DROP）。

**支援的物件類型：**

TABLE、INDEX、VIEW、PROCEDURE、FUNCTION、TRIGGER、SCHEMA，可含多句語句與 GO 分隔符。

**安全機制：**

- Production 連線一律拒絕
- 以下操作拒絕（fail-closed）：庫級操作（ALTER DATABASE 等）、TRUNCATE、權限語句（GRANT、REVOKE）、動態執行（EXEC、sp_executesql）、DML（INSERT、UPDATE、DELETE）
- 預設 `confirm=false`：在交易內執行後自動回滾（僅預演）
- `confirm=true`：執行後 COMMIT 至資料庫
- 整批單一交易，任一批失敗即整批回滾，不保留已執行批次的變更

**參數：**

- `script`（string）：DDL script 內容
- `confirm`（boolean，預設 `false`）：是否 COMMIT 至資料庫

**輸出欄位：**

- `Valid`（boolean）：語法是否通過驗證
- `Statements`（object array）：逐句摘要，每項含 Index（序號，1 起算）、Type（語句類型）、ObjectName（目標物件名稱，可為 null）、BatchIndex（所屬 GO 批次，1 起算）
- `ExecutionError`（string，可為 null）：執行過程中的錯誤訊息
- `FailedBatchIndex`（int，可為 null）：首個失敗批次的編號（1 起算）；無失敗時為 null
- `Committed`（boolean，可為 null）：是否已 COMMIT。三態規則：結果確定時 = 實際是否 COMMIT；結果不確定時 = null
- `DatabaseChanged`（boolean，可為 null）：資料庫是否已修改。三態規則：結果確定時 = 實際狀態；結果不確定時 = null
- `CommitUncertain`（boolean）：結果是否不確定（例如網路斷線或執行過程異常中止），為 true 時 `Committed` 與 `DatabaseChanged` 欄位無法判斷實際值

## 疑難排解

| 症狀 | 解決方式 |
|------|----------|
| macOS「無法驗證開發者」或「已損毀」 | `xattr -dr com.apple.quarantine <執行檔路徑>` |
| macOS `zsh: permission denied` | `chmod +x <執行檔路徑>` |
| `specurai-mcp: command not found` | 將 `$HOME/.dotnet/tools` 加入 PATH，或改用獨立執行檔絕對路徑 |
| AI 客戶端連不到 Server | 確認 JSON 語法、command 為絕對路徑、Windows 路徑用 `\\` 或 `/`、重啟客戶端 |

## 更多資訊

- [GitHub](https://github.com/KerryHuang/DatabaseDescriptionApp)
- [完整安裝指引](https://github.com/KerryHuang/DatabaseDescriptionApp/blob/master/docs/INSTALL.md)
- [桌面應用程式下載](https://github.com/KerryHuang/DatabaseDescriptionApp/releases)
