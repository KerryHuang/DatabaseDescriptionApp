# 跨平台腳本規範

本專案支援 Windows、macOS、Linux，所有腳本和指令必須跨平台相容。

## Null 裝置（丟棄輸出）

| 平台 | Null 裝置 |
|------|-----------|
| Windows CMD | `NUL` |
| Unix/Linux/macOS | `/dev/null` |
| Git Bash on Windows | `/dev/null`（**不要用 `nul`**） |

**正確寫法**（跨平台）：
```bash
# 丟棄標準輸出
command > /dev/null

# 丟棄標準輸出和錯誤輸出
command > /dev/null 2>&1
```

**錯誤寫法**（會在 Git Bash 建立 `nul` 檔案）：
```bash
command > nul        # ❌ 錯誤
command > NUL        # ❌ 只適用於 Windows CMD
```

## 路徑分隔符

| 平台 | 分隔符 |
|------|--------|
| Windows | `\` 或 `/` |
| Unix/Linux/macOS | `/` |

**正確寫法**：統一使用 `/`
```bash
dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj  # ✅
```

## 換行符號

| 平台 | 換行符 |
|------|--------|
| Windows | CRLF (`\r\n`) |
| Unix/Linux/macOS | LF (`\n`) |

**規範**：所有腳本檔案使用 **LF**（見 `.editorconfig`）

## 環境變數

| 用途 | Windows CMD | Unix/Bash |
|------|-------------|-----------|
| 取得變數 | `%VAR%` | `$VAR` 或 `${VAR}` |
| 設定變數 | `set VAR=value` | `export VAR=value` |

**跨平台方案**：使用 `dotnet` 環境變數或設定檔，避免依賴 shell 變數。

## 常用跨平台指令

| 用途 | 跨平台指令 |
|------|-----------|
| 建置 | `dotnet build` |
| 測試 | `dotnet test` |
| 執行 | `dotnet run` |
| 列出檔案 | `ls`（Git Bash/Unix）或使用 Glob 工具 |
| 建立目錄 | `mkdir -p path`（Git Bash/Unix） |

## 避免使用的指令

以下指令僅限特定平台，應避免使用：

| 指令 | 平台限制 | 替代方案 |
|------|----------|----------|
| `dir` | Windows only | `ls` |
| `copy` | Windows only | `cp` |
| `move` | Windows only | `mv` |
| `del` | Windows only | `rm` |
| `type` | Windows only | `cat` |
| `cls` | Windows only | `clear` |
