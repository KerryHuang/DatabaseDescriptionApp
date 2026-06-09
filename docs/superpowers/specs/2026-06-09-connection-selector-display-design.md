# 連線選擇器統一顯示與排序 — 設計規格

- 日期：2026-06-09
- 狀態：已核准，待實作
- 關聯：延續 [連線設定環境欄位](2026-06-09-connection-environment-field-design.md)

## 目標

讓應用程式中**所有**連線設定檔選擇器以一致方式顯示與排序：

1. **顯示**：每一項顯示「環境 + 連線名稱 + 預設標記」，格式為 `【{環境簡稱}】{連線名稱}`，預設連線額外加 ` (預設)`。
   - 範例：`【正式】MoldPlan-Schema (預設)`、`【預備】Ginlee-Staging`、`【測試】Fupite-Test`、`【開發】Dev-Local`。
   - 環境簡稱：Development→開發、Testing→測試、Staging→預備、Production→正式。
2. **排序**：預設連線優先 → 環境（列舉順序：開發→測試→預備→正式）→ 連線名稱。
3. **分格**：僅靠排序讓同環境自然聚集，不加環境標題列（Avalonia ComboBox 真分組過於棘手，已評估排除）。

## 背景

全應用程式共有 9 個連線設定檔集合，分散於 7 個 View：

| # | View | ViewModel 集合 | 型別 |
|---|------|----------------|------|
| 1 | ConnectionSetupWindow | `Profiles` | `ObservableCollection<ConnectionProfile>` |
| 2 | ConnectionSetupWindow | `ExternalProfiles` | `ObservableCollection<ConnectionProfile>` |
| 3 | MainWindow | `FilteredConnectionProfiles` | `ObservableCollection<ConnectionProfile>` |
| 4 | SchemaMigrationDocumentView | `ConnectionProfiles`（基準/目標共用） | `ObservableCollection<ConnectionProfile>` |
| 5 | SchemaCompareDocumentView | `AvailableProfiles` | `ObservableCollection<ConnectionProfile>` |
| 6 | SchemaCompareDocumentView | `TargetProfileItems` | `ObservableCollection<ProfileItemViewModel>` |
| 7 | UsageAnalysisDocumentView | `AvailableProfiles` | `ObservableCollection<ConnectionProfile>` |
| 8 | UsageAnalysisDocumentView | `TargetProfileItems` | `ObservableCollection<SelectableProfile>` |
| 9 | BackupRestore / SqlQuery | `ConnectionProfiles` | `ObservableCollection<ConnectionProfile>` |

**關鍵觀察**：所有集合都源自 `ConnectionManager.GetAllProfiles()`（目前 `_temporaryProfiles.Concat(_profiles).OrderBy(p => p.Name)`），且各衍生集合（MainWindow 篩選、UsageAnalysis 排除基準、勾選包裝）都**保留來源順序**。因此集中改一處排序即可全面套用。

包裝型別：
- `ProfileItemViewModel`（SchemaCompareDocumentViewModel）：含 `ConnectionProfile Profile`、`bool IsSelected`。
- `SelectableProfile`（UsageAnalysisDocumentViewModel）：含 `ConnectionProfile Profile`、`bool IsSelected`。

## 設計

採兩個共用元件，達成 DRY：

### 1. 排序：共用 `ConnectionProfileComparer`

新增 `src/Specurai.Domain/Entities/ConnectionProfileComparer.cs`（Domain 純 C#，無相依）：

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 連線設定檔顯示排序：預設連線優先 → 環境（列舉順序）→ 名稱。
/// </summary>
public sealed class ConnectionProfileComparer : IComparer<ConnectionProfile>
{
    public static readonly ConnectionProfileComparer Instance = new();

    public int Compare(ConnectionProfile? x, ConnectionProfile? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 預設連線優先（IsDefault = true 排前面）
        var byDefault = y.IsDefault.CompareTo(x.IsDefault);
        if (byDefault != 0) return byDefault;

        // 環境（列舉順序：Development=0 → Production=3）
        var byEnv = x.Environment.CompareTo(y.Environment);
        if (byEnv != 0) return byEnv;

        // 名稱（不分大小寫）
        return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
    }
}
```

`ConnectionManager.GetAllProfiles()` 改用此 comparer：

```csharp
public IReadOnlyList<ConnectionProfile> GetAllProfiles()
    => _temporaryProfiles.Concat(_profiles)
        .OrderBy(p => p, ConnectionProfileComparer.Instance)
        .ToList().AsReadOnly();
