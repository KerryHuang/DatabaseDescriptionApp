# 非正式環境 DML 執行通道設計

日期：2026-08-04
狀態：已與使用者確認

## 背景

目前三個入口（MCP、CLI、Desktop）對資料的 INSERT/UPDATE/DELETE 只有 dry run 預演通道，
沒有真正 commit 的路徑。需求：依連線設定的環境（`ConnectionProfile.Environment`），
除 Production 以外都可以實際執行 DML；dry run 保留不變。

同時修復既有唯讀漏洞：`execute_readonly_sql` 只用字串開頭關鍵字檢查，
CTE 開頭的 DML（`WITH x AS (...) DELETE ...`）與多句批次（`SELECT 1; DELETE ...`）
都能繞過並實際寫入；Desktop 查詢視窗與 CLI `sql query` 更是完全沒有擋。

## 需求決策（已確認）

| 決策點 | 結論 |
|---|---|
| 範圍 | MCP + CLI + Desktop 三入口 |
| 語句範圍 | 僅 DML（INSERT/UPDATE/DELETE），DDL 仍走 migration 與 drop 專用工具 |
| 確認閘門 | 要。預設先預演（dry run），確認後才 commit |
| 語句數量 | 單一語句，與 dry_run_sql 對齊 |
| 唯讀強化 | 一併修，改 ScriptDom AST 驗證，所有環境的查詢路徑都 SELECT-only |
| EXEC / 預存程序 | 查詢路徑一律擋（無法靜態判斷 SP 內容是否唯讀），Desktop 也不例外 |

## 架構（方案 A：中央執行服務）

環境閘門只存在 Application 層的 `IDmlExecutionService` 一處，三入口都呼叫它，
行為不會漂移。Infrastructure 沿用 `SqlDryRunRepository` 既有的執行管線
（SqlDryRunAnalyzer 單句驗證 → OUTPUT 注入 → 交易執行 → 讀取前後對照），
只在最後一步分支 ROLLBACK 或 COMMIT，驗證邏輯零重複。

```
MCP execute_sql ──┐
CLI sql execute ──┼─→ IDmlExecutionService ──→ ISqlDmlExecuteRepository
Desktop 執行DML ──┘      (Application)              (Domain 介面)
                     1. 環境閘門：Production 拒絕        │
                     2. confirm 分流：                   ↓
                        false → dry run          SqlDryRunRepository
                        true  → commit           (Infrastructure，同一類別
                                                  實作 dry run 與 commit 兩介面)
```

## 各層改動

### Domain

- 新增 `Interfaces/ISqlDmlExecuteRepository.cs`：
  ```csharp
  Task<DryRunResult> ExecuteAsync(string sql, CancellationToken ct = default);
  Task<DryRunResult> ExecuteAsync(string sql, string connectionString, CancellationToken ct = default);
  ```
- `Entities/DryRunResult.cs` 加一個欄位：`public bool Committed { get; init; }`
  （預設 false，dry run 路徑完全不受影響）。

### Application

- 新增 `Services/IDmlExecutionService.cs` 與 `DmlExecutionService`：
  ```csharp
  Task<DryRunResult> ExecuteAsync(string sql, bool confirm, Guid? profileId = null, CancellationToken ct = default);
  ```
  - 以 `profileId`（未指定則目前連線）向 `IConnectionManager` 解析
    `ConnectionProfile.Environment` 與連線字串。
  - `Environment == Production` → 回拒絕結果（`IsValid=false`、
    `RejectReason` 註明正式環境不允許 DML），不連資料庫。
  - `confirm == false` → 呼叫 `ISqlDryRunRepository.DryRunAsync`（預演）。
  - `confirm == true` → 呼叫 `ISqlDmlExecuteRepository.ExecuteAsync`（commit）。
  - 找不到 profile 或連線已停用 → 拒絕，不得靜默落回其他連線
    （比照 SqlQueryDocumentViewModel 對停用連線的處理原則）。

### Infrastructure

- `Repositories/SqlDryRunRepository.cs`：
  - 同時實作 `ISqlDryRunRepository` 與 `ISqlDmlExecuteRepository`。
  - 把 `ExecutePreviewAsync` / `ExecuteCountOnlyAsync` 的交易收尾抽成
    commit-or-rollback 參數；dry run 呼叫端固定 rollback，行為與現在完全相同。
  - commit 路徑同樣支援 OUTPUT 前後對照（執行且 commit 後回傳異動前後資料，
    Trigger fallback 邏輯照舊，只回影響筆數）。
  - commit 與 rollback 一律使用 `CancellationToken.None` 送出交易收尾，
    確保不因取消而留下懸掛交易。
- 新增 `Services/SqlReadOnlyValidator.cs`（與 SqlDryRunAnalyzer 同層）：
  - ScriptDom 解析整個批次，逐句檢查白名單：
    - 允許：`SELECT`（含 CTE，但 `SELECT ... INTO` 視為寫入、拒絕）、
      `DECLARE`、變數 `SET`、`SET` 工作階段選項（如 ISOLATION LEVEL）。
    - 其餘一律拒絕（含 INSERT/UPDATE/DELETE/MERGE/DDL/EXEC/EXECUTE）。
  - 解析失敗（語法錯誤）→ 拒絕並回報錯誤位置。
