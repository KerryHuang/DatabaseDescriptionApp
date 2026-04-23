# Specurai 安裝指引

本文件為 AI 助手提供完整的安裝指引，協助使用者安裝 Specurai 桌面應用程式和 MCP Server。

> **AI 助手請注意：** 請依照以下步驟，根據使用者的作業系統和需求，引導完成安裝。每個步驟都需確認使用者完成後再進行下一步。

---

## 一、確認使用者環境

請先詢問使用者以下資訊：

1. **作業系統**：Windows、macOS、Linux？
2. **macOS 架構**（僅 macOS）：Apple Silicon (M1/M2/M3/M4) 或 Intel？
   - 可請使用者執行 `uname -m` 確認：`arm64` = Apple Silicon，`x86_64` = Intel
3. **安裝需求**：只需要桌面應用程式、只需要 MCP Server、還是兩者都要？
4. **MCP 客戶端**：使用哪個 AI 工具？Claude Code、Claude Desktop、Cursor、Windsurf？

---

## 二、安裝桌面應用程式

### Windows

1. 前往 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載最新版本
2. 下載 `Specurai-win-Setup.exe`（安裝版）或 `Specurai-win-Portable.zip`（免安裝版）
3. **安裝版：** 執行 `Setup.exe`，依照精靈完成安裝
4. **免安裝版：** 解壓縮到任意目錄，執行 `Specurai.Desktop.exe`

### macOS

1. 前往 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載最新版本
2. 根據架構下載對應的 `.dmg` 檔案：
   - Apple Silicon：`Specurai-*-osx-arm64.dmg`
   - Intel：`Specurai-*-osx-x64.dmg`
3. 點兩下開啟 `.dmg`，將 Specurai 拖曳到 Applications 資料夾
4. **重要：** 首次開啟前，在終端機執行以下指令移除安全限制：

```bash
xattr -cr /Applications/Specurai.app
```

5. 之後即可從 Launchpad 或 Applications 開啟 Specurai

> **如果出現「已損毀，無法打開」的錯誤：** 請確認已執行步驟 4 的 `xattr -cr` 指令。

### Linux

1. 前往 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載最新版本
2. 下載 Linux 版本的安裝包
3. 依照 AppImage 標準方式執行

---

## 三、安裝 MCP Server

MCP Server 有兩種安裝方式，請根據使用者情況選擇：

| 方式 | 適合對象 | 前置需求 |
|------|----------|----------|
| **獨立執行檔**（推薦） | 一般使用者、不想裝 .NET | 無 |
| **dotnet tool** | 開發者、已有 .NET 環境 | .NET 8.0 SDK |

### 方式一：下載獨立執行檔（推薦）

免安裝 .NET，下載對應平台的檔案即可。

#### 步驟 1：下載

前往 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) 下載對應平台的 MCP Server：

| 平台 | 檔案名稱 |
|------|----------|
| Windows x64 | `Specurai.McpServer-win-x64.zip` |
| macOS Apple Silicon (M1/M2/M3/M4) | `Specurai.McpServer-osx-arm64.tar.gz` |
| macOS Intel | `Specurai.McpServer-osx-x64.tar.gz` |
| Linux x64 | `Specurai.McpServer-linux-x64.tar.gz` |

> macOS 不確定架構：在終端機執行 `uname -m`。`arm64` = Apple Silicon，`x86_64` = Intel。

#### 步驟 2：解壓縮

**Windows（PowerShell）：**

```powershell
# 解壓縮到 C:\Tools\SpecuraiMcp
Expand-Archive -Path $env:USERPROFILE\Downloads\Specurai.McpServer-win-x64.zip `
               -DestinationPath C:\Tools\SpecuraiMcp -Force
```

或直接右鍵解壓縮 `.zip` 到目標目錄。

**macOS（Apple Silicon 為例，Intel 請將 `osx-arm64` 換成 `osx-x64`）：**

```bash
mkdir -p ~/Tools/SpecuraiMcp
tar xzf ~/Downloads/Specurai.McpServer-osx-arm64.tar.gz -C ~/Tools/SpecuraiMcp

