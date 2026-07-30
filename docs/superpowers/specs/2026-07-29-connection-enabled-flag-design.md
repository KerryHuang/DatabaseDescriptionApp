# 連線設定「啟用」欄位設計

日期：2026-07-29

## 目標

`ConnectionProfile` 新增「啟用」狀態。只有啟用的連線能在各功能的資料庫連線選擇、查詢與比對中被選用；停用的連線仍保留在連線設定中可管理、可匯出，但不會出現在功能面的選項裡。

過濾範圍涵蓋 Desktop、CLI、MCP 三個進入點，語意一致。

## 決策

**過濾落在 Manager 層，以新方法表達，並在關鍵路徑加 fail-safe。**

`GetAllProfiles()` 語意不變（回傳全量），另開 `GetEnabledProfiles()`。管理型入口用前者，選用型入口用後者。

考慮過但不採用的替代方案：

- 反轉 `GetAllProfiles()` 語意，只回啟用的，另開 `GetAllProfilesIncludingDisabled()`。漏改方向天然安全、改動點也最少，但方法名說 All 卻不回 All，長期誤導成本高。
- 不加方法，各呼叫點自行 `.Where(p => p.IsEnabled)`。46 處散落的過濾條件沒有單一事實來源，容易漏。

fail-safe 補回替代方案 A 的安全優勢：即使某個呼叫點漏改，實際要取用停用連線時仍會被 Manager 擋下。

## 1. Domain 與持久化

`ConnectionProfile` 新增：

```csharp
/// <summary>
/// 是否啟用（停用的連線不會出現在各功能的連線選擇中）
/// </summary>
public bool IsEnabled { get; set; } = true;
```

**向後相容的關鍵**：既有 `connections.json` 沒有這個欄位。System.Text.Json 對 JSON 中不存在的屬性不會呼叫 setter，物件初始化的 `= true` 因此會保留，舊設定檔載入後全部維持啟用。此行為必須有專門測試釘住（讀一份無 `isEnabled` 欄位的 JSON，斷言全部 `IsEnabled == true`），否則將來改成 `required` 或換序列化器會靜默把所有連線停用。

`ConnectionExportData` 不需修改；`Profiles` 是 `ConnectionProfile` 清單，欄位自動跟著走，匯出匯入原樣保留啟用狀態。

外部來源同步（`IExternalConnectionSource`）產生的 profile 走預設值 `true`。

## 2. Manager 層 API 與 fail-safe

`IConnectionManager` 新增：

```csharp
/// <summary>
/// 取得所有已啟用的連線設定（供功能面的連線選擇使用）
/// </summary>
IReadOnlyList<ConnectionProfile> GetEnabledProfiles();
```

實作為 `GetAllProfiles().Where(p => p.IsEnabled)`，排序沿用 `ConnectionProfileComparer.Instance`。

fail-safe 三處：

- `SetCurrentProfile(Guid)`：目標停用時不動作，與目前「找不到 profile」的行為一致。
- `GetConnectionString(Guid)`：目標停用時回 `null`。
- `GetCurrentProfile()`：自動挑預設連線的邏輯改為只挑「啟用且 `IsDefault`」的 profile。

臨時 profile（`RegisterTemporaryProfiles` 註冊、CLI 使用）一律視為啟用，不受上述限制影響。

### 停用衝突的自動切離

放在 `UpdateProfile`：當這次更新把 profile 從啟用改為停用時，

1. 若它是目前連線，改指向第一個啟用的 profile；沒有啟用的則 `_currentProfileId = null`。兩種情況都觸發 `CurrentProfileChanged`。
2. 一併把它的 `IsDefault` 設為 `false`，避免留下一個永遠選不到的預設連線。

## 3. 呼叫點分流

維持 `GetAllProfiles()`（管理型，要看得到停用的）：

- `ConnectionSetupViewModel`
- `ExportConnectionsViewModel`、`ImportConnectionsViewModel`
- `ConnectionCrudTools`（MCP 連線 CRUD）
- `ConnCommand`（CLI 連線管理命令；列表額外顯示停用標記）

改用 `GetEnabledProfiles()`（選用型）：

- Desktop：`MainWindowViewModel`（連線選單）、`SqlQueryDocumentViewModel`、`ColumnSearchDocumentViewModel`、`BackupRestoreDocumentViewModel`、`SchemaCompareDocumentViewModel`、`SchemaMigrationDocumentViewModel`、`UsageAnalysisDocumentViewModel`
- CLI：`ConnectionResolver`、`ColumnsCommand`、`SqlCommand`、`SchemaCommand`、`UsageCommand`、`MigrationCommand`
- MCP：`ProfileResolver`（`Resolve` 與 `ResolveMultiple`）、`ConnectionTools`、`MigrationTools`

`ColumnSearchService` 不在此列：它刻意維持 `GetAllProfiles()`，因為多資料庫欄位搜尋要能顯示每個 profile 的名稱（包含已停用的），跳過停用連線是靠 `GetConnectionString(profileId)` 對停用連線回傳 `null`、該次查詢被跳過來達成，而不是在來源清單上過濾。

### 明確指定停用連線時的錯誤訊息

`ProfileResolver`（MCP）與 `ConnectionResolver`（CLI）在啟用清單找不到指定名稱／ID 時，再到全量清單找一次：

- 全量找得到 → 回「連線 "X" 已停用，請先在連線設定中啟用」
- 全量也找不到 → 維持原本的「找不到連線 "X"」

這需要兩個 resolver 從回傳 `null` 改為回傳帶原因的結果，是本次唯一會動到既有簽章的地方。

## 4. UI

連線設定畫面清單新增「啟用」CheckBox 欄，`IsChecked` 雙向綁定 `IsEnabled`，勾選變更即呼叫 `UpdateProfile` 存檔，不需另按儲存。

停用列以灰階呈現，作法是在欄位樣板上綁 `IsEnabled` 控制 `Opacity`。不走專案慣例的 code-behind `LoadingRow`：該事件只在列載入時觸發，勾選切換後不會重繪，灰階會停在舊狀態。

編輯表單同步新增「啟用」CheckBox。`ConnectionSetupViewModel` 加上 `[ObservableProperty] private bool _isEnabled = true;`，並在 `OnSelectedProfileChanged`、`ClearForm` 與儲存路徑中一併處理。

停用目前連線時，主視窗的連線指示透過既有的 `CurrentProfileChanged` 訂閱自動更新到新目標。

## 5. 測試

- Domain：`ConnectionProfile` 預設 `IsEnabled == true`。
- Infrastructure：
  - 舊格式 JSON（無 `isEnabled`）載入後全部啟用。
  - `GetEnabledProfiles()` 過濾與排序正確。
  - 停用目前連線時自動切離到第一個啟用的 profile，並清除 `IsDefault`。
  - 沒有其他啟用 profile 時，停用目前連線後目前連線為 `null`。
  - 停用 profile 的 `GetConnectionString` 回 `null`、`SetCurrentProfile` 不動作。
  - 臨時 profile 不受停用邏輯影響。
- Application：`ColumnSearchService` 跳過停用連線。
- Desktop：`ConnectionSetupViewModel` 切換啟用會存檔；挑 2–3 個代表性文件 ViewModel 驗證連線清單排除停用項。
- CLI／MCP：resolver 對停用連線回「已停用」訊息，對不存在的名稱回「找不到」。

## 影響範圍

約 25 個檔案，跨 Domain、Application、Infrastructure、Desktop、Cli、McpServer 六個專案。
