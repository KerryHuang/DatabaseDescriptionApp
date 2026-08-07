# 連線設定檔來源標記與匯入修復 — 設計文件

日期：2026-08-07

## 背景與問題

使用者回報三個問題：

1. **連線清單全面重複**：`connections.json` 裡整組連線各存兩份（同名同 Server、不同 Id，
   第二組 `IsDefault` 全為 false）。根因是 MCP 工具 `import_connections`
   （`ConnectionCrudTools.cs`）完全不查重，每筆直接 `AddProfile`；Desktop 匯入視窗與
   CLI `conn import` 都有按名稱去重，唯獨 MCP 這條路沒有。
2. **外部匯入環境誤分類（危險）**：CLI 的 `ConnectionProfileParser` 解析 mpe show --json
   與簡易格式時從未設定 `Environment`，全部落在 entity 預設值 `Staging`（預備）。
   名稱寫 Production 的正式環境連線被標成【預備】，環境防護形同失效。
3. **無法區分外部／自建**：`ConnectionProfile` 沒有來源欄位，落地後分不出是外部匯入
   還是手動建立，顯示上也無從標記。

## 決策（使用者已拍板）

- 重複資料：直接刪除第二組副本（先備份）。
- 誤分類資料：12 筆「XX Production」直接改 `Environment = Production`。
- 「外部」定義：外部來源同步（Ansible inventory）＋ CLI `conn import` ＋
  MCP `import_connections` 建立的都算外部；UI 手動建立算自建。
- mpe 格式：確認輸出含 `envTag` 欄位（`dev` / `staging` / `prod`），直接對應環境。

## 設計

### A. 一次性資料修復（腳本，不入版控邏輯）

1. 備份 `%APPDATA%\Specurai\connections.json`。
2. 依（Name, Server, Database, Username）去重：同 key 出現兩筆時，保留第一組
   （原始 Id、含預設標記者），刪除後出現的副本。
3. 名稱含「Production」且環境為 Staging 的 12 筆改為 `Environment = Production`。
4. 僅該 12 筆補 `IsExternal = true`（其餘既有連線來源無法確判，一律視為自建，
   使用者可自行調整）。

### B. Domain：`ConnectionProfile.IsExternal`

- 新增 `public bool IsExternal { get; set; }`，預設 `false`（自建）。
- 舊 JSON 無此欄位 → 反序列化為 false，向下相容；`SaveProfiles` 後自然帶欄位。
- 不用 enum：目前僅兩種來源，YAGNI。
- 匯出格式（`ConnectionExportData`）序列化整個 `ConnectionProfile`，`IsExternal`
  隨匯出／匯入檔案自然往返，Desktop 匯入視窗不需改動。

設為 `true` 的建立路徑：

| 路徑 | 位置 |
|------|------|
| 外部來源同步 | `InventoryConnectionSource.BuildProfileAsync` |
| CLI 匯入（mpe＋簡易格式） | `ConnectionProfileParser.FromMpeJson` / `FromSimpleJson` |
| MCP 匯入 | `ConnectionCrudTools.ImportConnections` |

### C. Bug 修復

1. **MCP `import_connections` 去重**：比照 CLI `conn import`——按名稱
   （OrdinalIgnoreCase）比對既有連線；存在則更新（Server / Database / AuthType /
   Username / Password / Environment / IsExternal），不存在才新增。
   回傳訊息改為「已匯入 N 個、已更新 M 個連線設定」。
2. **CLI parser 環境對應**：
   - mpe 格式：讀 `envTag` — `prod` → Production、`dev` → Development、
     `staging` → Staging；缺欄位或未知值維持預設 Staging。
   - 簡易格式：支援選用 `environment` / `Environment` 欄位（字串，對應
     `DatabaseEnvironment` 名稱，不分大小寫）；沒有則維持預設。

### D. 顯示標記

`ConnectionProfileDisplayConverter` 輸出格式改為：

```
【環境】【外部|自建】名稱 (預設)
```

