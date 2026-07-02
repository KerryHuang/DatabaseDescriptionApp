# 維護計劃頁：目標／還原資料庫改為伺服器資料庫下拉 設計文件

- **日期**：2026-07-02
- **狀態**：設計已核准，待撰寫實作計畫
- **影響範圍**：Application（`IMaintenancePlanService` 新增一個方法）、Desktop（維護計劃 ViewModel／View）
- **分支**：延續維護計劃頁強化系列（同一頁的相關功能）

## 1. 背景與目標

維護計劃精靈「步驟 1：基本設定」的「目標資料庫」（原標籤「資料庫名稱」）與「還原資料庫」（原標籤「測試資料庫名稱」）目前為純文字欄位：目標資料庫開啟時從目前連線設定檔自動帶入**單一**資料庫（去掉 `-test` 等後綴），平常唯讀，只有平台為「其他」時才可手動輸入。使用者希望能**從目前連線伺服器上的資料庫清單選取**，不必手打。

本次要達成：

- **目標資料庫**改為**唯讀下拉**（只能從伺服器清單挑選）——它是「要被維護的來源庫」，必須是伺服器上實際存在的資料庫。
- **還原資料庫**改為**可編輯下拉**（可挑可打）——允許輸入一個尚不存在的測試庫名，還原時會建立它。

## 2. 現況調查重點（實作前已確認）

| 項目 | 位置 | 說明 |
|------|------|------|
| 目標資料庫欄位 | `MaintenancePlanDocumentViewModel.DatabaseName`（第 95 行） | DI 建構函式（第 307-321 行）以 `_connectionManager.GetCurrentProfile()` 取設定檔資料庫、去除 `-Test`/`-test`/`_Test`/`_test` 後綴後帶入。 |
| 還原資料庫欄位 | `MaintenancePlanDocumentViewModel.TestDatabaseName`（第 136 行） | `OnDatabaseNameChanged`（第 406-412 行）在目標資料庫變更時自動帶入 `{目標}-test`。 |
| 唯讀切換 | `IsDatabaseNameEditable => SelectedPlatform == "其他"`（第 113 行） | 目前僅供目標資料庫 `TextBox` 的 `IsReadOnly="{Binding !IsDatabaseNameEditable}"` 使用（View 第 194 行）。**除此之外無其他引用。** |
| 資料庫清單來源（可重用） | `IDatabaseInfoRepository.GetDatabaseNamesAsync()`（Domain 介面；`DatabaseInfoRepository` 實作） | 已有：`SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name`，走目前連線字串工廠，回傳伺服器上使用者資料庫清單。**Domain／Infrastructure 不需改動。** |
| 服務層閘道 | `MaintenancePlanService`（Application）注入 `IDatabaseInfoRepository _dbInfoRepo` | VM 透過 `IMaintenancePlanService`（`_planService`）存取伺服器，符合「ViewModel 不含查詢邏輯」憲章。 |
| 可編輯下拉控件 | — | Avalonia 11 的 `ComboBox` **不支援**可編輯輸入（無 `IsEditable`）。「可挑可打」須用 `AutoCompleteBox`（`Text` 兩向綁定、`ItemsSource` 提供建議、`FilterMode` 篩選、允許任意文字）。專案尚未使用過，屬 Avalonia 內建控件，Semi.Avalonia 有主題化。 |

## 3. 設計決策（與使用者確認）

| 決策 | 選定 |
|------|------|
| 處理範圍 | **單選**（一次一個資料庫），不做多選批次 |
| 目標資料庫控件 | **唯讀下拉（`ComboBox`）**，只能從伺服器清單挑選，不可打字 |
| 還原資料庫控件 | **可編輯下拉（`AutoCompleteBox`）**，可挑可打；輸入不存在的庫名時仍可還原（建立該庫） |
| 清單來源 | 重用 `IDatabaseInfoRepository.GetDatabaseNamesAsync()`（目前連線伺服器的使用者資料庫） |
| 載入時機 | 開啟頁面時載入一次（建構函式 fire-and-forget，與 `LoadJobsAsync`／`DetectServerPlatformAsync` 一致），不加「重新整理」按鈕 |

## 4. 元件設計

### 4.1 Application：`IMaintenancePlanService` 新增方法

`IMaintenancePlanService`（`src/Specurai.Application/Services/IMaintenancePlanService.cs`）新增：

```csharp
/// <summary>
/// 取得目前連線伺服器上的使用者資料庫名稱清單（供維護計劃選取目標／還原資料庫）
/// </summary>
/// <param name="ct">取消權杖</param>
Task<IReadOnlyList<string>> GetServerDatabasesAsync(CancellationToken ct = default);
```

`MaintenancePlanService`（`src/Specurai.Application/Services/MaintenancePlanService.cs`）實作，委派既有 repository 方法：

```csharp
/// <inheritdoc />
public Task<IReadOnlyList<string>> GetServerDatabasesAsync(CancellationToken ct = default)
    => _dbInfoRepo.GetDatabaseNamesAsync(ct);
```

（不新增 Domain／Infrastructure 方法；`GetDatabaseNamesAsync` 已存在且行為符合。）

### 4.2 Desktop：`MaintenancePlanDocumentViewModel`

- 新增可觀察集合：

```csharp
/// <summary>目前連線伺服器上的使用者資料庫清單（供目標／還原資料庫下拉）</summary>
public ObservableCollection<string> AvailableDatabases { get; } = [];
```