```

9 個集合中有 8 個源自 `GetAllProfiles()` 並保留來源順序，因而**自動套用新排序，無需逐一修改載入點**。唯一例外是 ConnectionSetupWindow 的 `ExternalProfiles`（#2），它來自外部同步結果而非 `GetAllProfiles()`，故僅套用顯示轉換器、排序維持同步載入順序（見「不在範圍內」）。

> **影響**：現有 `ConnectionManagerTemporaryProfileTests` 中假設「臨時連線排最前」的測試（`RegisterTemporaryProfiles_ShouldPrioritizeOverPersistent`）會因排序改變而需更新為新順序。臨時連線「排最前」非產品需求，併入統一排序。其餘臨時連線測試（出現於清單、不落地、取代前次、空清單）不受排序影響。

### 2. 顯示：共用 `ConnectionProfileDisplayConverter`

新增 `src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs`（實作 `Avalonia.Data.Converters.IValueConverter`）：

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Converters;

/// <summary>
/// 將 <see cref="ConnectionProfile"/> 轉為選擇器顯示字串：【環境簡稱】名稱 (預設)。
/// </summary>
public class ConnectionProfileDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionProfile p)
            return value?.ToString();

        var tag = p.Environment switch
        {
            DatabaseEnvironment.Development => "開發",
            DatabaseEnvironment.Testing => "測試",
            DatabaseEnvironment.Staging => "預備",
            DatabaseEnvironment.Production => "正式",
            _ => p.Environment.ToString()
        };

        return p.IsDefault ? $"【{tag}】{p.Name} (預設)" : $"【{tag}】{p.Name}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

於 `src/Specurai.Desktop/App.axaml` 的 `<Application.Resources>` 註冊：

```xml
<converters:ConnectionProfileDisplayConverter x:Key="ConnectionProfileDisplayConverter"/>
```

### 3. 各選擇器套用顯示轉換器

逐一將 9 個選擇器的顯示改用轉換器：

- **一般 `ConnectionProfile` 集合**（#1-#5、#7、#9）：`ItemTemplate` 內 `<TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>`。
  - SchemaMigration 兩個 ComboBox（基準/目標）若使用 `ItemTemplate` 則套用之；維持 SelectedItem 綁定不變。
- **包裝型別集合**（#6 `ProfileItemViewModel`、#8 `SelectableProfile`）：標籤改為 `{Binding Profile, Converter={StaticResource ConnectionProfileDisplayConverter}}`，CheckBox 的 `IsSelected` 綁定不變。
- **SchemaCompare `AvailableProfiles`**：原為「名稱粗體＋資料庫灰字」自訂模板，**改為統一的轉換器顯示**（移除資料庫灰字），以符合全應用程式一致。

所有 `SelectedItem` / `IsSelected` 綁定維持不變 —— 僅改顯示文字，綁定的仍是 `ConnectionProfile`／包裝物件本身。

## 測試

- **Domain（`ConnectionProfileComparerTests`）**：
  - 預設連線排在非預設之前（即使環境/名稱較後）。
  - 同為非預設時，依環境列舉順序（開發→測試→預備→正式）。
  - 同環境同預設狀態時，依名稱不分大小寫排序。
  - null 處理（null 排最後）。
- **Desktop（`ConnectionProfileDisplayConverterTests`）**：
  - 四種環境 × 預設/非預設的輸出字串正確（如 `【正式】MoldPlan-Schema (預設)`、`【預備】Ginlee-Staging`）。
  - 非 `ConnectionProfile` 輸入回傳原值字串。
- **Infrastructure（`ConnectionManager`）**：
  - 以 `RegisterTemporaryProfiles` 注入多筆不同 預設/環境/名稱 的連線，驗證 `GetAllProfiles()` 順序符合 預設→環境→名稱。
  - 更新 `RegisterTemporaryProfiles_ShouldPrioritizeOverPersistent` 為反映新排序的斷言。

## 不在範圍內（Out of Scope）

- 環境標題列分格、環境色彩指示（已選「僅排序聚集」）。
- CLI／MCP 的顯示格式（`GetAllProfiles` 排序改變會連帶影響其回傳順序，屬一致的副作用，不另做顯示格式）。
- ConnectionSetupWindow 的 `ExternalProfiles` 排序：此清單來自同步結果，亦套用顯示轉換器；排序依其載入順序（非 `GetAllProfiles`），如需排序可後續再加。
