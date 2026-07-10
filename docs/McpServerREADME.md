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

## 可用工具（57 個）

⚠️ 表示寫入或破壞性操作。

| 分類 | 工具 |
|------|------|
| 連線管理 | `list_connections`、`switch_connection`、`test_connection`、`add_connection` ⚠️、`update_connection` ⚠️、`delete_connection` ⚠️、`export_connections`、`import_connections` ⚠️ |
| 資料表查詢 | `list_tables`、`get_columns`、`get_indexes`、`get_relations`、`get_parameters`、`get_definition` |
| SQL 查詢 | `execute_readonly_sql`、`dry_run_sql`、`search_columns`、`search_columns_multi_database`、`get_create_table_sql` |
| 描述管理 | `update_table_description`、`update_column_description` |
| 效能診斷 | `get_wait_statistics`、`get_expensive_queries`、`get_expensive_procedures`、`get_missing_indexes`、`get_unused_indexes`、`get_error_log` |
| 健康監控 | `get_health_install_status`、`get_health_status`、`get_health_metrics`、`get_health_alerts`、`install_health_monitoring` ⚠️、`uninstall_health_monitoring` ⚠️、`export_health_monitoring_sql` |
| 統計資訊 | `get_table_statistics`、`get_exact_row_count`、`get_column_usage_statistics` |
| Agent Job | `list_agent_jobs`、`list_non_specurai_jobs`、`get_agent_job_history`、`set_agent_job_enabled` ⚠️、`start_agent_job` ⚠️、`delete_agent_job` ⚠️、`update_agent_job_schedule` ⚠️、`import_agent_job` ⚠️ |
| Schema 比對 | `compare_schemas`、`compare_multiple_schemas` |
| 使用狀態分析 | `scan_usage`、`compare_usage_multi_environment`、`generate_drop_table_sql`、`generate_drop_column_sql` |
| 維護計劃 | `check_maintenance_prerequisites`、`check_maintenance_steps`、`generate_maintenance_plan_sql`、`execute_maintenance_plan` ⚠️ |
| 匯出 | `export_all_to_excel`、`export_table_to_excel` |

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
