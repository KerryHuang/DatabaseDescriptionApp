# CLI ⇄ MCP 功能對齊設計

- 日期：2026-06-10
- 狀態：已核准範圍，待 spec review
- 範圍：補齊 `Specurai.Cli` 與 `Specurai.McpServer` 之間的功能缺口，使兩者皆與主專案（Application 服務面）對齊。

## 背景

盤點 CLI 命令樹與 MCP 工具面後，發現兩邊互有缺口：

- CLI 缺少數個 MCP 已提供的功能（連線編輯/匯出、物件參數、CREATE TABLE 產生、非-Specurai 工作列表、健康監控 SQL 匯出等）。
- MCP 缺少數個 CLI 已提供的運維執行功能（backup / restore / recovery-model / migration）。

其中「連線匯出」是 2026-06-09 才修正過的主專案功能（`ConnectionExportService` 保留環境欄位），但 CLI 尚未接上，是「CLI 未跟上主專案」最具體的例子。

## 核心原則

**每個缺口在另一邊都已實作完成**，且兩個專案都透過共用的 `AddSpecuraiCore()`（`src/Specurai.Infrastructure/ServiceRegistration.cs`）註冊了所有服務：

- `IConnectionManager`、`IConnectionExportService`、`ITableQueryService`、`IAgentJobService`、`IHealthMonitoringService`
- `IBackupService`、`IDatabaseRecoveryModelService`、`ISchemaMigrationService`、`ISchemaMigrationExecutor`

因此本案為**純展示層接線**：

- **不**新增/修改 Domain、Application、Infrastructure 程式碼。
- **不**修改 DI 註冊（服務皆已註冊）。
- CLI：新增 `Command` 類別或於既有命令群組內新增子命令；新根命令需在 `Program.cs` 以 `rootCommand.AddCommand(...)` 註冊（子命令掛在既有群組內則不需動 `Program.cs`）。
- MCP：新增帶 `[McpServerToolType]` / `[McpServerTool]` 的工具類別即可，由 `.WithToolsFromAssembly()` 自動探索，**不需**改 `Program.cs`。

每個新展示層單元一律**鏡像另一邊既有實作對同一服務的呼叫方式**，確保行為一致。

## 範圍清單

### 方向 A — CLI 補上（鏡像現有 MCP 工具）

| 新增 CLI | 掛載位置 | 對應服務 | 來源 MCP 工具 |
|---|---|---|---|
| `conn update` | ConnCommand 子命令 | `IConnectionManager` | `update_connection` |
| `conn export` | ConnCommand 子命令 | `IConnectionExportService` | `export_connections`（含環境欄位）|
| `tables parameters` | TablesCommand 子命令 | `ITableQueryService` | `get_parameters` |
| `tables create-sql` | TablesCommand 子命令 | `ITableQueryService` | `get_create_table_sql` |
| `tables row-count`（精確）| TablesCommand 子命令 | `ITableQueryService` | `get_exact_row_count` |
| 資料表/欄位統計 | TablesCommand 或 UsageCommand 子命令 | `ITableQueryService` | `get_table_statistics`、`get_column_usage_statistics` |
| `jobs list --include-non-specurai`（旗標）| JobsCommand `list` 加旗標 | `IAgentJobService` | `list_non_specurai_jobs` |
| `health export-sql` | HealthCommand 子命令 | `IHealthMonitoringService` | `export_health_monitoring_sql` |

### 方向 B — MCP 補上（鏡像現有 CLI 命令，全部暴露，含破壞性操作）

| 新增 MCP 工具類別 | 對應服務 | 來源 CLI |
|---|---|---|
| `BackupTools`：`backup_run` / `backup_verify` / `backup_info` / `backup_history` | `IBackupService` | `backup` |
| `RestoreTools`：`restore_run` | `IBackupService` | `restore` |
| `RecoveryModelTools`：`list_recovery_models` / `set_recovery_model` | `IDatabaseRecoveryModelService` | `recovery-model` |
| `MigrationTools`：`migration_analyze` / `migration_dry_run` / `migration_apply` / `migration_preview` / `migration_log_resize` | `ISchemaMigrationService`、`ISchemaMigrationExecutor` | `migration` |

### 安全策略決議

MCP 端的破壞性/運維操作（`restore_run`、`migration_apply`、`set_recovery_model`、`backup_run`）採**完全對齊 CLI、全部暴露**，不額外加 confirm 旗標等防護。理由：使用者明確要求與 CLI 完全對齊；行為一致性優先。

## 分批計畫

每批：TDD（先寫失敗測試）→ `dotnet test` 全綠 → `superpowers:requesting-code-review` → 才進下一批。

| 批次 | 內容 |
|---|---|
| **B1 · CLI 連線** | `conn update`、`conn export` |
| **B2 · CLI 物件與診斷** | `tables parameters`、`tables create-sql`、`tables row-count`、資料表/欄位統計 |
| **B3 · CLI 其他** | `jobs list --include-non-specurai`、`health export-sql` |
| **B4 · MCP 備份還原** | `BackupTools`、`RestoreTools` |
| **B5 · MCP 復原與遷移** | `RecoveryModelTools`、`MigrationTools`（含破壞性 `apply`/`set`）|

排序理由：先做價值最高且最獨立的 CLI 連線對齊；MCP 破壞性最高的遷移/復原留到最後，待前面批次建立信心。

## 測試方式

- 各專案沿用既有模式：**xUnit + NSubstitute + FluentAssertions**。
- CLI 測試置於 `tests/Specurai.Cli.Tests`，MCP 測試置於對應測試專案，鏡像既有 command/tool 測試寫法（以 `Substitute.For<T>()` mock 服務介面、驗證對服務的呼叫參數與輸出格式）。
- 測試命名沿用 `[Method]_[Condition]_[Expected]` 繁體中文慣例。

## 不在範圍內（YAGNI）

- 不為 MCP 破壞性操作新增額外確認機制（已決議全部暴露）。
- 不重構既有命令/工具，除非接線過程中為新增功能所必需。
- 不調整 Desktop 層。

## 命名待確認項

- 非-Specurai 工作列表採旗標 `jobs list --include-non-specurai`（與現有 `jobs list` 一致），而非新子命令。如偏好子命令可於 review 時調整。