# 補執行權限
chmod +x ~/Tools/SpecuraiMcp/Specurai.McpServer

# 移除 Gatekeeper 隔離標記（瀏覽器下載的檔案會被打上 quarantine bit，首次執行會被擋）
xattr -dr com.apple.quarantine ~/Tools/SpecuraiMcp/Specurai.McpServer
```

> **若出現「無法驗證開發者」或「已損毀」：** 確認上方 `xattr -dr com.apple.quarantine` 已執行。
> **若出現 `zsh: permission denied`：** 確認上方 `chmod +x` 已執行。

**Linux：**

```bash
mkdir -p ~/Tools/SpecuraiMcp
tar xzf Specurai.McpServer-linux-x64.tar.gz -C ~/Tools/SpecuraiMcp
chmod +x ~/Tools/SpecuraiMcp/Specurai.McpServer
```

#### 步驟 3：記下執行檔完整路徑

下一步設定 MCP 客戶端時需要用到：

| 平台 | 範例路徑 |
|------|----------|
| Windows | `C:\Tools\SpecuraiMcp\Specurai.McpServer.exe` |
| macOS | `/Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer` |
| Linux | `/home/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer` |

### 方式二：dotnet tool

適合已有 .NET 環境的開發者，安裝後可用 `specurai-mcp` 指令直接執行。

#### 步驟 1：安裝 .NET 8.0 SDK

**Windows (PowerShell)：**

```powershell
winget install Microsoft.DotNet.SDK.8
```

或從 https://dotnet.microsoft.com/download/dotnet/8.0 下載安裝程式。

**macOS：**

```bash
brew install dotnet@8
```

若未安裝 Homebrew，先執行：

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

或從 https://dotnet.microsoft.com/download/dotnet/8.0 下載安裝。

**Linux (Ubuntu / Debian)：**

```bash
sudo apt update && sudo apt install -y dotnet-sdk-8.0
```

**Linux (Fedora)：**

```bash
sudo dnf install -y dotnet-sdk-8.0
```

驗證安裝：

```bash
dotnet --version
```

應顯示 `8.x.x` 版本號。

#### 步驟 2：安裝 MCP Server

```bash
dotnet tool install -g Specurai.McpServer
```

> **若顯示「找不到套件」：** 表示 NuGet 套件尚未發布，請改用方式一（獨立執行檔）。

#### 步驟 3：設定 PATH（macOS / Linux）

如果出現 PATH 警告，需將 dotnet tools 路徑加入系統 PATH：

**macOS (zsh)：**

```bash
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile
source ~/.zprofile
```

**Linux (bash)：**

```bash
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc
```

**Windows** 通常不需要額外設定 PATH。

#### 步驟 4：驗證安裝

```bash
specurai-mcp --help
```

若指令可執行，即表示安裝成功。

---

## 四、設定 MCP 客戶端

根據使用者的 AI 工具，選擇對應的設定方式。

### Claude Code

**獨立執行檔（方式一）：**

```bash
# Windows（請替換為實際路徑）
claude mcp add specurai -s user -- "C:\Tools\SpecuraiMcp\Specurai.McpServer.exe"

