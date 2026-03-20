# Specurai MCP Server

透過 [Model Context Protocol](https://modelcontextprotocol.io/) 讓 AI 助手直接存取 SQL Server 資料庫結構資訊。

支援 Claude Code、Claude Desktop、Cursor、Windsurf 等所有 MCP 客戶端。

## 前置需求

安裝 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

- **Windows：** `winget install Microsoft.DotNet.SDK.8` 或從官網下載
- **macOS：** `brew install dotnet@8` 或從官網下載
- **Linux (Ubuntu/Debian)：** `sudo apt install dotnet-sdk-8.0`

## 安裝

```bash
dotnet tool install -g Specurai.McpServer
```

> **macOS / Linux：** 若出現 PATH 警告，需將 dotnet tools 加入 PATH：
>
> ```bash
> # macOS (zsh)
> echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile && source ~/.zprofile
>
> # Linux (bash)
> echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc && source ~/.bashrc
> ```

## 設定

### Claude Code

```bash
claude mcp add specurai -s user -- specurai-mcp
```

### Claude Desktop / Cursor / Windsurf

在設定檔中加入：

```json
{
  "mcpServers": {
    "specurai": {
      "command": "specurai-mcp"
    }
  }
}
```

設定檔位置：

| 客戶端 | Windows | macOS | Linux |
|--------|---------|-------|-------|
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` | `~/.config/Claude/claude_desktop_config.json` |
| Cursor | `%APPDATA%\Cursor\mcp.json` | `~/Library/Application Support/Cursor/mcp.json` | `~/.config/Cursor/mcp.json` |
| Windsurf | `%APPDATA%\Windsurf\mcp_config.json` | `~/Library/Application Support/Windsurf/mcp_config.json` | `~/.config/Windsurf/mcp_config.json` |

## 連線設定

首次使用時，透過 AI 對話設定資料庫連線，或在桌面應用程式中設定。

連線設定儲存於：

| 平台 | 路徑 |
|------|------|
| Windows | `%APPDATA%\Specurai\connections.json` |
| macOS | `~/.config/Specurai/connections.json` |
| Linux | `~/.config/Specurai/connections.json` |

## 可用工具（27 個）

| 分類 | 工具 |
|------|------|
| 連線管理 | `list_connections`、`switch_connection`、`test_connection` |
| 資料表查詢 | `list_tables`、`get_columns`、`get_indexes`、`get_relations`、`get_parameters`、`get_definition` |
| SQL 查詢 | `execute_readonly_sql`、`search_columns`、`get_create_table_sql` |
| 描述管理 | `update_table_description`、`update_column_description` |
| 效能診斷 | `get_wait_statistics`、`get_expensive_queries`、`get_expensive_procedures`、`get_missing_indexes`、`get_unused_indexes`、`get_error_log` |
| 健康監控 | `get_health_install_status`、`get_health_status`、`get_health_metrics`、`get_health_alerts` |
| 統計資訊 | `get_table_statistics`、`get_exact_row_count`、`get_column_usage_statistics` |

## 更多資訊

- [GitHub](https://github.com/KerryHuang/DatabaseDescriptionApp)
- [桌面應用程式下載](https://github.com/KerryHuang/DatabaseDescriptionApp/releases)