例：`【正式】【外部】嘉泰 Production`、`【開發】【自建】MoldPlan-Schema (預設)`。
主視窗下拉與其他使用處共用此 converter，一處改動全面生效。

## 錯誤處理

- 資料修復腳本任何一步失敗即中止，保留備份檔不動原檔。
- MCP 匯入維持既有 try/catch 回傳錯誤訊息慣例。

## 測試

- Domain：`IsExternal` 預設 false；舊 JSON（無欄位）反序列化相容。
- CLI parser：`envTag` 三種值對應、缺欄位預設；簡易格式 `environment` 欄位。
- MCP import：同名走更新不新增；新名新增且 `IsExternal = true`。
- Converter：外部／自建前綴、預設標記組合輸出。

## 追加需求（2026-08-07 變更）：外部來源同步不落地

### 需求

「同步外部來源」取得的連線只存活於當下 process：App 關閉即消失，下次開啟需重新按同步。
MCP 亦同（活到 server 重啟）。CLI 因單次執行特性不提供同步。
CLI / MCP 的**檔案匯入**（`conn import`、`import_connections`）維持落地不變。

### 現況與缺口

`ConnectionManager` 已有 `_temporaryProfiles`（`RegisterTemporaryProfiles` 註冊、不寫入
`connections.json`），`GetAllProfiles` / `GetConnectionString(Guid)` 都已涵蓋它。但：

- **`SetCurrentProfile` 與 `GetCurrentProfile` 只查 `_profiles`**，臨時連線永遠無法成為
  目前連線。現行 `ConnectionSetupViewModel.Connect` 對外部連線的處理因此靜默失敗——
  這是既有 bug，也是新設計的前置阻礙。
- `SaveProfiles` 會把 `_currentProfileId` 寫入檔案；若目前連線是臨時的，會存下一個下次
  啟動不存在的 Guid（`GetCurrentProfile` 有自我修復，但不該寫髒值）。
- 外部來源的 DI 註冊（`IExternalSourceSettings` / `IExternalConnectionSource`）目前只在
  `Desktop/Program.cs`，McpServer 走 `AddSpecuraiCore` 拿不到。

### 設計

1. **`ConnectionManager` 支援臨時連線成為目前連線**
   - `SetCurrentProfile` / `GetCurrentProfile` 的查找範圍改為 `_temporaryProfiles` ＋ `_profiles`。
   - `SaveProfiles` 在目前連線為臨時連線時，`CurrentProfileId` 寫 null。
2. **Desktop 同步後整批註冊為臨時連線**
   - `SyncExternalSourceAsync` 取得結果後 `RegisterTemporaryProfiles(newProfiles)`，
     關閉連線設定視窗時主畫面 `LoadConnectionProfiles()` 即帶出（標【外部】）。
   - `Connect` 對外部連線不再重新註冊單筆，直接 `SetCurrentProfile`。
3. **外部來源 DI 移入 `AddSpecuraiCore`**，Desktop 移除重複註冊，McpServer 隨之可用。
4. **MCP 新增 `sync_external_connections` 工具**：同步後整批註冊為臨時連線，
   回報同步筆數；連線本身不落地。
5. **資料修復**：從 `connections.json` 刪除既有 12 筆外部正式連線（先備份），
   改由同步取得。

### 測試（追加）

- `ConnectionManager`：臨時連線可設為目前連線並取得連線字串；目前連線為臨時時
  存檔的 `CurrentProfileId` 為 null；臨時連線不寫入 `connections.json`。
- `ConnectionSetupViewModel`：同步後整批呼叫 `RegisterTemporaryProfiles`。
- MCP `sync_external_connections`：呼叫 `SyncAsync` 並註冊臨時連線、回報筆數。

## 已知不一致（本次不動）

`InventoryConnectionSource` 將 inventory 的 `staging` 環境映射為 `Testing`（測試），
與使用者手動建立的 `-Staging`（預備）慣例不一致；留待後續決定是否統一。