- `Repositories/SqlQueryRepository.cs`：`ExecuteQueryAsync` /
  `ExecuteQueryWithSchemaAsync` 執行前先過 validator，未通過丟出
  `InvalidOperationException`，訊息註明「查詢僅支援 SELECT，DML 請走執行通道」。

### McpServer

- `Tools/SqlTools.cs`：
  - 新增 `ExecuteSql` 工具（`execute_sql`）：
    - Description 標明 ⚠️ 破壞性、僅限非正式環境、預設預演、需 `confirm:true`
      才實際執行（比照 `migration_apply` 慣例）。
    - 參數：`sql`、`confirm = false`。
    - 輸出 JSON 含 `Committed` 與 `DatabaseChanged` 欄位；未 confirm 時回
      預演結果並提示「加 confirm:true 執行」。
  - `ExecuteReadonlySql`：移除 StartsWith 關鍵字檢查，改為 repository 層
    validator 把關（工具端只轉譯拒絕訊息）。
- `dry_run_sql` 不動。

### Cli

- `Commands/SqlCommand.cs` 新增 `sql execute <sql> [--confirm]`：
  - 無 `--confirm`：輸出預演結果（沿用 dry-run 的轉置表格呈現）+ 提示加
    `--confirm` 執行。
  - 有 `--confirm`：實際執行，輸出影響筆數與前後對照，註明「已 commit」。
  - Production 或驗證失敗：錯誤訊息 + ExitCode 1。
  - `--json` 模式輸出與 MCP 對齊（含 `Committed`）。
- `sql query`、`sql dry-run` 介面不動（query 的 DML 會被新 validator 擋下）。

### Desktop

- `SqlQueryDocumentViewModel`：
  - 注入 `IDmlExecutionService`。
  - 新增 `ExecuteDmlCommand`：
    - `CanExecute`：選定連線的 `Environment != Production`。
    - 流程：先呼叫 `ExecuteAsync(confirm: false)` 取得預演 → 以
      `ConfirmExecuteCallback` 模式跳確認對話框（顯示影響筆數、語句類型）→
      確認後 `ExecuteAsync(confirm: true)` → 結果呈現沿用 dry run 的
      前後對照 DataGrid，狀態列註明「已寫入資料庫」。
  - 連線下拉切換時重新評估按鈕可用性。
- View：查詢視窗加「執行 DML」按鈕（Production 連線時停用並附 tooltip 說明）。
- `Program.cs`：DI 註冊 `IDmlExecutionService`、`ISqlDmlExecuteRepository`
  （與 `ISqlDryRunRepository` 共用同一 `SqlDryRunRepository` 實例）。

## 既有行為變更（唯一一處）

查詢路徑（MCP `execute_readonly_sql`、CLI `sql query`、Desktop 查詢執行）
加上 SELECT-only AST 驗證後：

- CTE 繞過、多句批次繞過：被堵住（原本會實際寫入，是漏洞）。
- Desktop 查詢視窗、CLI `sql query` 偷跑 DML：被擋下，提示改走 DML 通道。
- `EXEC` / 預存程序：三入口一律擋（原本 Desktop/CLI 不擋）。
- 純 SELECT 查詢：完全不受影響。

## 不動範圍

- `dry_run_sql`（MCP）、`sql dry-run`（CLI）、Desktop「Dry Run」按鈕：
  介面、行為、輸出格式全部不變。
- `SqlDryRunAnalyzer`：只被共用，不改內容。
- `migration_*`、備份還原、健康監控、匯出、結果編輯／產生 UPDATE SQL 等
  所有其他功能：零接觸。

## 錯誤處理

- Production 拒絕、profile 不存在、連線停用：Application 層以結果物件回報
  （`IsValid=false` + `RejectReason`），不丟例外、不連資料庫。
- SQL 語法錯誤、非單一 DML：沿用 SqlDryRunAnalyzer 既有拒絕邏輯。
- commit 路徑執行期錯誤（違反約束等）：交易 rollback，回報
  `ExecutionError`，`Committed=false`。

## 測試策略（TDD）

- **Application**（`DmlExecutionServiceTests`）：
  - Production profile → 拒絕、repository 不被呼叫。
  - Development/Testing/Staging + confirm=false → 走 dry run repo。
  - 非正式 + confirm=true → 走 execute repo。
  - profile 不存在／停用 → 拒絕。
- **Infrastructure**（`SqlReadOnlyValidatorTests`）：
  - 放行：純 SELECT、CTE SELECT、多句 SELECT、DECLARE+SET+SELECT、
    SET ISOLATION LEVEL。
  - 拒絕：INSERT/UPDATE/DELETE/MERGE、`WITH ... DELETE`、`SELECT 1; DELETE`、
    `SELECT ... INTO`、EXEC、DDL、語法錯誤。
- **McpServer**（比照 `ConfirmGateTests`）：`execute_sql` 未 confirm 回摘要
  不執行、confirm 才執行。
- **Desktop**（ViewModel 測試）：Production 連線時 `ExecuteDmlCommand`
  不可執行；確認回呼取消時不 commit。
