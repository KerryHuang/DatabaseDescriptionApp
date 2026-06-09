# 連線選擇器統一顯示與排序 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓全應用程式所有連線選擇器以統一格式 `【環境簡稱】名稱 (預設)` 顯示，並依 預設→環境→名稱 排序。

**Architecture:** 兩個共用元件達成 DRY —— Domain 的 `ConnectionProfileComparer`（集中於 `ConnectionManager.GetAllProfiles`，8/9 集合自動套用排序），與 Desktop 的 `ConnectionProfileDisplayConverter`（各選擇器 ItemTemplate 套用）。

**Tech Stack:** .NET 8、Avalonia 11、CommunityToolkit.Mvvm、xUnit + NSubstitute + FluentAssertions。

---

## 檔案結構

| 檔案 | 責任 | 動作 |
|------|------|------|
| `src/Specurai.Domain/Entities/ConnectionProfileComparer.cs` | 顯示排序比較器 | Create |
| `tests/Specurai.Domain.Tests/Entities/ConnectionProfileComparerTests.cs` | 比較器測試 | Create |
| `src/Specurai.Infrastructure/Services/ConnectionManager.cs` | `GetAllProfiles` 改用比較器 | Modify |
| `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs` | 排序測試＋更新既有測試 | Modify |
| `src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs` | 顯示字串轉換器 | Create |
| `tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs` | 轉換器測試 | Create |
| `src/Specurai.Desktop/App.axaml` | 註冊轉換器 | Modify |
| `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml` | 2 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/MainWindow.axaml` | 1 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml` | 2 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml` | 2 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/UsageAnalysisDocumentView.axaml` | 2 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml` | 2 個選擇器套用 | Modify |
| `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml` | 1 個選擇器套用 | Modify |

---

## Task 1: ConnectionProfileComparer（Domain）

**Files:**
- Create: `src/Specurai.Domain/Entities/ConnectionProfileComparer.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/ConnectionProfileComparerTests.cs`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Domain.Tests/Entities/ConnectionProfileComparerTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

/// <summary>
/// ConnectionProfileComparer 排序測試（預設→環境→名稱）
/// </summary>
public class ConnectionProfileComparerTests
{
    private static ConnectionProfile P(string name, DatabaseEnvironment env, bool isDefault = false) =>
        new() { Name = name, Server = "s", Database = "d", Environment = env, IsDefault = isDefault };

    [Fact]
    public void 預設連線_應排在非預設之前()
    {
        var list = new List<ConnectionProfile>
        {
            P("Zzz", DatabaseEnvironment.Development),
            P("Aaa", DatabaseEnvironment.Production, isDefault: true)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list[0].Name.Should().Be("Aaa"); // 預設優先，即使環境較後、名稱較後
    }

    [Fact]
    public void 同為非預設_應依環境列舉順序排序()
    {
        var list = new List<ConnectionProfile>
        {
            P("a", DatabaseEnvironment.Production),
            P("b", DatabaseEnvironment.Development),
            P("c", DatabaseEnvironment.Staging),
            P("d", DatabaseEnvironment.Testing)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list.Select(p => p.Environment).Should().ContainInOrder(
            DatabaseEnvironment.Development,
            DatabaseEnvironment.Testing,
            DatabaseEnvironment.Staging,
            DatabaseEnvironment.Production);
    }

    [Fact]
    public void 同環境同預設狀態_應依名稱不分大小寫排序()
    {
        var list = new List<ConnectionProfile>
        {
            P("banana", DatabaseEnvironment.Staging),
            P("Apple", DatabaseEnvironment.Staging)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list[0].Name.Should().Be("Apple");
        list[1].Name.Should().Be("banana");
    }

    [Fact]
    public void Null_應排在最後()
    {
        var a = P("a", DatabaseEnvironment.Staging);

        ConnectionProfileComparer.Instance.Compare(a, null).Should().BeNegative();
        ConnectionProfileComparer.Instance.Compare(null, a).Should().BePositive();
        ConnectionProfileComparer.Instance.Compare(null, null).Should().Be(0);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileComparerTests"`
