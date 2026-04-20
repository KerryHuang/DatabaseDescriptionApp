---
paths:
  - "**/*.sh"
  - ".github/workflows/**"
  - "scripts/**"
---

# 跨平台腳本規範

本專案支援 Windows、macOS、Linux，所有腳本和指令必須跨平台相容。

## Null 裝置（丟棄輸出）

統一使用 `/dev/null`（Git Bash/Unix 語法）：

```bash
command > /dev/null        # ✅ 正確
command > /dev/null 2>&1   # ✅ 正確
command > nul              # ❌ 錯誤（在 Git Bash 會建立 nul 檔案）
```

## 路徑與指令

- 路徑分隔符統一使用 `/`
- 換行符使用 **LF**（見 `.editorconfig`）

## 避免使用的 Windows 專用指令

| 避免 | 替代 |
|------|------|
| `dir` | `ls` |
| `copy` / `move` / `del` | `cp` / `mv` / `rm` |
| `type` | `cat` |
| `set VAR=` | `export VAR=` |
| `taskkill` | 使用跨平台 SDK 方法 |

## 常用跨平台指令

```bash
dotnet build / test / run / publish   # 建置與執行
mkdir -p path                          # 建立目錄
```
