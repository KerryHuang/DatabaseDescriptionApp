# 連線設定「環境」欄位 — 設計規格

- 日期：2026-06-09
- 狀態：已核准，待實作

## 目標

在連線設定中新增「環境」欄位，提供四個選項：**Development、Testing、Staging、Production**。
此欄位有兩個用途：

1. **標籤**：記錄並顯示每個連線屬於哪個環境。
2. **Production 防呆**：當目前連線為 Production 時，對其執行破壞性操作（DROP、還原、Schema Migration、Recovery Model 變更等）會在確認對話框加上紅色警告橫幅。

## 背景與限制

### 共用設定影響性（moldplan-change-database）

連線設定檔 `connections.json` 與 `C:\Users\zihao\source\repos\moldplan-change-database` 的 `MoldplanDbSwitcher` 共用，機制如下：

- **Specurai（本專案）** 寫入 `%APPDATA%\Specurai\connections.json`（`SpecuraiPaths.ResolveConfigFile`）。
- **MoldplanDbSwitcher** 透過 `ConnectionSourceService.LoadSpecuraiConnections()` **唯讀** 同一檔案，反序列化成自己的 `ConnectionProfile`（`PropertyNameCaseInsensitive = true`）。
- Specurai 是唯一寫入者；MoldplanDbSwitcher 不會回寫該檔案（它自己的設定寫在 `%APPDATA%\MoldplanDbSwitcher\connections.json`）。

**結論：新增欄位安全，影響極小。**

1. System.Text.Json 預設忽略未知屬性 → MoldplanDbSwitcher 讀到多出來的 `Environment` 會自動略過，不會壞。
2. MoldplanDbSwitcher 不回寫 Specurai 檔案 → 不會把 `Environment` 洗掉。
3. ⚠️ 既有 `AuthType` enum 以**數字**序列化（0/1），MoldplanDbSwitcher 也以數字對應。因此**不可引入全域 `JsonStringEnumConverter`**，否則會把 `authType` 改成字串而破壞另一專案讀取。

### 序列化決策：數字（非字串）

`Environment` 採**預設數字序列化**：

- 沿用 `AuthType` 既有做法，零序列化屬性、零轉換器。
- 符合本專案 <law>：Domain 層保持純淨，不引入序列化關注。
- MoldplanDbSwitcher 無論數字或字串都忽略該欄位 → 相容性零風險。

### 預設值：Staging

- 既有連線（`connections.json` 中無 `Environment` 欄位者）載入時預設 **Staging**。
- 新建連線預設 **Staging**。
- Staging 不觸發 Production 防呆。
- 機制：以屬性初始化值 `= DatabaseEnvironment.Staging` 達成。System.Text.Json 以無參數建構式建立物件時會執行屬性初始化值，JSON 缺欄位時保留該值。

## 設計

### 1. Domain 層（`Specurai.Domain`）

新增 `Entities/DatabaseEnvironment.cs`：

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 資料庫連線所屬環境
/// </summary>
public enum DatabaseEnvironment
{
    /// <summary>開發環境</summary>
    Development,
    /// <summary>測試環境</summary>
    Testing,
    /// <summary>預備環境</summary>
    Staging,
    /// <summary>正式環境</summary>
    Production
}
```

`Entities/ConnectionProfile.cs` 新增純屬性（無序列化屬性）：

```csharp
/// <summary>
/// 連線所屬環境
/// </summary>
public DatabaseEnvironment Environment { get; set; } = DatabaseEnvironment.Staging;
```

### 2. 設定表單（Desktop）

`ViewModels/ConnectionSetupViewModel.cs`：

- 新增 `[ObservableProperty] private DatabaseEnvironment _environment = DatabaseEnvironment.Staging;`
- 新增 `public static IReadOnlyList<DatabaseEnvironment> EnvironmentOptions { get; } = Enum.GetValues<DatabaseEnvironment>();`
- `OnSelectedProfileChanged`：`Environment = value.Environment;`（else 分支由 `ClearForm` 重置）
- `CreateProfileFromForm`：`Environment = Environment`
- `ClearForm`：`Environment = DatabaseEnvironment.Staging`

`Views/ConnectionSetupWindow.axaml`：

- 在「資料庫名稱」與「驗證方式」之間插入「環境」`ComboBox`：
  `ItemsSource="{Binding EnvironmentOptions}"`、`SelectedItem="{Binding Environment}"`。

### 3. Production 防呆（紅色警告橫幅）

`ViewModels/MainWindowViewModel.cs` 新增計算屬性：

```csharp
public bool IsCurrentProfileProduction =>
    _connectionManager?.GetCurrentProfile()?.Environment == DatabaseEnvironment.Production;