Expected: 編譯失敗（`ConnectionProfileComparer` 尚未存在）。

- [ ] **Step 3: 建立比較器**

建立 `src/Specurai.Domain/Entities/ConnectionProfileComparer.cs`：

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 連線設定檔顯示排序：預設連線優先 → 環境（列舉順序）→ 名稱（不分大小寫）。
/// </summary>
public sealed class ConnectionProfileComparer : IComparer<ConnectionProfile>
{
    /// <summary>共用單例。</summary>
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

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileComparerTests"`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Domain/Entities/ConnectionProfileComparer.cs tests/Specurai.Domain.Tests/Entities/ConnectionProfileComparerTests.cs
git commit -m "feat(domain): 新增連線設定檔顯示排序比較器"
```
（commit 訊息結尾加上：
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>）

---

## Task 2: ConnectionManager 套用排序

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/ConnectionManager.cs:26-27`
- Test: `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs`

**背景**：`GetAllProfiles()` 目前為 `_temporaryProfiles.Concat(_profiles).OrderBy(p => p.Name).ToList().AsReadOnly();`。`ConnectionManager` 無參數建構式會讀取真實 `connections.json`，故測試以 `RegisterTemporaryProfiles` 注入、並用唯一名稱前綴過濾出自己的資料來驗證排序，避免受磁碟既有連線干擾。

- [ ] **Step 1: 寫失敗測試（新排序）**

在 `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs` 類別內加入（檔頭已有 `using Specurai.Domain.Entities;`、`using Specurai.Infrastructure.Services;`、`using FluentAssertions;`）：

```csharp
[Fact(DisplayName = "GetAllProfiles: 應依 預設→環境→名稱 排序")]
public void GetAllProfiles_ShouldSortByDefaultThenEnvThenName()
{
    var prefix = $"排序測試-{Guid.NewGuid():N}-";
    _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
    {
        new() { Name = prefix + "prod-zzz", Server = "s", Database = "d", Environment = DatabaseEnvironment.Production },
        new() { Name = prefix + "dev-bbb",  Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
        new() { Name = prefix + "dev-aaa",  Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
        new() { Name = prefix + "the-default", Server = "s", Database = "d", Environment = DatabaseEnvironment.Production, IsDefault = true },
    });

    var mine = _manager.GetAllProfiles().Where(p => p.Name.StartsWith(prefix)).ToList();

    mine.Select(p => p.Name).Should().Equal(
        prefix + "the-default", // 預設優先（即使環境 Production、名稱靠後）
        prefix + "dev-aaa",     // 環境 Development，名稱 aaa
        prefix + "dev-bbb",     // 環境 Development，名稱 bbb
        prefix + "prod-zzz");   // 環境 Production
}
```

- [ ] **Step 2: 更新既有的脆弱測試**

既有測試 `RegisterTemporaryProfiles_ShouldPrioritizeOverPersistent`（DisplayName「temporary profiles should come before persistent ones」）假設「臨時連線排最前（依名稱）」，此語意已被新排序取代。將該測試整段**替換**為下列驗證「預設優先」的確定性測試：

```csharp
[Fact(DisplayName = "RegisterTemporaryProfiles: 預設連線應排在非預設之前")]
public void RegisterTemporaryProfiles_DefaultShouldComeFirst()
{
    var prefix = $"預設優先-{Guid.NewGuid():N}-";
    _manager.RegisterTemporaryProfiles(new List<ConnectionProfile>
    {
        new() { Name = prefix + "zzz-非預設", Server = "s", Database = "d", Environment = DatabaseEnvironment.Development },
        new() { Name = prefix + "aaa-預設",   Server = "s", Database = "d", Environment = DatabaseEnvironment.Production, IsDefault = true },
    });

    var mine = _manager.GetAllProfiles().Where(p => p.Name.StartsWith(prefix)).ToList();

    mine[0].Name.Should().Be(prefix + "aaa-預設", "預設連線應排最前，與環境/名稱無關");
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTemporaryProfileTests"`
Expected: 新測試 `GetAllProfiles_ShouldSortByDefaultThenEnvThenName` 與更新後的 `RegisterTemporaryProfiles_DefaultShouldComeFirst` 失敗（目前仍 OrderBy Name）。

- [ ] **Step 4: 改用比較器**

在 `src/Specurai.Infrastructure/Services/ConnectionManager.cs`，將 `GetAllProfiles` 方法（第 26-27 行）：

```csharp
    public IReadOnlyList<ConnectionProfile> GetAllProfiles()
        => _temporaryProfiles.Concat(_profiles).OrderBy(p => p.Name).ToList().AsReadOnly();
```

改為：

```csharp
    public IReadOnlyList<ConnectionProfile> GetAllProfiles()
        => _temporaryProfiles.Concat(_profiles)
            .OrderBy(p => p, ConnectionProfileComparer.Instance)
            .ToList().AsReadOnly();
```

（`ConnectionProfileComparer` 位於 `Specurai.Domain.Entities`，該檔已有 `using Specurai.Domain.Entities;`。）

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTemporaryProfileTests"`
Expected: PASS（含新測試與更新後的測試；其餘臨時連線測試不受影響）。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Infrastructure/Services/ConnectionManager.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs
git commit -m "feat(infrastructure): GetAllProfiles 改用統一排序比較器"
```
（commit 訊息結尾加上 Co-Authored-By 行，同上。）

---

## Task 3: ConnectionProfileDisplayConverter（Desktop）

**Files:**
- Create: `src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs`
- Modify: `src/Specurai.Desktop/App.axaml:9`
- Test: `tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs`：

```csharp
using System.Globalization;
using FluentAssertions;
using Specurai.Desktop.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.Converters;

public class ConnectionProfileDisplayConverterTests
{
    private readonly ConnectionProfileDisplayConverter _converter = new();

