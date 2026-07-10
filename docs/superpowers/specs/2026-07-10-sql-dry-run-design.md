# SQL Dry Run 設計文件

日期：2026-07-10
狀態：已由使用者確認

## 背景與目標

目前 Desktop APP、MCP（`execute_readonly_sql`）與 CLI（`sql query`）均卡控只能執行唯讀查詢。
本功能新增「Dry Run」能力：**驗證 DML 語法並預演實際執行結果，但永遠回滾、絕不修改資料**。

### 已確認的需求範圍

| 決策點 | 結論 |
|--------|------|
| 是否開放實際執行 | **否**，只有 Dry Run，系統維持唯讀；實際執行需自行使用 SSMS 等工具 |
| 支援語法類型 | **僅 DML**（INSERT/UPDATE/DELETE）；DDL、TRUNCATE、EXEC 維持完全阻擋 |
| 預演深度 | 語法驗證 + 影響筆數 + **前後資料對照**（OUTPUT 子句擷取） |
| 介面範圍 | Desktop APP、MCP、CLI **三個都要** |
| 陳述式數量 | **單一 DML 陳述式**，多語句一律拒絕 |

### 技術選型（方案 A）

使用微軟官方 T-SQL 解析器 **`Microsoft.SqlServer.TransactSql.ScriptDom`**（NuGet，Infrastructure 層引用，跨平台）：

- **語法驗證**：離線解析，不連資料庫即可回報語法錯誤與行/列位置
- **語句分類**：以 AST 判斷「恰好一句 INSERT/UPDATE/DELETE」，可正確識別 CTE 包裝的 DML（如 `;WITH cte AS (...) UPDATE`）、註解開頭等現有前綴檢查無法處理的情況
- **OUTPUT 注入**：在 AST 上注入 OUTPUT 子句後重新產生 SQL，避免字串拼接的脆弱性

否決的替代方案：
- 字串/正規式改寫——子查詢、字串常值、註解都會造成誤判，語法驗證只能靠丟給 SQL Server 試錯
- 只回報影響筆數（不改寫）——看不到前後資料對照，不符合需求深度

## 架構分層（Clean Architecture）

| 層級 | 新增內容 |
|------|----------|
| **Domain** | `Entities/DryRunResult.cs`（含 `SyntaxError` 明細）、`Interfaces/ISqlDryRunRepository.cs` |
| **Infrastructure** | `SqlDryRunAnalyzer`（純解析，無 DB 相依）＋ `SqlDryRunRepository`（交易執行）；引用 ScriptDom 套件 |
| **Desktop / McpServer / Cli** | 各自的呈現層（見下） |

- **不新增 Application Service**：比照 `ISqlQueryRepository` 現有模式，三個介面直接使用 Repository。
- DI 註冊沿用 `Func<string?>` 連線字串工廠模式，統一在 `Program.cs` 的 `ConfigureServices()`。
- **拆成兩個單元的理由**：`SqlDryRunAnalyzer` 完全不碰資料庫（解析 → 驗證 → 分類 → 改寫），可寫大量單元測試；`SqlDryRunRepository` 只負責薄薄的交易執行層。

### DryRunResult 主要欄位

| 欄位 | 說明 |
|------|------|
| `IsValid` | 語法與分類驗證是否通過 |
| `SyntaxErrors` | 語法錯誤明細（行、列、訊息） |
| `StatementType` | Insert / Update / Delete |
| `AffectedRowCount` | 影響筆數 |
| `PreviewTable` | 前後資料對照（DataTable） |
| `PreviewTruncated` | 預覽是否被截斷（超過 100 筆） |
| `Warnings` | 警告清單（IDENTITY 消耗、trigger fallback 等） |
| `ExecutionError` | 語法正確但實際執行會失敗時的錯誤訊息（如違反 FK） |

實體遵循專案慣例：`required` + `init` 屬性、集合預設 `[]`。

## 執行流程

