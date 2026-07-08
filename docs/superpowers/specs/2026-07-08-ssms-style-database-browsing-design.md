# SSMS 式資料庫瀏覽設計

- 日期：2026-07-08
- 狀態：已與使用者確認設計方向

## 目標

將左側面板從「單一連線資料庫」改為 SSMS 式的階層瀏覽：連線（Host）下可看到伺服器上所有使用者資料庫，展開資料庫後瀏覽其 Tables / Views / Stored Procedures / Functions。

## 背景與現況

目前「一個連線 = 一個資料庫」烙印在兩處：

1. `ConnectionProfile.Database` 欄位（`src/Specurai.Domain/Entities/ConnectionProfile.cs`）——連線設定檔綁定單一資料庫。
2. `ConnectionManager.BuildConnectionString`（`src/Specurai.Infrastructure/Services/ConnectionManager.cs`）設定 `InitialCatalog = profile.Database`——全系統約 20 個 Repository 都透過 `Func<string?>` 工廠呼叫 `GetCurrentConnectionString()` 取得連線字串。

既有多資料庫先例（可重用）：

- `PerformanceDiagnosticsRepository.GetIndexStatusForDatabaseAsync`：複製連線字串並改寫 `InitialCatalog` 逐庫查詢。
- `DatabaseInfoRepository.GetDatabaseNamesAsync`：`sys.databases WHERE database_id > 4` 列舉使用者資料庫。
- `ColumnSearchService`：`Func<string?, IRepository>` 工廠平行跨連線查詢。
- Missing/Unused Index 報表：資料庫下拉選單 UI 先例。

## 已確認的設計決策

| 決策點 | 結論 |
|--------|------|
| ConnectionProfile.Database 欄位 | 保留，語意改為「此連線的預設資料庫」；`connections.json` 完全相容，不需遷移 |
| 當前資料庫上下文 | 全域單一「當前資料庫」；樹狀圖選取資料庫即切換全域當前資料庫 |
| 資料庫列舉範圍 | 僅使用者資料庫（`database_id > 4 AND state = 0`） |
| MCP/CLI | 一併納入，維持 CLI⇄MCP 功能對齊 |
| 實作方案 | 方案 A：連線字串層覆寫（見下） |
| 覆寫持久化 | Session 層級、不持久化；重啟回到設定檔預設資料庫 |

### 曾評估但未採用的方案

- 方案 B（`databaseName` 參數貫穿 Service/Repository 層）：60–90 個呼叫點，與全域當前資料庫模型不符，過度設計。
- 方案 C（依資料庫建 Repository 工廠）：適合平行跨庫場景，目前單一當前庫用不到。

## 設計

### 1. 資料模型與 ConnectionManager

`ConnectionProfile` 不變。`IConnectionManager`（`src/Specurai.Application/Services/IConnectionManager.cs`）新增：

```csharp
string GetCurrentDatabase();              // 覆寫值 ?? 當前設定檔.Database
void SetCurrentDatabase(string? name);    // null = 重設回設定檔預設
Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct);  // 使用者資料庫清單
event EventHandler? CurrentDatabaseChanged;
```

行為規則：

- `GetCurrentConnectionString()` 組字串時 `InitialCatalog = 覆寫 ?? profile.Database`——唯一組字串點，全系統 Repository 自動生效，Repository/Service 層零改動。
- `SetCurrentProfile()` 切換連線設定檔時自動清除資料庫覆寫。
- `GetDatabasesAsync` 實作於 Infrastructure，查詢 `sys.databases WHERE database_id > 4 AND state = 0`；列舉失敗（權限不足）時 degrade 為僅回傳設定檔預設資料庫。

### 2. 側邊欄樹狀圖

樹狀結構從兩層變三層：

```
【預備】Gma-Staging        ← 連線下拉（不變）
├─ 📁 GINLEE-test  ★當前   ← 新增：資料庫層
│  ├─ Tables (528)
│  ├─ Views (42)
│  ├─ Stored Procedures (38)
│  └─ Functions (3)
├─ 📁 GINLEE-mis            ← 未展開，懶載入
└─ 📁 moldplan-quartz-ginlee
```

