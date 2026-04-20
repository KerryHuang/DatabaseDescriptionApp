# Agent System Review Report

**Date:** 2026-04-20
**Project:** Specurai (DatabaseDescriptionApp)

## Summary

| 元件 | 類型 | Reviewer | Rating | Critical | Major | Minor |
|------|------|----------|--------|----------|-------|-------|
| CLAUDE.md | CLAUDE.md | claudemd-reviewer | Needs Fix | 1 | 2 | 2 |
| clean-architecture.md | rule | rule-reviewer | Needs Fix | 0 | 1 | 0 |
| chinese-conventions.md | rule | rule-reviewer | Fail | 1 | 0 | 0 |
| mvvm-patterns.md | rule | rule-reviewer | Needs Fix | 0 | 2 | 0 |
| repository-pattern.md | rule | rule-reviewer | Needs Fix | 0 | 1 | 0 |
| cross-platform-scripts.md | rule | rule-reviewer | Needs Fix | 0 | 1 | 0 |
| settings.json (hook) | hook | hook-reviewer | Needs Fix | 2 | 2 | 1 |
| debug-issue.md | skill | skill-reviewer | Needs Fix | 3 | 1 | 0 |
| update-docs.md | skill | skill-reviewer | Needs Fix | 2 | 1 | 0 |
| code-reviewer.md | subagent | subagent-reviewer | Fail | 3 | 1 | 1 |
| test-runner.md | subagent | subagent-reviewer | Fail | 2 | 2 | 1 |
| documentation-writer.md | subagent | subagent-reviewer | Needs Fix | 2 | 1 | 1 |

---

## Detailed Findings

### CLAUDE.md — Needs Fix

**Critical:**
- 行 44-47：`dotnet test tests/Specurai.Application.Tests` 等 3 個測試路徑均缺少 `tests/` 前綴，從 repo root 執行會失敗

**Major:**
- 行 9,11,13,15：4 條 `<law>` 重複指向已自動載入的 rules 檔案，雙重消耗 token（laws 指向 rule 檔即可，無需重述內容）
- 行 80：架構表格新增了 McpServer/Cli，但依賴圖（lines 69-72）未更新，兩者不一致

**Minor:**
- 行 85,92：列舉既有介面名稱和 Domain 物件（Claude 可從程式碼自行發現），非可執行指令，是噪音

---

### clean-architecture.md — Needs Fix

**Major:**
- 無 `paths:` frontmatter（全域載入）；內容是 C# 層級放置規則，應加 `paths: ["src/**/*.cs"]` 限制載入範圍

---

### chinese-conventions.md — Fail

**Critical:**
- 整個檔案內容完全重複 CLAUDE.md 的法則（「一律以繁體中文回答」、「UI 文字/註解/Commit 使用繁體中文」）；無 `paths:` frontmatter；應刪除或僅保留非重複的專案特定規則

---

### mvvm-patterns.md — Needs Fix

**Major:**
- 77 行（超出 50 行上限）；內容以多行 C# 程式碼範例為主（程序性內容），應提取至 skill references/
- `[ObservableProperty]`/`[RelayCommand]` 說明重複 CLAUDE.md 法則，應刪除

---

### repository-pattern.md — Needs Fix

**Major:**
- 61 行（超出 50 行上限）；Dapper/DI wiring 程式碼範例是程序性內容，應提取至 skill references/；只保留抽象指令（介面在 Domain、實作在 Infrastructure、使用 `Func<string?>`、參數化 SQL）

---

### cross-platform-scripts.md — Needs Fix

**Major:**
- `paths: ["**/*.ps1"]` 是 dead glob（repo 中無 .ps1 檔案），應移除或補充 PowerShell 腳本

---

### settings.json (hook) — Needs Fix

**Critical:**
- Pipeline 吃掉 exit code：`dotnet build ... | tail -5` 的 exit code 是 `tail` 的（幾乎永遠為 0），build 失敗時 Claude 不會被阻擋；需 `set -o pipefail` 或捕捉 `${PIPESTATUS[0]}`，並以 `exit 2` 明確阻擋
- 未使用 `$CLAUDE_PROJECT_DIR`：hook 依賴 cwd 找 .sln，若 cwd 不同會建置錯誤專案或失敗

**Major:**
- 無檔案過濾：Edit/Write 任何檔案（.md、.json、.axaml）都觸發完整 `dotnet build`，60 秒 timeout 阻擋 agent 迴圈；應限制只在 `*.cs` / `*.csproj` 變更時觸發
- `tail -5` 取最後 5 行是建置摘要（"Build succeeded/FAILED"），而非實際錯誤位置；應改為 `--nologo -clp:ErrorsOnly` 或 `grep -E "error"`

**Minor:**
- 未加 `command -v dotnet || exit 0` 防護，環境缺少 SDK 時輸出誤導訊息

---

### debug-issue.md — Needs Fix

**Critical:**
- 缺少 `name` 欄位
- description 不以「Use when...」開頭（先是「系統性除錯問題。」再接 Use when）
- 檔名 `debug-issue.md` 不符合動名詞命名規範（應為 `debugging-issues.md`）

**Major:**
- C# 測試範本程式碼和「除錯工具」段落應提取至 `references/` 實現漸進式揭露

---

### update-docs.md — Needs Fix

**Critical:**
- description 包含工作流摘要（「Updates README.md, UserGuide.md...」），應只含觸發條件
- 檔名 `update-docs.md` 不符合動名詞規範（應為 `updating-docs.md`）

**Major:**
- 與 `documentation-writer.md` agent 功能重疊（相同觸發場景）；應整合或明確分工

---

### code-reviewer.md — Fail

**Critical:**
- Frontmatter 缺少 `description`、`model`、`tools`、`context` 四個必填欄位
- 作為品質閘門 agent 應為 `model: claude-opus-4-7`，`tools: Read, Grep, Glob`，`context: fork`
- 無結構化 YAML 輸出格式

**Major:**
- 使用 `mcp__codex-cli__review` 但未在 `tools` 中宣告

**Minor:**
- 審查流程描述模糊

---

### test-runner.md — Fail

**Critical:**
- Frontmatter 缺少 `description`、`model`、`tools`
- 應為 `model: claude-sonnet-4-6`（implementer 層級）

**Major:**
- 違反單一職責：同時負責「執行測試」和「撰寫測試案例」兩個職責
- 無輸出格式宣告

**Minor:**
- 覆蓋率收集有命令但無下游報告步驟

---

### documentation-writer.md — Needs Fix

**Critical:**
- Frontmatter 缺少 `description`、`model`、`tools`
- 應明確排除 `Bash`（只需 Read/Grep/Glob/Edit/Write）

**Major:**
- 無輸出格式宣告

**Minor:**
- 單一職責清晰，內容合理

---

## 整體評估

- **Pass：** 0 個元件
- **Needs Fix：** 9 個元件
- **Fail：** 3 個元件（code-reviewer、test-runner、chinese-conventions）

最高優先修復：
1. Hook exit code 問題（build 失敗無法阻擋）
2. 三個 subagent frontmatter 補全（model/tools/description/context）
3. chinese-conventions.md 刪除（完全重複）
4. CLAUDE.md 測試路徑修正
