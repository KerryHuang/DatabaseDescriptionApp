# MCP 破壞性工具 confirm 閘門 + DI Smoke Test 設計

- 日期：2026-06-11
- 狀態：設計已核准
- 範圍：為 MCP 破壞性工具加 `confirm` 閘門（預設回摘要、`confirm:true` 才執行）；新增 McpServer 測試專案做 DI 解析 smoke test。

## 背景

CLI⇄MCP 對齊（B1–B5）後，MCP 破壞性工具為「全部暴露、無確認」。使用者決定改為需明確 `confirm:true` 才執行，於工具協定層保留確認語意，降低 LLM 自動編排誤觸風險。另，B5 的 recovery-model Critical（DI 漏註冊）暴露出「MCP 薄包裝工具缺自動化安全網」，需補 DI smoke test。

## Part A：confirm 閘門

對下列**真正破壞性/不可逆**工具，各加最後一個參數 `bool confirm = false`：
`set_recovery_model`、`restore_run`、`migration_apply`、`migration_log_resize`。

- `backup_run` **不加**（備份為保護性、可重跑的非破壞性操作）。
- read-only 工具（analyze/preview/verify/info/history/list/columns 等）不動。

行為：
- `confirm == false`（預設）：**不執行**，回傳人/AI 可讀的「將執行什麼」摘要，並提示加 `confirm:true` 執行。
- `confirm == true`：執行原本的破壞性動作。

各工具摘要內容：

| 工具 | confirm=false 摘要 |
|---|---|
| `set_recovery_model` | `將把 [{database}] 的 Recovery Model 設為 {normalized}。加 confirm:true 執行。`（仍先做 FULL/SIMPLE/BULK_LOGGED 驗證） |
| `restore_run` | 解析 current profile（null 則回引導訊息）後：`將從 {path} 還原到 {target ?? profile.Database}（模式 {restoreMode}{；overwrite 會覆蓋現有資料庫，無法復原}）。加 confirm:true 執行。`（仍先驗證 mode=new 需 target） |
| `migration_apply` | 先跑唯讀 analyze + generate（排除高風險）：0 項 → `沒有可執行的差異（高風險已排除）。`；否則 → `將對 {target} 套用 {N} 項變更（高風險已排除）。加 confirm:true 執行。` |
| `migration_log_resize` | 解析 target（null 則回找不到）、驗證 64~102400 後：`將把 {target} 的 LDF 調整為 {sizeMb} MB。加 confirm:true 執行。` |

工具 `[Description]` 補上「預設僅回傳摘要，需 confirm:true 才實際執行」。

實作原則：摘要分支與執行分支共用前置的解析/驗證（profile 解析、enum 映射、範圍檢查），僅在「實際呼叫服務」前以 `if (!confirm) return 摘要;` 切出。`migration_apply` 的 analyze+generate 屬唯讀，置於 confirm 檢查之前以便摘要能報出差異數。

## Part B：McpServer.Tests + DI smoke test

- 新增 `tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`（net8.0、xUnit + FluentAssertions），參考 `Specurai.McpServer`、`Specurai.Infrastructure`，並加入 `Specurai.sln`。
- 測試 `AllMcpToolInjectedServices_ShouldBeResolvable`：
  1. `var provider = new ServiceCollection().AddSpecuraiCore().BuildServiceProvider();`
  2. 反射 `typeof(BackupTools).Assembly` 中所有具 `[McpServerToolType]` 的型別。
  3. 對每個型別的 public static 方法中具 `[McpServerTool]` 者，逐一檢查其參數。
  4. 對「參數型別為介面且 `Namespace` 以 `Specurai` 開頭」者，斷言 `provider.GetService(paramType)` 不為 null（聚集所有缺漏一次報出）。
- 此測試在 B5 修復前會失敗（`IDatabaseRecoveryModelService` 無法解析），修復後通過，作為日後薄包裝工具的 DI 安全網。

## 測試與驗證

- Part A：MCP 工具沿用「薄包裝」慣例，但 confirm 閘門引入可測的分支邏輯。鑑於工具靜態方法以服務為參數，**可用 NSubstitute mock 服務做單元測試**（驗證 confirm=false 不呼叫破壞性服務、confirm=true 有呼叫）。將於新測試專案中為各破壞性工具加「confirm=false 不執行」「confirm=true 執行」測試。
- Part B：smoke test 本身即驗證。
- 全程 `dotnet build` + `dotnet test`（McpServer.Tests 等）綠燈 + code review。

## 不在範圍（YAGNI）

- 不為 read-only 或 `backup_run` 加 confirm。
- 不改變既有非破壞性工具行為。
- 不引入跨工具的結構化 `{success,...}` 統一回傳格式（屬另案）。

## 刻意取捨

- 本案首次為 MCP 工具引入單元測試（透過新測試專案）。這與先前「MCP 薄包裝無測試」慣例不同，理由：confirm 閘門有真實分支邏輯、且 DI smoke test 需要測試專案承載；屬合理演進。
