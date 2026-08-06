# DDL 執行能力設計（僅限非正式環境）

日期：2026-08-06
狀態：待審

## 背景與目標

現有工具鏈對 DDL 全面封鎖：`dry_run_sql`／`execute_sql` 只收單一 DML，
`migration_apply` 是「基準連線 vs 目標連線」的差異傳播工具，無法對設計庫本身下新 DDL。

目標：新增一條獨立的 DDL 執行管線，讓 AI 客戶端（MCP）、CLI 與 Desktop
都能對**非正式環境**連線執行物件級 DDL；正式環境（`DatabaseEnvironment.Production`）一律拒絕。

## 需求決策（已與使用者確認）

| 項目 | 決策 |
|------|------|
| DDL 範圍 | 物件級 DDL 白名單；庫級操作與 TRUNCATE 拒絕 |
| 確認閘門 | 比照 `execute_sql`：預設交易內預演後 ROLLBACK，`confirm:true` 才 COMMIT |
| 語句數量 | 允許多句批次（可含 `GO`），整批單一交易，任一句失敗全部回滾 |
| 入口 | MCP、CLI、Desktop 三入口 |

## 架構

沿用 DML 管線的既有骨架（Analyzer → Repository → Service → 三入口），但完全獨立成平行管線，
不與 DML 的「單句 + OUTPUT 前後對照」語意糾纏。

```
Domain:         DdlExecutionResult（結果實體）
                ISqlDdlExecuteRepository（介面）
Infrastructure: SqlDdlScriptAnalyzer（ScriptDom 離線解析 + 白名單驗證）
                SqlDdlExecuteRepository（交易執行，內部使用 Analyzer）
Application:    IDdlExecutionService / DdlExecutionService（連線解析 + Production 防線 + confirm 閘門）
入口:           McpServer SqlTools.ExecuteDdl（execute_ddl）
                Cli SqlCommand「sql ddl」子命令
                Desktop SqlQueryDocumentViewModel DDL 執行流程
```

## 元件設計

### Domain：`DdlExecutionResult`

新實體，不重用 `DryRunResult`（DDL 沒有影響筆數與前後對照，硬塞會出現一堆無意義欄位）：

- `IsValid`：語法與白名單驗證是否通過
- `RejectReason`：拒絕原因（含第幾句、什麼語句類型被擋）
- `SyntaxErrors`：重用既有 `DryRunSyntaxError`
- `Statements`：逐句摘要清單（`DdlStatementSummary { Index, Type, ObjectName }`），
  預演與執行成功時回報，讓呼叫端看得到整批要動哪些物件
- `ExecutionError`／`FailedBatchIndex`：執行失敗時的錯誤與失敗批次索引
  （執行以 `GO` 批次為單位，SQL 錯誤訊息本身含行號可再定位；整批已回滾）
- `Committed`：是否已 COMMIT（預演一律 false）
- `CommitUncertain`：COMMIT 結果不確定（沿用 DML 既有三態回報模式）

### Domain：`ISqlDdlExecuteRepository`

```csharp
Task<DdlExecutionResult> ExecuteAsync(
    string script, string connectionString, bool commit, CancellationToken ct = default);
```

單一介面即可：預演與實際執行只差最後 COMMIT／ROLLBACK，不像 DML 需要拆兩個
repository（DML 的 dry run 有 OUTPUT 注入邏輯，DDL 沒有）。
與 DML 相同，環境限制由 Application 層把關，呼叫端不得繞過 Service 直接使用。

### Infrastructure：`SqlDdlScriptAnalyzer`

以 `TSql160Parser` 解析整段 script（純離線，不碰資料庫）：

1. 語法錯誤 → 回報 `SyntaxErrors`。
2. 逐句比對**白名單**（以 ScriptDom 語句類別判斷），任一句不在名單 → 整批拒絕，
   `RejectReason` 指明第幾句、什麼類型：

   | 物件 | 允許語句 |
   |------|----------|
   | TABLE | CREATE / ALTER / DROP |
   | INDEX | CREATE / ALTER / DROP |
   | VIEW | CREATE / ALTER / DROP / CREATE OR ALTER |
   | PROCEDURE | CREATE / ALTER / DROP / CREATE OR ALTER |
   | FUNCTION | CREATE / ALTER / DROP / CREATE OR ALTER |
   | TRIGGER | CREATE / ALTER / DROP / CREATE OR ALTER |
   | SCHEMA | CREATE / ALTER / DROP |

   明確拒絕（不限於）：CREATE/ALTER/DROP DATABASE、TRUNCATE TABLE、
   GRANT/DENY/REVOKE、CREATE USER/LOGIN/ROLE、EXEC、所有 DML 與 SELECT。
   白名單採「不在名單即拒絕」策略，未列舉的語句類型一律擋下。
3. 通過驗證 → 產出逐句摘要（類型 + 目標物件名稱）與 `GO` 批次切分結果。