# macOS / Linux
claude mcp add specurai -s user -- /Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer
```

**dotnet tool（方式二）：**

```bash
claude mcp add specurai -s user -- specurai-mcp
```

驗證設定：

```bash
claude mcp list
```

應顯示 `specurai` 狀態為 `Connected`。

### Claude Desktop

開啟設定檔：

| 平台 | 設定檔路徑 |
|------|-----------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

**獨立執行檔（方式一）— Windows：**

```json
{
  "mcpServers": {
    "specurai": {
      "command": "C:\\Tools\\SpecuraiMcp\\Specurai.McpServer.exe"
    }
  }
}
```

> Windows JSON 內的反斜線需 escape 為 `\\`，或直接改用正斜線 `C:/Tools/SpecuraiMcp/Specurai.McpServer.exe`。

**獨立執行檔（方式一）— macOS / Linux：**

```json
{
  "mcpServers": {
    "specurai": {
      "command": "/Users/你的帳號/Tools/SpecuraiMcp/Specurai.McpServer"
    }
  }
}
```

**dotnet tool（方式二）— 所有平台：**

```json
{
  "mcpServers": {
    "specurai": {
      "command": "specurai-mcp"
    }
  }
}
```

設定完成後，重新啟動 Claude Desktop。

### Cursor

開啟設定檔：

| 平台 | 設定檔路徑 |
|------|-----------|
| Windows | `%APPDATA%\Cursor\mcp.json` |
| macOS | `~/Library/Application Support/Cursor/mcp.json` |
| Linux | `~/.config/Cursor/mcp.json` |

加入與 Claude Desktop 相同的 JSON 內容，重新啟動 Cursor。

### Windsurf

開啟設定檔：

| 平台 | 設定檔路徑 |
|------|-----------|
| Windows | `%APPDATA%\Windsurf\mcp_config.json` |
| macOS | `~/Library/Application Support/Windsurf/mcp_config.json` |
| Linux | `~/.config/Windsurf/mcp_config.json` |

加入與 Claude Desktop 相同的 JSON 內容，重新啟動 Windsurf。

---

## 五、設定資料庫連線

MCP Server 安裝完成後，需要設定 SQL Server 連線才能使用。

### 方式 A：透過桌面應用程式設定（推薦）

1. 開啟 Specurai 桌面應用程式
2. 點選「連線設定」
3. 輸入連線資訊：伺服器名稱、認證方式、資料庫名稱
4. 點選「測試連線」確認成功後儲存

桌面應用程式和 MCP Server 共用連線設定，設定一次即可。

### 方式 B：透過 AI 對話設定

在 AI 客戶端中直接說：

```
幫我設定 SQL Server 連線，伺服器是 myserver.database.windows.net，資料庫是 MyDB
```

AI 會透過 MCP Server 的 `list_connections` 和 `switch_connection` 工具協助設定。

### 連線設定儲存位置

| 平台 | 路徑 |
|------|------|
| Windows | `%APPDATA%\Specurai\connections.json` |
| macOS | `~/.config/Specurai/connections.json` |
| Linux | `~/.config/Specurai/connections.json` |

---

## 六、驗證完成

在 AI 客戶端中輸入以下指令測試：

```
列出所有連線設定
```

如果顯示連線清單，恭喜！安裝完成。

### 更多使用範例

- 「列出所有資料表」→ 查看資料庫物件
- 「查看 Orders 表的欄位」→ 查看欄位結構
- 「找出所有包含 Price 的欄位」→ 搜尋欄位
- 「分析資料庫效能瓶頸」→ 效能診斷
- 「執行 SELECT TOP 10 * FROM Users」→ 執行 SQL 查詢

---

## 常見問題

### macOS 出現「已損毀，無法打開」

執行 `xattr -cr /Applications/Specurai.app` 後重試。

### dotnet tool install 失敗

確認 .NET 8.0 SDK 已安裝：`dotnet --version` 應顯示 `8.x.x`。

### specurai-mcp 指令找不到

確認 PATH 已設定：
- macOS：`echo $PATH` 應包含 `$HOME/.dotnet/tools`
- 若未包含，執行 `echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile && source ~/.zprofile`

### MCP Server 連線失敗

1. 確認 SQL Server 可從本機連線
2. 檢查防火牆設定
3. 確認連線字串正確（伺服器名稱、認證方式）

### 更新版本

```bash
# dotnet tool
dotnet tool update -g Specurai.McpServer

# 桌面應用程式：從 GitHub Releases 下載最新版本
```
