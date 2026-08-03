# 外部連線與內部連線去重設計

日期：2026-08-03

## 目標

同步外部來源時，若某筆外部連線與既有的內部連線指向同一個資料庫、且用同一組身分連線，就不要重複列在「外部連線」清單中。

外部來源目前一次同步出 35 筆，其中相當比例是使用者早已手動建好的連線。兩份清單並列會讓人分不清該點哪一個，也讓外部清單的實際價值（找出還沒建的連線）被稀釋。

## 決策

**過濾放在 ViewModel，比對規則放在 Domain entity。**

`ConnectionSetupViewModel.SyncExternalSourceAsync()` 是唯一同時持有本地 `Profiles` 與外部 `result.Profiles` 的地方，過濾在此進行。比對規則本身是 `ConnectionProfile` 自己的知識，放進 entity 的方法，純邏輯、可獨立測試。

考慮過但不採用的替代方案：

- 在 `InventoryConnectionSource` 過濾。要把本地連線清單傳進 Infrastructure，讓外部來源解析器反過來依賴應用程式狀態，職責倒置。
- 過濾邏輯直接寫在 ViewModel 的 LINQ 述詞裡。規則有五個欄位、兩種大小寫語意、一個 Windows 驗證特例，埋在 UI 層難測也難改。

## 1. Domain：比對方法

`ConnectionProfile` 新增：

```csharp
/// <summary>
/// 判斷兩筆連線是否指向同一個資料庫且使用同一組身分。
/// 用於外部來源同步時排除與既有連線重複的項目。
/// </summary>
public bool HasSameConnectionSettings(ConnectionProfile other)
```

比對欄位與語意：

| 欄位 | 比對方式 |
|------|----------|
| `Server` | `OrdinalIgnoreCase` |
| `Database` | `OrdinalIgnoreCase` |
| `AuthType` | 列舉相等 |
| `Username` | `OrdinalIgnoreCase`，`null` 與空字串等價 |

**不比對密碼**。密碼相同與否不改變「是不是同一個連線」；外部來源的密碼由 vault 解出，與使用者手動輸入的那筆本來就可能有落差（例如手動那筆存的是舊密碼），納入比對會讓去重形同失效。

**不比對** `Name`、`Environment`、`IsDefault`、`IsEnabled`、`Id`。名稱不同但連的是同一個庫，仍屬重複。

Windows 驗證的連線 `Username` 為 `null`，因此 `null` 與空字串必須等價，否則兩筆同為 Windows 驗證的連線會因一個存 `null`、一個存 `""` 而被判為不同。

## 2. Desktop：同步流程過濾

`SyncExternalSourceAsync()` 取得結果後：

```csharp
var deduped = result.Profiles
    .Where(e => !Profiles.Any(e.HasSameConnectionSettings))
    .ToList();
```

`Profiles` 是本地連線的完整清單，**包含已停用者**。停用只表示使用者當下不想用它，該連線設定依然存在，外部清單再列一次沒有意義；需要時把內部那筆重新啟用即可。

## 3. 狀態訊息

過濾後數量會明顯少於實際同步到的筆數，訊息必須說明差額，否則使用者會以為同步又壞了。

- 無重複、無失敗：`已同步 N 個外部連線`
- 有重複：`已同步 N 個外部連線（M 個與現有連線重複已略過）`
- 有失敗：既有的 `已同步 N 個，M 個失敗` 格式不變

重複數與失敗數是兩件獨立的事，訊息各自附加。

## 4. 測試

**Domain**（`ConnectionProfileTests`）：

- 全部欄位相同 → `true`
- `Server`／`Database`／`Username` 大小寫不同 → `true`
- `Server`／`Database`／`Username`／`AuthType` 任一不同 → `false`
- 密碼不同但其餘相同 → `true`（釘住「不比密碼」的決策）
- `Name`／`Environment`／`IsEnabled` 不同但其餘相同 → `true`
- Windows 驗證，一邊 `Username` 為 `null`、另一邊為空字串 → `true`

**Desktop**（`ConnectionSetupViewModelTests`）：

- 同步結果中與本地重複者不進 `ExternalProfiles`，其餘保留
- 與已停用的本地連線重複者同樣被排除
- 有重複時狀態訊息含略過筆數

## 風險

外部來源的伺服器欄位可能是 IP，而使用者手動建的那筆用主機名稱或 FQDN 指向同一台機器。這種情況判為不重複，兩筆都會顯示。字串比對無法解決，需要 DNS 解析才能判定，成本與誤判風險都不划算，接受此限制。