public string? CurrentEnvironmentDatabase =>
    _connectionManager?.GetCurrentProfile()?.Database;
```

`Views/ConfirmDialog`：新增可選的警告橫幅（紅色邊框／背景 + 警告文字），未提供時不顯示。

`Views/MainWindow.axaml.cs` 的 `ShowConfirmSaveDialogAsync`：

```csharp
private async Task<bool> ShowConfirmSaveDialogAsync(string message)
{
    string? banner = null;
    if (DataContext is MainWindowViewModel vm && vm.IsCurrentProfileProduction)
        banner = $"⚠ 正式環境 (Production)：{vm.CurrentEnvironmentDatabase}";

    var dialog = new ConfirmDialog(message, banner);
    await dialog.ShowDialog(this);
    return dialog.Result;
}
```

這是所有破壞性操作確認的唯一集中點（`MainWindowViewModel.ConfirmSaveCallback` → 各 Document ViewModel 的 `ConfirmExecuteCallback` / `ConfirmCallback`）。已接上集中確認路徑而自動受惠者：TableDetail、缺少/未使用/使用狀態索引報表、Recovery Model 變更等。

> **實作期間範圍修正（Schema Migration）**：原設計假設 Schema Migration 也已走集中確認路徑，但實際上 `SchemaMigrationDocumentViewModel.ExecuteMigrationAsync`（套用真實 DDL）原本**完全沒有任何執行前確認**。本次已為其新增 `ConfirmExecuteCallback`，並在 `MainWindowViewModel.OpenSchemaMigration` 接上 `ConfirmSaveCallback`，使 Migration 執行前會確認、且 Production 連線時顯示警告橫幅。確認文案會標示目標資料庫名稱與「無法自動還原」。

### 4. 自動流通、v1 不額外曝露

`Environment` 為一般屬性，下列路徑自動序列化／反序列化，無需改動，外部建立的連線預設 Staging：

- 匯出／匯入（`ConnectionExportData`）
- CLI 連線解析（`ConnectionProfileParser`）
- MCP 建立連線（`ConnectionCrudTools`）

v1 不在 CLI／MCP／匯入介面額外提供環境選擇（YAGNI），未來需要再加。

### 4.1 環境選單繁中顯示（實作期間新增）

環境下拉選單以繁體中文顯示（符合 UI 文字繁中規範）：新增 `DatabaseEnvironmentDisplayConverter`（`Specurai.Desktop/Converters/`，enum → 開發/測試/預備/正式環境），於 `App.axaml` 註冊，`ConnectionSetupWindow.axaml` 的 ComboBox 以 `ItemTemplate` 套用。`SelectedItem` 仍綁定 `DatabaseEnvironment` 列舉值，僅顯示文字本地化。

### 5. 共用設定（moldplan-change-database）

**無需任何程式碼變更**，已確認向前相容。未來若要在 MoldplanDbSwitcher 顯示環境，屬獨立增強，out of scope。

## 測試

- **Domain**：`ConnectionProfile` 預設 `Environment == Staging`。
- **Infrastructure**：
  - `ConnectionManager` 序列化往返保留 `Environment`。
  - 載入無 `Environment` 欄位的舊 JSON → `Staging`。
- **Desktop**：
  - `ConnectionSetupViewModel`：`EnvironmentOptions` 含四個值；`OnSelectedProfileChanged` 載入 `Environment`；`CreateProfileFromForm` 寫回；`ClearForm` 重置為 Staging。
  - `MainWindowViewModel`：`IsCurrentProfileProduction` 在當前連線為 Production／非 Production／null 三種情況正確回傳。
  - `SchemaMigrationDocumentViewModel`：`ConfirmExecuteCallback` 回傳 false 時不執行 Migration、回傳 true 時執行。
  - `DatabaseEnvironmentDisplayConverter`：四個列舉值對應繁中、非列舉值回傳原值。

## 不在範圍內（Out of Scope）

- MoldplanDbSwitcher 端顯示或使用環境。
- CLI／MCP／匯入介面的環境選擇。
- 依名稱自動推斷環境（例如 `-Staging` 後綴）。
- Production 以外環境的防呆。