### Infrastructure：`SqlDdlExecuteRepository`

1. 先呼叫 Analyzer，驗證不過直接回傳拒絕結果。
2. 開連線、`BeginTransaction`，依 `GO` 批次順序逐批執行
   （`CREATE PROCEDURE` 等語句必須是 batch 首句，故以批次為執行單位；
   SQL Server 物件級 DDL 皆為 transactional，回滾可靠）。
3. 任一批失敗 → ROLLBACK，回報 `ExecutionError` 與 `FailedBatchIndex`。
4. 全部成功：`commit=false` → ROLLBACK（預演）；`commit=true` → COMMIT。
   COMMIT 擲例外時依既有模式判定 `CommitUncertain`。

### Application：`DdlExecutionService`

```csharp
Task<DdlExecutionResult> ExecuteAsync(
    string script, bool confirm, Guid? profileId = null, CancellationToken ct = default);
```

流程與 `DmlExecutionService` 對齊：

1. 解析目標連線：`profileId` 為 null 用目前連線；指定時從啟用連線中找，找不到不得靜默落回。
2. **`profile.Environment == DatabaseEnvironment.Production` → 拒絕**：
   「連線「{Name}」為正式環境，不允許執行 DDL。」
3. 取連線字串，委派 `ISqlDdlExecuteRepository.ExecuteAsync(script, cs, commit: confirm, ct)`。

### 入口一：MCP 工具 `execute_ddl`

`SqlTools.ExecuteDdl`，描述比照 `execute_sql` 的警示格式
（⚠️ 破壞性操作、僅限非正式環境、預設 confirm=false 僅預演）。
JSON 輸出：`Valid`、`RejectReason`、`SyntaxErrors`、`Statements`（逐句摘要）、
`ExecutionError`、`FailedBatchIndex`、`Committed`、`CommitUncertain`、`DatabaseChanged`、
預演時附 `Hint` 提示加 confirm:true。
`CommitUncertain` 時 `Committed`／`DatabaseChanged` 輸出 JSON null（沿用既有三態規則）。

### 入口二：CLI `specurai sql ddl`

- 參數：`[script]` 引數與 `--file <路徑>` 二擇一（DDL script 通常多行，檔案輸入是主要情境）；
  兩者皆給或皆缺 → 錯誤。
- `--confirm` 才 COMMIT，未指定僅預演；輸出逐句摘要表格；
  JSON 模式沿用 `Committed`／`DatabaseChanged` 三態 null 規則（`JsonIgnore(Never)` 覆寫）。
- 失敗 exit code 1。

### 入口三：Desktop

`SqlQueryDocumentViewModel` 新增「執行 DDL」命令，流程比照現有 `ExecuteDmlAsync`：

1. 先預演（`confirm: false`）並顯示逐句摘要。
2. 透過既有 `ConfirmExecuteCallback` 彈確認對話框（列出目標連線／資料庫與逐句摘要）。
3. 確認後 `confirm: true` 實際執行，顯示結果。
   環境閘門在 Service 層，UI 只控制可用性與確認流程。

## 錯誤處理

- 拒絕與錯誤訊息一律繁體中文，指明第幾句與原因。
- 整批單一交易：任一句失敗全部回滾，不留半套 schema。
- `CommitUncertain` 三態回報與 DML 一致：三入口都不得在結果不確定時誤報「已回滾／未變更」。
- Analyzer 對無法識別的語句類型一律拒絕（fail-closed）。

## 測試規劃

- **Infrastructure.Tests／`SqlDdlScriptAnalyzerTests`**（離線，不需資料庫）：
  白名單各類型接受；庫級操作／TRUNCATE／GRANT／EXEC／DML／SELECT 拒絕；
  混合批次（DDL + DML）拒絕；`GO` 批次切分；語法錯誤回報；空 script 拒絕。
- **Application.Tests／`DdlExecutionServiceTests`**（mock repository）：
  Production 連線拒絕；指定 profileId 找不到時拒絕（不落回目前連線）；
  無目前連線拒絕；confirm 旗標正確傳遞。
- **McpServer.Tests**：`execute_ddl` JSON 輸出形狀；confirm 閘門行為（比照 `ConfirmGateTests`）；
  `CommitUncertain` 時三態 null 輸出。
- Repository 的實際資料庫行為不寫自動化測試（現有測試套件皆為離線），以手動驗證涵蓋。

## 文件

- 更新 MCP 工具文件（比照 a4f2f7f 對 `execute_sql` 的文件模式），
  說明 `execute_ddl` 的範圍、白名單與確認閘門。

## 不做的事（YAGNI）

- 不開放庫級操作、TRUNCATE、權限語句——需要時再議，屆時另開白名單項目。
- 不做 DDL 的「前後 schema 對照」預覽（那是 `compare_schemas` 的職責）。
- 不動現有 DML 管線與 `migration_*` 工具。