    private static ConnectionProfile P(string name, DatabaseEnvironment env, bool isDefault = false) =>
        new() { Name = name, Server = "s", Database = "d", Environment = env, IsDefault = isDefault };

    [Theory]
    [InlineData(DatabaseEnvironment.Development, "【開發】Dev-Local")]
    [InlineData(DatabaseEnvironment.Testing, "【測試】Dev-Local")]
    [InlineData(DatabaseEnvironment.Staging, "【預備】Dev-Local")]
    [InlineData(DatabaseEnvironment.Production, "【正式】Dev-Local")]
    public void Convert_非預設_應為環境標籤加名稱(DatabaseEnvironment env, string expected)
    {
        var result = _converter.Convert(P("Dev-Local", env), typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_預設連線_應附加預設標記()
    {
        var result = _converter.Convert(
            P("MoldPlan-Schema", DatabaseEnvironment.Production, isDefault: true),
            typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("【正式】MoldPlan-Schema (預設)");
    }

    [Fact]
    public void Convert_非ConnectionProfile_應回傳原值字串()
    {
        var result = _converter.Convert("其他", typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("其他");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileDisplayConverterTests"`
Expected: 編譯失敗（轉換器尚未存在）。

- [ ] **Step 3: 建立轉換器**

建立 `src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs`：

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

- [ ] **Step 4: 在 App.axaml 註冊轉換器**

在 `src/Specurai.Desktop/App.axaml` 的 `<Application.Resources>` 內，`TestResultColorConverter` 與 `DatabaseEnvironmentDisplayConverter` 之後加入一行：

```xml
        <converters:ConnectionProfileDisplayConverter x:Key="ConnectionProfileDisplayConverter"/>
```

（`xmlns:converters="using:Specurai.Desktop.Converters"` 已存在於 App.axaml。）

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionProfileDisplayConverterTests"`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs src/Specurai.Desktop/App.axaml tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs
git commit -m "feat(desktop): 新增連線選擇器顯示轉換器"
```
（commit 訊息結尾加上 Co-Authored-By 行。）

---

## Task 4: 各選擇器套用顯示轉換器（AXAML）

**Files:** 7 個 View（見下）。此為 UI 變更，無單元測試；以建置與後續手動驗證確認。所有 `SelectedItem`/`IsChecked` 綁定維持不變，僅改顯示文字。轉換器以 `{StaticResource ConnectionProfileDisplayConverter}` 引用（App 資源，無需各檔加 xmlns）。

- [ ] **Step 1: ConnectionSetupWindow.axaml — Profiles ListBox**

將（第 39-47 行）：

```xml
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Orientation="Horizontal" Spacing="5">
                                            <TextBlock Text="{Binding Name}"/>
                                            <TextBlock Text="(預設)" Foreground="Gray"
                                                       IsVisible="{Binding IsDefault}"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
```

改為（轉換器已含 (預設) 標記，移除原本獨立的 (預設) TextBlock）：

```xml
                                <ListBox.ItemTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
```

- [ ] **Step 2: ConnectionSetupWindow.axaml — ExternalProfiles ListBox**

將（第 55-59 行）：

```xml
                                    <ListBox.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Name}"/>
                                        </DataTemplate>
                                    </ListBox.ItemTemplate>
```

改為：

```xml
                                    <ListBox.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                        </DataTemplate>
                                    </ListBox.ItemTemplate>
```

- [ ] **Step 3: MainWindow.axaml — 連線 ComboBox**

將（第 280-284 行）的 ItemTemplate 內 TextBlock：

```xml
                                    <DataTemplate x:DataType="domain:ConnectionProfile">
                                        <TextBlock Text="{Binding Name}"/>
                                    </DataTemplate>
```

改為：

```xml
                                    <DataTemplate x:DataType="domain:ConnectionProfile">
                                        <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                    </DataTemplate>
```

- [ ] **Step 4: SchemaMigrationDocumentView.axaml — 加入 domain 命名空間並改兩個 ComboBox**

(a) 在根元素的 xmlns 區塊（第 3 行 `xmlns:vm=...` 附近）加入：

```xml
             xmlns:domain="using:Specurai.Domain.Entities"
```

(b) 基準 ComboBox（第 24-27 行）：移除 `DisplayMemberBinding="{Binding Name}"`，改用 ItemTemplate：

```xml
                    <ComboBox ItemsSource="{Binding ConnectionProfiles}"
                              SelectedItem="{Binding SelectedBaseProfile}"
                              MinWidth="180">
                        <ComboBox.ItemTemplate>
                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
```

(c) 目標 ComboBox（第 32-35 行）：

```xml
                    <ComboBox ItemsSource="{Binding ConnectionProfiles}"
                              SelectedItem="{Binding SelectedTargetProfile}"
                              MinWidth="180">
                        <ComboBox.ItemTemplate>
                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
```

- [ ] **Step 5: SchemaCompareDocumentView.axaml — AvailableProfiles ComboBox**

將（第 84-91 行）：

```xml
                            <ComboBox.ItemTemplate>
                                <DataTemplate x:DataType="domain:ConnectionProfile">
                                    <StackPanel>
                                        <TextBlock Text="{Binding Name}" FontWeight="SemiBold"/>
                                        <TextBlock Text="{Binding Database}" FontSize="11" Foreground="Gray"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
```

改為（統一顯示，移除資料庫灰字）：

```xml
                            <ComboBox.ItemTemplate>
                                <DataTemplate x:DataType="domain:ConnectionProfile">
                                    <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
```

- [ ] **Step 6: SchemaCompareDocumentView.axaml — TargetProfileItems**

將（第 121-128 行）CheckBox 內容：

```xml
                                            <StackPanel Orientation="Vertical" Spacing="2">
                                                <TextBlock Text="{Binding Profile.Name}" FontWeight="SemiBold"/>
                                                <StackPanel Orientation="Horizontal" Spacing="5">
                                                    <TextBlock Text="{Binding Profile.Server}" FontSize="11" Foreground="Gray"/>
                                                    <TextBlock Text="(基準環境)" FontSize="11" Foreground="Orange"
                                                               IsVisible="{Binding !IsEnabled}"/>
                                                </StackPanel>
                                            </StackPanel>
```

改為（統一顯示，保留「(基準環境)」狀態指示）：

```xml
                                            <StackPanel Orientation="Horizontal" Spacing="5">
                                                <TextBlock Text="{Binding Profile, Converter={StaticResource ConnectionProfileDisplayConverter}}" FontWeight="SemiBold"/>
                                                <TextBlock Text="(基準環境)" FontSize="11" Foreground="Orange"
                                                           IsVisible="{Binding !IsEnabled}"/>
                                            </StackPanel>
```

- [ ] **Step 7: UsageAnalysisDocumentView.axaml — AvailableProfiles ComboBox**

將（第 59-61 行）：

```xml
                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                <TextBlock Text="{Binding Name}"/>
                            </DataTemplate>
```

改為：

```xml
                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                            </DataTemplate>
```

- [ ] **Step 8: UsageAnalysisDocumentView.axaml — TargetProfileItems CheckBox**

將（第 74 行）：

```xml
                                <CheckBox Content="{Binding Profile.Name}"
                                          IsChecked="{Binding IsSelected}"
                                          Margin="0,0,12,0"/>
```

改為：

```xml
                                <CheckBox Content="{Binding Profile, Converter={StaticResource ConnectionProfileDisplayConverter}}"
                                          IsChecked="{Binding IsSelected}"
                                          Margin="0,0,12,0"/>
```

- [ ] **Step 9: BackupRestoreDocumentView.axaml — 兩個 ComboBox**

(a) 連線 ComboBox（第 67-71 行）內 TextBlock：

```xml
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                                <TextBlock Text="{Binding Name}"/>
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
```

改為：

```xml
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                                <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
```

(b) 目標連線 ComboBox（第 202-206 行）內 TextBlock — 同樣改法：

```xml
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate x:DataType="domain:ConnectionProfile">
                                                <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
```

- [ ] **Step 10: SqlQueryDocumentView.axaml — 連線 ComboBox**

將（第 27-29 行）：

```xml
                        <DataTemplate x:DataType="domain:ConnectionProfile">
                            <TextBlock Text="{Binding Name}"/>
                        </DataTemplate>
```

改為：

```xml
                        <DataTemplate x:DataType="domain:ConnectionProfile">
                            <TextBlock Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                        </DataTemplate>
```

- [ ] **Step 11: 建置確認**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded（可能有既有 AVLN3001 警告，與本任務無關）。若桌面程式佔用 DLL 無法輸出則先關閉再建置。

- [ ] **Step 12: Commit**

```bash
git add src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml src/Specurai.Desktop/Views/MainWindow.axaml src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml src/Specurai.Desktop/Views/SchemaCompareDocumentView.axaml src/Specurai.Desktop/Views/UsageAnalysisDocumentView.axaml src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml
git commit -m "feat(desktop): 所有連線選擇器套用統一顯示格式"
```
（commit 訊息結尾加上 Co-Authored-By 行。）

---

## Task 5: 整體驗證與程式碼審查

**Files:** 無（驗證任務）

- [ ] **Step 1: 完整建置**

Run: `dotnet build`
Expected: Build succeeded，無新增警告。

- [ ] **Step 2: 完整測試**

Run: `dotnet test`
Expected: 全部通過（含新增 Comparer、Converter、ConnectionManager 排序測試）。

- [ ] **Step 3: 手動驗證 UI**

執行桌面程式（`dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`），確認：
- 主視窗連線下拉、連線設定清單、Schema Migration / Schema Compare / Usage Analysis / Backup-Restore / SQL Query 各選擇器，皆顯示 `【環境簡稱】名稱`，預設連線顯示 ` (預設)`。
- 同環境的連線在清單中相鄰聚集；預設連線排最上。
- 各選擇器選取後，功能（連線、分析、備份等）行為正常。

- [ ] **Step 4: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本次所有變更（依 CLAUDE.md <law> 要求），再回報完成。