- **懶載入**：切換連線後只列資料庫名稱清單；展開/點選資料庫節點 = 呼叫 `SetCurrentDatabase()` 並載入該庫四組物件。
- **單一展開**：同一時間只有當前資料庫展開；切換時原資料庫自動收合（避免「樹上看得到但不是當前庫」的混淆）。
- 當前資料庫節點以粗體/圖示標示；連線後預設自動展開設定檔的預設資料庫。
- 「搜尋物件」框行為不變（搜尋當前資料庫內物件）。
- Production 環境警示維持在連線設定檔層級（同一 Host 上所有庫視為同一環境）。
- 分頁行為維持與「切換連線」相同的既有語意：已開啟分頁不自動關閉；物件明細分頁快取鍵改為 `TableDetail:{Database}.{Schema}.{Name}`，分頁標題顯示資料庫名。

### 3. 全系統功能影響

自動生效、無需改動（透過 `GetCurrentConnectionString()`）：

| 功能 | 說明 |
|------|------|
| 物件明細（欄位/索引/關聯/參數/定義） | `ITableQueryService` 走覆寫後的連線字串 |
| SQL 查詢分頁 | 對當前資料庫執行 |
| Excel 匯出（單表/全部） | 匯出當前資料庫 |
| 說明編輯（資料表/欄位描述） | 寫入當前資料庫的 extended properties |
| 欄位使用率掃描、資料表統計 | 分析當前資料庫 |

本來就是 Server 層級、不受影響：維護計劃、備份/還原、Agent Jobs、復原模式、健康監控、效能診斷、Missing/Unused Index 報表、Schema 比對、多資料庫欄位搜尋、多環境使用率比對。

需小幅調整：

- `MainWindowViewModel.CurrentEnvironmentDatabase` 改為顯示當前資料庫（非設定檔資料庫）。
- 文件分頁標題含資料庫名時取自 `GetCurrentDatabase()`。
- 訂閱 `CurrentDatabaseChanged` 重載側邊欄物件樹（與既有 `CurrentProfileChanged` 並列）。

### 4. MCP/CLI 對齊

| 能力 | MCP | CLI |
|------|-----|-----|
| 列出 Host 上的資料庫 | 新工具 `list_databases` | 新命令 `specurai databases` |
| 切換當前資料庫 | 新工具 `switch_database`（session 內有效） | 既有 `--database` 參數已可指定，不需新增 |
| 查詢時顯示當前庫 | `list_connections` 回傳值加註當前資料庫 | 同左 |

- MCP `switch_database` 語意與 Desktop 一致：影響該 MCP server 行程內所有後續工具呼叫，直到 `switch_connection` 重設。
- CLI 為一次性行程，`--database` 已可覆寫，僅補 `databases` 列表命令。

### 5. 錯誤處理

- 資料庫列舉失敗（權限不足/離線）→ 側邊欄 degrade 為僅顯示設定檔預設資料庫，不阻斷連線。
- 切換到的資料庫中途離線 → 物件載入失敗訊息顯示於樹狀圖區，維持在原資料庫。
- `SetCurrentDatabase` 不預先驗證庫名（連線時自然報錯）；MCP `switch_database` 先對照 `list_databases` 結果回傳友善錯誤。

### 6. 測試策略

TDD，沿用 xUnit + NSubstitute + FluentAssertions：

- `ConnectionManagerTests`：覆寫優先序、切換設定檔清除覆寫、事件觸發（現無測試，順勢補上核心行為）。
- `ObjectTreeViewModelTests`：資料庫節點載入、懶載入、單一展開、切換觸發 `SetCurrentDatabase`。
- `MainWindowViewModel` 相關新增行為測試。
- MCP `DatabaseTools` 與 CLI `DatabasesCommand` 測試。
- 手動驗證清單：對照 SSMS 確認庫清單、跨庫開啟物件、匯出、說明編輯寫入正確的庫。