- 新增載入方法：

```csharp
/// <summary>載入目前連線伺服器上的資料庫清單，供下拉選取。</summary>
public async Task LoadAvailableDatabasesAsync()
{
    if (_planService == null) return;

    try
    {
        var databases = await _planService.GetServerDatabasesAsync();
        AvailableDatabases.Clear();
        foreach (var db in databases)
            AvailableDatabases.Add(db);
    }
    catch
    {
        // 載入失敗維持空清單，欄位仍可（還原欄）手動輸入
    }
}
```

- DI 建構函式：先設定 `DatabaseName` 預設（既有邏輯，第 307-321 行）**之後**再 fire-and-forget 載入清單，讓預帶的目標庫名在清單載入後能於 `ComboBox` 反映為選中項：

```csharp
_ = LoadJobsAsync();
_ = DetectServerPlatformAsync();
_ = LoadAvailableDatabasesAsync();
```

  - **時序說明**：`DatabaseName` 於建構函式同步設定；`AvailableDatabases` 非同步於稍後填入。`ComboBox` 的 `SelectedItem` 兩向綁 `DatabaseName`（字串），當 `AvailableDatabases` 填入且包含該名稱時，選取會自動對應（預帶的目標庫名為設定檔主庫，通常存在於使用者資料庫清單）。若清單不含該名稱（例如剛好為測試庫已被去尾綴、或連線異常），`ComboBox` 顯示未選取，使用者需自清單選擇——與「目標庫必須存在」的語意一致。
- 移除 `IsDatabaseNameEditable` 屬性（第 113 行）——`ComboBox` 選取不需唯讀切換，且該屬性移除後無其他引用。
- **保留** `OnDatabaseNameChanged`（自動帶 `{目標}-test` 至 `TestDatabaseName`）；目標資料庫透過 `ComboBox` 選取變更時照常觸發。

### 4.3 Desktop：`MaintenancePlanDocumentView.axaml`

第一列「目標資料庫」——由 `TextBox` 改為 `ComboBox`（只能選）：

```xml
<StackPanel Grid.Column="0" Spacing="4">
    <TextBlock Text="目標資料庫"/>
    <ComboBox ItemsSource="{Binding AvailableDatabases}"
              SelectedItem="{Binding DatabaseName}"
              HorizontalAlignment="Stretch"
              PlaceholderText="從伺服器選擇資料庫"/>
</StackPanel>
```

第三列「還原資料庫」——由 `TextBox` 改為 `AutoCompleteBox`（可挑可打）：

```xml
<StackPanel Spacing="4">
    <TextBlock Text="還原資料庫"/>
    <AutoCompleteBox Text="{Binding TestDatabaseName}"
                     ItemsSource="{Binding AvailableDatabases}"
                     FilterMode="Contains"
                     Watermark="可選既有庫或輸入新測試庫名"/>
</StackPanel>
```

- 移除原 `IsReadOnly="{Binding !IsDatabaseNameEditable}"` 綁定。
- 其餘版面（欄位配置、平台下拉、路徑列等）不變。

## 5. 錯誤處理

| 情境 | 行為 |
|------|------|
| 無目前連線 / 連線字串為空 / 查詢失敗 | `LoadAvailableDatabasesAsync` 吞例外、`AvailableDatabases` 為空。目標資料庫下拉為空（維護計劃本需連線才能跑，屬預期）；還原資料庫仍可手動輸入。不崩潰。 |
| 預帶目標庫名不在清單 | `ComboBox` 顯示未選取，使用者自清單挑選。 |
| 還原資料庫輸入不存在的庫名 | 允許（`AutoCompleteBox` 接受任意文字）；還原步驟建立該測試庫。 |

## 6. 測試

- **Application**（`MaintenancePlanServiceTests`）：`GetServerDatabasesAsync` 委派 `IDatabaseInfoRepository.GetDatabaseNamesAsync`——mock repository 回傳清單，驗證服務回傳相同內容。
- **Desktop VM**（`MaintenancePlanDocumentViewModelDatabaseTests`，新檔）：
  - `LoadAvailableDatabasesAsync` 於 `_planService` 回傳清單時，`AvailableDatabases` 填入對應項目。
  - 無 `_planService`（設計時建構函式）時呼叫 `LoadAvailableDatabasesAsync` 不丟例外、`AvailableDatabases` 維持空。
  - `GetServerDatabasesAsync` 拋例外時 `LoadAvailableDatabasesAsync` 吞掉、`AvailableDatabases` 維持空、不丟例外。
- 命名 `[方法]_[條件]_[預期]`（繁體中文），xUnit + NSubstitute + FluentAssertions。
- `ComboBox`／`AutoCompleteBox` 綁定與 `SelectedItem` 時序的實際顯示，靠建置 + 手動驗證（與備份頁對話框一致，UI 綁定不寫單元測試）。

## 7. 範圍外（YAGNI）

- 不做多選、批次逐庫套用。
- 不驗證還原資料庫輸入的庫名是否存在（刻意允許新測試庫名）。
- 不加「重新整理清單」按鈕（開啟載入一次，與平台偵測一致）。
- 不改資料庫清單的過濾條件（沿用 `database_id > 4`）。
- 不改維護計劃的 SQL 產生／執行邏輯。