```
使用者 SQL
  → ① ScriptDom 解析（離線）
      語法錯誤 → 直接回報行/列/訊息，不連資料庫
  → ② AST 驗證：恰好一句，且是 INSERT/UPDATE/DELETE
      多語句、DDL、EXEC、TRUNCATE、SELECT 等一律拒絕並說明原因
  → ③ OUTPUT 注入改寫
      INSERT → OUTPUT inserted.*
      DELETE → OUTPUT deleted.*
      UPDATE → 先查目標表欄位清單（sys.columns），
               產生 OUTPUT deleted.[c] AS [舊_c], inserted.[c] AS [新_c]
      使用者已自帶 OUTPUT 子句 → 直接沿用，不重複注入
  → ④ BEGIN TRAN → ExecuteReader（CommandTimeout = 30 秒）
      讀取 OUTPUT 結果集：預覽最多 100 筆，總筆數照實統計
  → ⑤ 一律 ROLLBACK（finally 保證，含例外路徑）
```

### 安全與相容細節

- **現有卡控不動**：`execute_readonly_sql`、`sql query`、桌面查詢的唯讀限制維持原樣；dry run 是獨立入口，且步驟②的 AST 驗證比現有前綴檢查嚴格，不會因此開洞。
- **Trigger fallback**：目標表有觸發程序時 `OUTPUT`（無 INTO）會報錯（error 334），此時自動改以「原句執行、只回報影響筆數」重試，並附警告「目標資料表有觸發程序，無法提供前後對照」。
- **固定警告**：
  - INSERT 時提醒「若目標表有 IDENTITY，序號在回滾後仍會被消耗」
  - 所有結果均明示「已回滾，資料庫未變更」
- **執行期錯誤是功能不是失敗**：違反約束、FK 等以 `ExecutionError` 回報「此語句實際執行將會失敗：{原因}」——這正是預演的價值。交易由 finally 保證回滾。
- **鎖定影響**：交易中執行會取得真實鎖定，30 秒逾時上限控制影響範圍。

## 三個介面的呈現

### MCP

`SqlTools` 新增 `dry_run_sql` 工具：

- 參數：`sql`（單一 DML 陳述式）
- 回傳 JSON：`valid`、`statementType`、`affectedRowCount`、`previewColumns`、`previewRows`、`previewTruncated`、`warnings`、`rolledBack: true`
- 工具描述明示「永遠回滾、不會修改資料」

### CLI

`sql dry-run "<sql>"` 子命令：

- 一般模式：Spectre.Console 表格顯示前後對照 + 影響筆數 + 警告
- `--json` 模式：走 `CliOutput.Success` 標準格式
- 語法錯誤或執行期錯誤時 exit code 1

### Desktop

SQL 查詢分頁「執行」旁新增「Dry Run」按鈕：

- 結果沿用現有 DataGrid 顯示 OUTPUT 對照表
- 狀態列顯示「影響 N 筆（UPDATE）｜已回滾，資料庫未變更」
- 警告以醒目文字呈現
- ViewModel 依現有模式：`[RelayCommand] DryRunAsync`、設計時建構子 + DI 建構子

## 測試策略（TDD）

| 測試專案 | 範圍 |
|----------|------|
| Domain.Tests | `DryRunResult` 實體行為 |
| Infrastructure.Tests（重點） | `SqlDryRunAnalyzer` 純單元測試：合法 DML、CTE 包裝的 UPDATE、多語句拒絕、DDL/EXEC/TRUNCATE 拒絕、註解開頭、字串常值內含關鍵字、OUTPUT 注入後的 SQL 正確性、已含 OUTPUT 的沿用 |
| McpServer.Tests | `dry_run_sql` 工具，mock `ISqlDryRunRepository` |
| Desktop.Tests | ViewModel 初始狀態、命令行為（mock repository） |

- 測試命名依專案慣例：`[Method]_[Condition]_[Expected]`，繁體中文
- **執行層**（真實交易/回滾/trigger fallback）無法在單元測試連 DB，完成後以實際資料庫手動驗證（透過 MCP 工具實測）
