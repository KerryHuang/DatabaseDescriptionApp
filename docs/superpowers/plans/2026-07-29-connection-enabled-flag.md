# 連線設定「啟用」欄位 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 `ConnectionProfile` 具備「啟用」狀態，只有啟用的連線能在 Desktop／CLI／MCP 的功能面被選用。

**Architecture:** 過濾落在 `IConnectionManager` 層：`GetAllProfiles()` 語意不變（全量，供管理型入口使用），新增 `GetEnabledProfiles()` 供選用型入口使用。`SetCurrentProfile` / `GetConnectionString` / `GetCurrentProfile` 加上 fail-safe，即使呼叫點漏改，實際取用停用連線時仍會被擋。

**Tech Stack:** .NET 8、Avalonia 11（Semi.Avalonia）、CommunityToolkit.Mvvm、System.CommandLine、MCP SDK、xUnit + NSubstitute + FluentAssertions。

**Spec:** `docs/superpowers/specs/2026-07-29-connection-enabled-flag-design.md`

## Global Constraints

- 遵守 Clean Architecture 分層：Domain 不參考任何外部套件；Application 只相依 Domain。
- UI 文字、XML 註解、commit 訊息一律繁體中文。
- ViewModel 使用 CommunityToolkit.Mvvm 的 `[ObservableProperty]` / `[RelayCommand]`；每個 ViewModel 保留無參數的設計時建構函式。
- 檔案 UTF-8 無 BOM。
- 測試命名 `[方法]_[條件]_[預期]`，內容繁體中文。
- 屬性名稱固定為 `IsEnabled`；新方法名稱固定為 `GetEnabledProfiles`。
- 每個 Task 結束時 commit，只 `git add` 該 Task 動到的檔案（禁止 `git add -A` / `git add .`）。

## 兩處與 spec 的刻意偏離

1. **`ColumnSearchService` 不改用 `GetEnabledProfiles()`。** 該處 `GetAllProfiles()` 是用 profileId 反查資料庫名稱（顯示用），改掉會讓停用連線的名稱顯示不出來。跳過停用連線由 `GetConnectionString` 的 fail-safe（回 `null`）達成，外部行為與 spec 一致。
2. **停用衝突判「事後狀態」而非「狀態轉換」。** UI 勾選會就地修改同一個 `ConnectionProfile` 實例，`UpdateProfile` 無法比對舊值。改為存檔後只檢查「這個 profile 現在是停用的嗎」，是就清 `IsDefault` 並切離目前連線。

## File Structure

**Domain**
- 修改 `src/Specurai.Domain/Entities/ConnectionProfile.cs` — 新增 `IsEnabled` 屬性

**Application**
- 修改 `src/Specurai.Application/Services/IConnectionManager.cs` — 新增 `GetEnabledProfiles()`

**Infrastructure**
- 修改 `src/Specurai.Infrastructure/Services/ConnectionManager.cs` — `GetEnabledProfiles()`、三處 fail-safe、`UpdateProfile` 停用切離

**Desktop**
- 修改 7 個文件 ViewModel — 連線清單改用 `GetEnabledProfiles()`
- 修改 `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs` — 啟用欄位與切換命令
- 修改 `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml` — 清單勾選欄、灰階、表單 CheckBox
- 建立 `src/Specurai.Desktop/Converters/BoolToOpacityConverter.cs` — 停用列灰階
- 修改 `src/Specurai.Desktop/App.axaml` — 註冊上述 converter

**Cli**
- 修改 `src/Specurai.Cli/ConnectionResolver.cs` — `FromProfileName` 走啟用清單、新增 `DescribeMissing`
- 修改 6 個命令檔 — 選用型呼叫改用 `GetEnabledProfiles()`、錯誤訊息改用 `DescribeMissing`
- 修改 `src/Specurai.Cli/Commands/ConnCommand.cs` — `conn list` 顯示停用標記

**McpServer**
- 修改 `src/Specurai.McpServer/Tools/ProfileResolver.cs` — 走啟用清單、新增 `DescribeMissing`
- 修改 `ConnectionTools.cs`、`MigrationTools.cs` — 選用型呼叫分流、`list_connections` 輸出 `IsEnabled`

---

### Task 1: Domain 屬性與舊設定檔相容

**Files:**
- Modify: `src/Specurai.Domain/Entities/ConnectionProfile.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`

**Interfaces:**
- Consumes: 無
- Produces: `ConnectionProfile.IsEnabled`（`bool`，可讀寫，預設 `true`）

- [ ] **Step 1: 寫失敗測試 — 預設值**

加到 `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs` 類別內：

```csharp
[Fact]
public void IsEnabled_未指定時_預設為啟用()
{
    var profile = new ConnectionProfile
    {
        Name = "測試",
        Server = "localhost",
        Database = "TestDb"
    };

    profile.IsEnabled.Should().BeTrue();
}
```

- [ ] **Step 2: 寫失敗測試 — 舊設定檔相容**

加到 `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs` 類別內。這支測試釘住「舊 `connections.json` 沒有 `isEnabled` 欄位時全部維持啟用」，是本次最容易被將來的重構弄壞的行為：

```csharp
[Fact]
public void LoadProfiles_舊設定檔無IsEnabled欄位_全部視為啟用()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    var json = """
    {
      "Profiles": [
        {
          "Id": "11111111-1111-1111-1111-111111111111",
          "Name": "舊連線",
          "Server": "localhost",
          "Database": "OldDb",
          "AuthType": 0,
          "IsDefault": true,
          "Environment": 2
        }
      ],
      "CurrentProfileId": "11111111-1111-1111-1111-111111111111"
    }
    """;
    File.WriteAllText(configPath, json);

    try
    {
        var manager = new ConnectionManager(configPath);

        manager.GetAllProfiles().Should().ContainSingle()
            .Which.IsEnabled.Should().BeTrue();
    }
    finally
    {
        File.Delete(configPath);
    }
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test --filter "FullyQualifiedName~IsEnabled"`
Expected: FAIL，編譯錯誤 `ConnectionProfile' 未包含 'IsEnabled' 的定義`。

- [ ] **Step 4: 加上屬性**

在 `src/Specurai.Domain/Entities/ConnectionProfile.cs` 的 `Environment` 屬性之後加入：

```csharp
    /// <summary>
    /// 是否啟用（停用的連線不會出現在各功能的連線選擇中）
    /// </summary>
    public bool IsEnabled { get; set; } = true;
```

不要改成 `required`，也不要加 `[JsonPropertyName]`。預設值 `= true` 正是舊設定檔相容的機制：System.Text.Json 對 JSON 中不存在的屬性不會呼叫 setter，物件初始化值會保留。

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test --filter "FullyQualifiedName~IsEnabled"`
Expected: PASS，2 passed。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Entities/ConnectionProfile.cs tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs
git commit -m "feat: ConnectionProfile 新增 IsEnabled 欄位

預設啟用，舊設定檔無此欄位時維持啟用。"
```

---

### Task 2: `GetEnabledProfiles()` 與 Manager fail-safe

**Files:**
- Modify: `src/Specurai.Application/Services/IConnectionManager.cs`
- Modify: `src/Specurai.Infrastructure/Services/ConnectionManager.cs:35-52`（`GetAllProfiles` / `GetCurrentProfile`）、`:225-229`（`GetConnectionString`）、`:54-64`（`SetCurrentProfile`）
- Test: `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`

**Interfaces:**
- Consumes: `ConnectionProfile.IsEnabled`（Task 1）
- Produces: `IConnectionManager.GetEnabledProfiles()` → `IReadOnlyList<ConnectionProfile>`；`GetConnectionString(Guid)` 對停用 profile 回 `null`；`SetCurrentProfile(Guid)` 對停用 profile 不動作

- [ ] **Step 1: 寫失敗測試**

加到 `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`。若該測試類別已有建立暫存設定檔的 helper 就沿用；沒有的話這四支測試各自用下面的寫法：

```csharp
[Fact]
public void GetEnabledProfiles_有停用連線_只回傳啟用的()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        manager.AddProfile(new ConnectionProfile
        {
            Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
        });
        manager.AddProfile(new ConnectionProfile
        {
            Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false
        });

        var enabled = manager.GetEnabledProfiles();

        enabled.Should().ContainSingle().Which.Name.Should().Be("啟用的");
        manager.GetAllProfiles().Should().HaveCount(2);
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void GetConnectionString_連線已停用_回傳Null()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var profile = new ConnectionProfile
        {
            Name = "停用的", Server = "s1", Database = "db1", IsEnabled = false
        };
        manager.AddProfile(profile);

        manager.GetConnectionString(profile.Id).Should().BeNull();
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void SetCurrentProfile_目標已停用_不切換()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var enabled = new ConnectionProfile
        {
            Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
        };
        var disabled = new ConnectionProfile
        {
            Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false
        };
        manager.AddProfile(enabled);
        manager.AddProfile(disabled);
        manager.SetCurrentProfile(enabled.Id);

        manager.SetCurrentProfile(disabled.Id);

        manager.GetCurrentProfile()!.Id.Should().Be(enabled.Id);
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void GetConnectionString_臨時連線_不受停用邏輯影響()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var temp = new ConnectionProfile
        {
            Name = "臨時", Server = "s1", Database = "db1"
        };
        manager.RegisterTemporaryProfiles([temp]);

        manager.GetConnectionString(temp.Id).Should().NotBeNull();
    }
    finally
    {
        File.Delete(configPath);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTests"`
Expected: FAIL，編譯錯誤 `'ConnectionManager' 未包含 'GetEnabledProfiles' 的定義`。

- [ ] **Step 3: 介面加方法**

在 `src/Specurai.Application/Services/IConnectionManager.cs` 的 `GetAllProfiles()` 之後加入：

```csharp
    /// <summary>
    /// 取得所有已啟用的連線設定（供功能面的連線選擇使用）
    /// </summary>
    IReadOnlyList<ConnectionProfile> GetEnabledProfiles();
```

- [ ] **Step 4: 實作與 fail-safe**

在 `src/Specurai.Infrastructure/Services/ConnectionManager.cs`：

`GetAllProfiles()` 之後加入實作：

```csharp
    public IReadOnlyList<ConnectionProfile> GetEnabledProfiles()
        => GetAllProfiles().Where(p => p.IsEnabled).ToList().AsReadOnly();
```

`GetCurrentProfile()` 中挑預設連線那段改為只挑啟用的：

```csharp
            var defaultProfile = _profiles.FirstOrDefault(p => p.IsDefault && p.IsEnabled);
```

`SetCurrentProfile(Guid)` 的查找條件加上啟用判斷：

```csharp
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId && p.IsEnabled);
```

`GetConnectionString(Guid)` 改為（臨時 profile 一律視為啟用，故先查臨時清單）：

```csharp
    public string? GetConnectionString(Guid profileId)
    {
        var temporary = _temporaryProfiles.FirstOrDefault(p => p.Id == profileId);
        if (temporary != null)
            return BuildConnectionString(temporary);

        var profile = _profiles.FirstOrDefault(p => p.Id == profileId && p.IsEnabled);
        return profile != null ? BuildConnectionString(profile) : null;
    }
```

`GetProfileName(Guid)` **不要改** —— 停用連線的名稱仍需查得到，供訊息與顯示使用。

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTests"`
Expected: PASS。

- [ ] **Step 6: 修補測試替身**

`IConnectionManager` 多了一個方法，用 `Substitute.For<IConnectionManager>()` 的測試會自動回傳空清單，通常不需改。但若有手寫的 fake 實作類別會編譯失敗。

Run: `dotnet build`
Expected: 成功。若某個手寫 fake 缺 `GetEnabledProfiles`，補上 `=> GetAllProfiles().Where(p => p.IsEnabled).ToList();`。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Application/Services/IConnectionManager.cs src/Specurai.Infrastructure/Services/ConnectionManager.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs
git commit -m "feat: 新增 GetEnabledProfiles 並加上停用連線的 fail-safe

停用的連線無法取得連線字串、無法被設為目前連線。"
```

---

### Task 3: 停用時自動切離目前連線與預設身分

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/ConnectionManager.cs:85-106`（`UpdateProfile`）
- Test: `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs`

**Interfaces:**
- Consumes: `GetEnabledProfiles()`（Task 2）
- Produces: `UpdateProfile` 在 profile 停用時清除其 `IsDefault`，並在它是目前連線時切離、觸發 `CurrentProfileChanged`

- [ ] **Step 1: 寫失敗測試**

```csharp
[Fact]
public void UpdateProfile_停用目前連線_自動切換至第一個啟用連線()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var first = new ConnectionProfile { Name = "甲", Server = "s1", Database = "db1" };
        var second = new ConnectionProfile { Name = "乙", Server = "s2", Database = "db2" };
        manager.AddProfile(first);
        manager.AddProfile(second);
        manager.SetCurrentProfile(second.Id);

        second.IsEnabled = false;
        manager.UpdateProfile(second);

        manager.GetCurrentProfile()!.Id.Should().Be(first.Id);
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void UpdateProfile_停用唯一連線_目前連線變為Null()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var only = new ConnectionProfile { Name = "唯一", Server = "s1", Database = "db1" };
        manager.AddProfile(only);
        manager.SetCurrentProfile(only.Id);

        only.IsEnabled = false;
        manager.UpdateProfile(only);

        manager.GetCurrentProfile().Should().BeNull();
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void UpdateProfile_停用預設連線_一併清除預設身分()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var profile = new ConnectionProfile
        {
            Name = "預設的", Server = "s1", Database = "db1", IsDefault = true
        };
        manager.AddProfile(profile);

        profile.IsEnabled = false;
        manager.UpdateProfile(profile);

        manager.GetAllProfiles().Single().IsDefault.Should().BeFalse();
    }
    finally
    {
        File.Delete(configPath);
    }
}

[Fact]
public void UpdateProfile_停用目前連線_觸發CurrentProfileChanged()
{
    var configPath = Path.Combine(Path.GetTempPath(), $"specurai-{Guid.NewGuid()}.json");
    try
    {
        var manager = new ConnectionManager(configPath);
        var first = new ConnectionProfile { Name = "甲", Server = "s1", Database = "db1" };
        var second = new ConnectionProfile { Name = "乙", Server = "s2", Database = "db2" };
        manager.AddProfile(first);
        manager.AddProfile(second);
        manager.SetCurrentProfile(second.Id);

        ConnectionProfile? raised = null;
        var raisedCount = 0;
        manager.CurrentProfileChanged += (_, p) => { raised = p; raisedCount++; };

        second.IsEnabled = false;
        manager.UpdateProfile(second);

        raisedCount.Should().Be(1);
        raised!.Id.Should().Be(first.Id);
    }
    finally
    {
        File.Delete(configPath);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~UpdateProfile"`
Expected: FAIL — 目前連線仍是「乙」、`IsDefault` 仍為 `true`、事件未觸發。

- [ ] **Step 3: 實作**

把 `src/Specurai.Infrastructure/Services/ConnectionManager.cs` 的 `UpdateProfile` 整個換成：

```csharp
    public void UpdateProfile(ConnectionProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0)
            return;

        if (profile.IsDefault)
        {
            foreach (var p in _profiles)
            {
                p.IsDefault = false;
            }
        }

        // 停用的連線不該保留預設身分，否則會留下一個永遠選不到的預設連線
        if (!profile.IsEnabled)
        {
            profile.IsDefault = false;
        }

        _profiles[index] = profile;
        SaveProfiles();

        // 停用目前連線時自動切離至第一個啟用的連線，沒有就變成無連線
        if (!profile.IsEnabled && _currentProfileId == profile.Id)
        {
            var fallback = _profiles.FirstOrDefault(p => p.IsEnabled);
            _currentProfileId = fallback?.Id;
            _currentDatabaseOverride = null;
            CurrentProfileChanged?.Invoke(this, fallback);
            return;
        }

        if (_currentProfileId == profile.Id)
        {
            CurrentProfileChanged?.Invoke(this, profile);
        }
    }
```

判斷依據是「更新後的狀態」而非前後值比對——UI 勾選會就地修改同一個實例，`_profiles[index]` 與 `profile` 可能是同一個參考，比對舊值不可靠。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ConnectionManagerTests"`
Expected: PASS，全部通過（含 Task 1、2 的測試）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Services/ConnectionManager.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTests.cs
git commit -m "feat: 停用連線時自動切離目前連線並清除預設身分"
```

---

### Task 4: Desktop 文件 ViewModel 分流

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs:177`
- Modify: `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs:121`
- Modify: `src/Specurai.Desktop/ViewModels/ColumnSearchDocumentViewModel.cs:144`
- Modify: `src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs:244`
- Modify: `src/Specurai.Desktop/ViewModels/SchemaCompareDocumentViewModel.cs:225`
- Modify: `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs:135`
- Modify: `src/Specurai.Desktop/ViewModels/UsageAnalysisDocumentViewModel.cs:351,362`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/ColumnSearchDocumentViewModelTests.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SchemaCompareDocumentViewModelTests.cs`

**Interfaces:**
- Consumes: `IConnectionManager.GetEnabledProfiles()`（Task 2）
- Produces: 無新介面

- [ ] **Step 1: 寫失敗測試（挑兩個代表性 ViewModel）**

`ColumnSearchDocumentViewModelTests.cs`：

```csharp
[Fact]
public void LoadConnectionProfiles_有停用連線_不出現在連線清單()
{
    var connectionManager = Substitute.For<IConnectionManager>();
    var enabled = new ConnectionProfile
    {
        Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
    };
    connectionManager.GetEnabledProfiles().Returns([enabled]);
    connectionManager.GetAllProfiles().Returns([
        enabled,
        new ConnectionProfile { Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false }
    ]);

    var vm = CreateViewModel(connectionManager);

    vm.ConnectionProfiles.Should().ContainSingle().Which.Name.Should().Be("啟用的");
}
```

`CreateViewModel` 依該測試檔既有的建構方式撰寫（多數測試檔已有相同的 helper 或直接 `new XxxViewModel(...)`；沿用既有寫法，不要新增 helper）。

`SchemaCompareDocumentViewModelTests.cs` 寫同樣結構的一支，斷言其連線集合屬性（依該 ViewModel 實際的集合名稱調整）。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~不出現在連線清單"`
Expected: FAIL — 清單有 2 筆（仍走 `GetAllProfiles()`）。

- [ ] **Step 3: 逐一改呼叫**

七個檔案的改法一致，把載入連線清單處的 `GetAllProfiles()` 換成 `GetEnabledProfiles()`：

```csharp
        var profiles = _connectionManager?.GetEnabledProfiles() ?? [];
```

各檔案的實際位置：

- `MainWindowViewModel.cs:177`（`LoadConnectionProfiles`）
- `SqlQueryDocumentViewModel.cs:121`
- `ColumnSearchDocumentViewModel.cs:144`
- `BackupRestoreDocumentViewModel.cs:244`
- `SchemaCompareDocumentViewModel.cs:225`
- `SchemaMigrationDocumentViewModel.cs:135` — 該處是 `foreach (var profile in _connectionManager?.GetAllProfiles() ?? [])`，改成 `GetEnabledProfiles()`
- `UsageAnalysisDocumentViewModel.cs:351` 與 `:362` — 兩處都改；`:362` 是 `foreach (var p in _connectionManager.GetAllProfiles().Where(p => p.Id != value.Id))`，改成 `GetEnabledProfiles()`

只改這一個方法呼叫，不要動相鄰的邏輯、註解與格式。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: PASS。

若有既有測試因為 mock 只設定了 `GetAllProfiles()` 而失敗，在該測試補上 `connectionManager.GetEnabledProfiles().Returns(...)`，回傳與 `GetAllProfiles()` 相同的清單即可——這些測試原本就沒有停用連線的情境。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs src/Specurai.Desktop/ViewModels/ColumnSearchDocumentViewModel.cs src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs src/Specurai.Desktop/ViewModels/SchemaCompareDocumentViewModel.cs src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs src/Specurai.Desktop/ViewModels/UsageAnalysisDocumentViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/ColumnSearchDocumentViewModelTests.cs tests/Specurai.Desktop.Tests/ViewModels/SchemaCompareDocumentViewModelTests.cs
git commit -m "feat: Desktop 各功能連線清單只列啟用的連線"
```

---

### Task 5: 連線設定畫面的啟用勾選

**Files:**
- Create: `src/Specurai.Desktop/Converters/BoolToOpacityConverter.cs`
- Modify: `src/Specurai.Desktop/App.axaml:11` 附近（converter 註冊區）
- Modify: `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs`
- Modify: `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml:38-46`（連線清單 ItemTemplate）與右側表單
- Test: `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs`

**Interfaces:**
- Consumes: `IConnectionManager.UpdateProfile`、`ConnectionProfile.IsEnabled`
- Produces: `ConnectionSetupViewModel.IsEnabled`（`bool`，預設 `true`）、`ConnectionSetupViewModel.ToggleProfileEnabledCommand`（參數 `ConnectionProfile`）

- [ ] **Step 1: 寫失敗測試**

加到 `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs`：

```csharp
[Fact]
public void ToggleProfileEnabled_切換啟用_呼叫UpdateProfile存檔()
{
    var connectionManager = Substitute.For<IConnectionManager>();
    var profile = new ConnectionProfile
    {
        Name = "測試", Server = "s1", Database = "db1", IsEnabled = false
    };
    connectionManager.GetAllProfiles().Returns([profile]);
    var vm = new ConnectionSetupViewModel(connectionManager);

    vm.ToggleProfileEnabledCommand.Execute(profile);

    connectionManager.Received(1).UpdateProfile(profile);
}

[Fact]
public void OnSelectedProfileChanged_選取停用的連線_表單顯示未啟用()
{
    var connectionManager = Substitute.For<IConnectionManager>();
    var profile = new ConnectionProfile
    {
        Name = "測試", Server = "s1", Database = "db1", IsEnabled = false
    };
    connectionManager.GetAllProfiles().Returns([profile]);
    var vm = new ConnectionSetupViewModel(connectionManager);

    vm.SelectedProfile = profile;

    vm.IsEnabled.Should().BeFalse();
}

[Fact]
public void Save_表單啟用為False_建立的Profile為停用()
{
    var connectionManager = Substitute.For<IConnectionManager>();
    connectionManager.GetAllProfiles().Returns([]);
    var vm = new ConnectionSetupViewModel(connectionManager)
    {
        Name = "新連線",
        Server = "s1",
        Database = "db1",
        IsEnabled = false
    };

    vm.SaveCommand.Execute(null);

    connectionManager.Received(1).AddProfile(Arg.Is<ConnectionProfile>(p => !p.IsEnabled));
}

[Fact]
public void NewProfile_清空表單_啟用回到預設True()
{
    var connectionManager = Substitute.For<IConnectionManager>();
    connectionManager.GetAllProfiles().Returns([]);
    var vm = new ConnectionSetupViewModel(connectionManager)
    {
        IsEnabled = false
    };

    vm.NewProfileCommand.Execute(null);

    vm.IsEnabled.Should().BeTrue();
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionSetupViewModelTests"`
Expected: FAIL，編譯錯誤找不到 `IsEnabled` 與 `ToggleProfileEnabledCommand`。

- [ ] **Step 3: ViewModel 加屬性與命令**

在 `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs` 的 `_isDefault` 欄位之後加入：

```csharp
    [ObservableProperty]
    private bool _isEnabled = true;
```

`OnSelectedProfileChanged` 的 `IsDefault = value.IsDefault;` 之後加入：

```csharp
            IsEnabled = value.IsEnabled;
```

`CreateProfileFromForm` 的物件初始式中，`Environment = Environment` 之後加入：

```csharp
            IsEnabled = IsEnabled
```

（記得把前一行 `Environment = Environment` 補上逗號。）

`ClearForm` 的 `IsDefault = false;` 之後加入：

```csharp
        IsEnabled = true;
```

在 `Delete` 命令之後加入切換命令：

```csharp
    /// <summary>
    /// 切換連線的啟用狀態並立即存檔（CheckBox 已先寫回 IsEnabled，此處只負責持久化）
    /// </summary>
    [RelayCommand]
    private void ToggleProfileEnabled(ConnectionProfile? profile)
    {
        if (_connectionManager == null || profile == null) return;

        _connectionManager.UpdateProfile(profile);

        // 若切換的正是目前編輯中的連線，表單同步顯示新狀態
        if (SelectedProfile?.Id == profile.Id)
            IsEnabled = profile.IsEnabled;
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ConnectionSetupViewModelTests"`
Expected: PASS。

- [ ] **Step 5: 建立 converter**

建立 `src/Specurai.Desktop/Converters/BoolToOpacityConverter.cs`：

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Specurai.Desktop.Converters;

/// <summary>
/// 將布林值轉為透明度：true 為 1.0，false 為 0.45（用於停用項目的灰階呈現）。
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? 0.45 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`value is false` 讓 `null`（CheckBox 的 `IsChecked` 為 `bool?`）落在 1.0，符合「未知時視為正常顯示」。

- [ ] **Step 6: 註冊 converter**

在 `src/Specurai.Desktop/App.axaml` 的 `ConnectionProfileDisplayConverter` 那一行下方加入：

```xml
        <converters:BoolToOpacityConverter x:Key="BoolToOpacityConverter"/>
```

- [ ] **Step 7: 清單加勾選欄**

把 `src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml` 中的連線清單 ListBox（第 38-46 行，`ItemsSource="{Binding Profiles}"` 那個）換成：

```xml
                            <ListBox x:Name="ProfileList"
                                     ItemsSource="{Binding Profiles}"
                                     SelectedItem="{Binding SelectedProfile}">
                                <ListBox.ItemTemplate>
                                    <DataTemplate x:DataType="domain:ConnectionProfile">
                                        <StackPanel Orientation="Horizontal" Spacing="6">
                                            <CheckBox x:Name="EnabledCheck"
                                                      IsChecked="{Binding IsEnabled, Mode=TwoWay}"
                                                      ToolTip.Tip="停用後此連線不會出現在各功能的連線選擇中"
                                                      Command="{Binding #ProfileList.((vm:ConnectionSetupViewModel)DataContext).ToggleProfileEnabledCommand}"
                                                      CommandParameter="{Binding}"/>
                                            <TextBlock VerticalAlignment="Center"
                                                       Opacity="{Binding #EnabledCheck.IsChecked, Converter={StaticResource BoolToOpacityConverter}}"
                                                       Text="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"
                                                       ToolTip.Tip="{Binding Converter={StaticResource ConnectionProfileDisplayConverter}}"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </ListBox.ItemTemplate>
                            </ListBox>
```

灰階綁的是 CheckBox 的 `IsChecked` 而不是 `IsEnabled` 屬性本身：`ConnectionProfile` 是純 Domain 實體、沒有 `INotifyPropertyChanged`，直接綁屬性在勾選後不會更新；`IsChecked` 是 AvaloniaProperty，會即時通知。

外部連線那個 ListBox（`ItemsSource="{Binding ExternalProfiles}"`）**不要改** —— 外部連線是唯讀清單，沒有啟用概念。

- [ ] **Step 8: 表單加 CheckBox**

在 `ConnectionSetupWindow.axaml` 右側表單中，找到「環境」那個 `StackPanel`，在其後加入：

```xml
                            <!-- 啟用 -->
                            <CheckBox Content="啟用此連線"
                                      IsChecked="{Binding IsEnabled}"
                                      IsEnabled="{Binding !IsExternalProfileSelected}"
                                      ToolTip.Tip="停用後此連線不會出現在各功能的連線選擇中"/>
```

- [ ] **Step 9: 建置並手動驗證**

Run: `dotnet build`
Expected: 成功，無 AXAML 編譯錯誤。

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`
手動確認：開啟連線設定 → 清單每列左側有勾選框 → 取消勾選後該列文字轉灰 → 關閉再開啟連線設定，停用狀態仍在 → 主視窗連線下拉不再出現該連線。

- [ ] **Step 10: Commit**

```bash
git add src/Specurai.Desktop/Converters/BoolToOpacityConverter.cs src/Specurai.Desktop/App.axaml src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs src/Specurai.Desktop/Views/ConnectionSetupWindow.axaml tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs
git commit -m "feat: 連線設定畫面可勾選啟用，停用列以灰階呈現"
```

---

### Task 6: CLI 分流與錯誤訊息

**Files:**
- Modify: `src/Specurai.Cli/ConnectionResolver.cs:129-133`
- Modify: `src/Specurai.Cli/Commands/ConnCommand.cs`（`list` 加停用標記；第 63、308、344、406 行錯誤訊息）
- Modify: `src/Specurai.Cli/Commands/ColumnsCommand.cs:32`
- Modify: `src/Specurai.Cli/Commands/SqlCommand.cs:366,374,379`
- Modify: `src/Specurai.Cli/Commands/SchemaCommand.cs:38,48,159,173,176`
- Modify: `src/Specurai.Cli/Commands/UsageCommand.cs:114,127,129`
- Modify: `src/Specurai.Cli/Commands/MigrationCommand.cs:41,47,51,195,242,312,316`
- Test: `tests/Specurai.Cli.Tests/ConnectionResolverTests.cs`

**Interfaces:**
- Consumes: `IConnectionManager.GetEnabledProfiles()`
- Produces: `ConnectionResolver.DescribeMissing(IConnectionManager cm, string name)` → `string`（靜態方法）

- [ ] **Step 1: 寫失敗測試**

加到 `tests/Specurai.Cli.Tests/ConnectionResolverTests.cs`：

```csharp
[Fact]
public void DescribeMissing_連線存在但已停用_回傳已停用訊息()
{
    var cm = Substitute.For<IConnectionManager>();
    cm.GetEnabledProfiles().Returns([]);
    cm.GetAllProfiles().Returns([
        new ConnectionProfile { Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false }
    ]);

    var message = ConnectionResolver.DescribeMissing(cm, "正式庫");

    message.Should().Be("連線「正式庫」已停用，請先在連線設定中啟用。");
}

[Fact]
public void DescribeMissing_連線不存在_回傳找不到訊息()
{
    var cm = Substitute.For<IConnectionManager>();
    cm.GetEnabledProfiles().Returns([]);
    cm.GetAllProfiles().Returns([]);

    var message = ConnectionResolver.DescribeMissing(cm, "不存在");

    message.Should().Be("找不到連線「不存在」");
}

[Fact]
public void Resolve_指定的Profile已停用_回傳Null()
{
    var cm = Substitute.For<IConnectionManager>();
    var disabled = new ConnectionProfile
    {
        Name = "停用的", Server = "s1", Database = "db1", IsEnabled = false
    };
    cm.GetEnabledProfiles().Returns([]);
    cm.GetAllProfiles().Returns([disabled]);

    var profile = new ConnectionResolver(cm).Resolve(new GlobalOptions { Profile = "停用的" });

    profile.Should().BeNull();
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnectionResolverTests"`
Expected: FAIL，找不到 `DescribeMissing`。

- [ ] **Step 3: 實作 resolver**

把 `src/Specurai.Cli/ConnectionResolver.cs` 的 `FromProfileName` 改成走啟用清單，並在其後加入 `DescribeMissing`：

```csharp
    /// <summary>
    /// 從已儲存的 profile 名稱查找（只找啟用的連線）
    /// </summary>
    private ConnectionProfile? FromProfileName(string name)
    {
        return _connectionManager.GetEnabledProfiles()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 產生「找不到連線」的錯誤訊息；若該連線存在但已停用，回傳更明確的說明。
    /// </summary>
    public static string DescribeMissing(IConnectionManager connectionManager, string name)
    {
        var disabled = connectionManager.GetAllProfiles()
            .FirstOrDefault(p =>
                !p.IsEnabled &&
                (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                 p.Id.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)));

        return disabled != null
            ? $"連線「{disabled.Name}」已停用，請先在連線設定中啟用。"
            : $"找不到連線「{name}」";
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnectionResolverTests"`
Expected: PASS。

- [ ] **Step 5: 選用型命令改用啟用清單**

以下每處的 `cm.GetAllProfiles()` 改為 `cm.GetEnabledProfiles()`，其餘不動：

- `ColumnsCommand.cs:32`
- `SqlCommand.cs:366`（`--all` 展開所有 profile）與 `:374`（依名稱查找）
- `SchemaCommand.cs:38, 48, 159, 173`
- `UsageCommand.cs:114, 127`
- `MigrationCommand.cs:41, 47, 195, 242, 312`

`ConnCommand.cs` 的所有 `GetAllProfiles()` **維持不變**——它是連線管理命令，要看得到停用的連線。

- [ ] **Step 6: 錯誤訊息改用 DescribeMissing**

把下列各行的字串字面值換成 `ConnectionResolver.DescribeMissing(cm, <名稱變數>)`：

- `SchemaCommand.cs:42` — 改為 `CliOutput.Error(string.IsNullOrEmpty(baseName) ? "未設定目前連線" : ConnectionResolver.DescribeMissing(cm, baseName));`
- `SchemaCommand.cs:53` — 改為 `CliOutput.Error(ConnectionResolver.DescribeMissing(cm, targetName));`
- `SchemaCommand.cs:163` — 同 `:42` 的寫法
- `SchemaCommand.cs:176` — 改為 `CliOutput.Warning($"{ConnectionResolver.DescribeMissing(cm, tn)}，已跳過");`
- `SqlCommand.cs:379` — 改為 `CliOutput.Warning($"{ConnectionResolver.DescribeMissing(cm, pn)}，已跳過");`
- `UsageCommand.cs:129` — 改為 `CliOutput.Warning($"{ConnectionResolver.DescribeMissing(cm, tn)}，已跳過");`
- `MigrationCommand.cs:44` — 同 `SchemaCommand.cs:42` 的寫法（變數為 `baseName`）
- `MigrationCommand.cs:51`、`:316` — 改為 `CliOutput.Error(ConnectionResolver.DescribeMissing(cm, targetName));`
- `DatabasesCommand.cs:24` **維持不變** — 該訊息是「完全沒指定連線」，與名稱查找無關。

`ConnCommand.cs` 第 63、308、344、406 行的 `找不到連線「{name}」` **維持不變**——連線管理命令走的是全量清單，找不到就是真的不存在。

- [ ] **Step 7: `conn list` 顯示停用標記**

在 `src/Specurai.Cli/Commands/ConnCommand.cs` 的 `CreateListCommand`：

JSON 模式的投影加上 `IsEnabled`（在 `p.IsDefault` 之後）：

```csharp
                    p.IsDefault,
                    p.IsEnabled
```

表格模式的 `status` 計算改為（停用優先顯示，因為停用的連線不可能是目前連線）：

```csharp
                    var status = !p.IsEnabled
                        ? "[red]停用[/]"
                        : isCurrent ? "[green]← 目前[/]" : (p.IsDefault ? "[grey]預設[/]" : "");
```

名稱欄的著色改為（停用時轉灰）：

```csharp
                    var nameCell = !p.IsEnabled
                        ? $"[grey]{p.Name.EscapeMarkup()}[/]"
                        : isCurrent ? $"[green]{p.Name.EscapeMarkup()}[/]" : p.Name.EscapeMarkup();
```

並把 `table.AddRow` 的第一個引數換成 `nameCell`。

- [ ] **Step 8: 建置並跑 CLI 測試**

Run: `dotnet build && dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj`
Expected: 建置成功、測試全過。既有測試若因 mock 只設 `GetAllProfiles()` 而失敗，補上 `GetEnabledProfiles()` 回傳相同清單。

- [ ] **Step 9: Commit**

```bash
git add src/Specurai.Cli/ConnectionResolver.cs src/Specurai.Cli/Commands/ConnCommand.cs src/Specurai.Cli/Commands/ColumnsCommand.cs src/Specurai.Cli/Commands/SqlCommand.cs src/Specurai.Cli/Commands/SchemaCommand.cs src/Specurai.Cli/Commands/UsageCommand.cs src/Specurai.Cli/Commands/MigrationCommand.cs tests/Specurai.Cli.Tests/ConnectionResolverTests.cs
git commit -m "feat: CLI 只選用啟用的連線，指定停用連線時明確提示

conn list 額外顯示停用標記。"
```

---

### Task 7: MCP 分流與錯誤訊息

**Files:**
- Modify: `src/Specurai.McpServer/Tools/ProfileResolver.cs`
- Modify: `src/Specurai.McpServer/Tools/ConnectionTools.cs:22,52-59`
- Modify: `src/Specurai.McpServer/Tools/MigrationTools.cs:26,30,228`
- Modify: `src/Specurai.McpServer/Tools/SchemaCompareTools.cs`、`UsageAnalysisTools.cs`（錯誤訊息）
- Test: `tests/Specurai.McpServer.Tests/ConnectionToolsTests.cs`

**Interfaces:**
- Consumes: `IConnectionManager.GetEnabledProfiles()`
- Produces: `ProfileResolver.DescribeMissing(IConnectionManager cm, string nameOrId)` → `string`

- [ ] **Step 1: 寫失敗測試**

加到 `tests/Specurai.McpServer.Tests/ConnectionToolsTests.cs`：

```csharp
[Fact]
public void SwitchConnection_目標連線已停用_回傳已停用訊息()
{
    var cm = Substitute.For<IConnectionManager>();
    var disabled = new ConnectionProfile
    {
        Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
    };
    cm.GetEnabledProfiles().Returns([]);
    cm.GetAllProfiles().Returns([disabled]);

    var result = ConnectionTools.SwitchConnection(cm, "正式庫");

    result.Should().Be("連線「正式庫」已停用，請先在連線設定中啟用。");
}

[Fact]
public void ListConnections_有停用連線_輸出包含IsEnabled()
{
    var cm = Substitute.For<IConnectionManager>();
    cm.GetAllProfiles().Returns([
        new ConnectionProfile { Name = "停用的", Server = "s1", Database = "db1", IsEnabled = false }
    ]);

    var result = ConnectionTools.ListConnections(cm);

    result.Should().Contain("\"IsEnabled\": false");
}

[Fact]
public void ResolveMultiple_未指定名稱_只回傳啟用的連線()
{
    var cm = Substitute.For<IConnectionManager>();
    var enabled = new ConnectionProfile
    {
        Name = "啟用的", Server = "s1", Database = "db1", IsEnabled = true
    };
    cm.GetEnabledProfiles().Returns([enabled]);
    cm.GetAllProfiles().Returns([
        enabled,
        new ConnectionProfile { Name = "停用的", Server = "s2", Database = "db2", IsEnabled = false }
    ]);

    var ids = ProfileResolver.ResolveMultiple(cm, "");

    ids.Should().ContainSingle().Which.Should().Be(enabled.Id);
}
```

`ProfileResolver` 目前是 `internal static`，測試專案需要能存取。若 `Specurai.McpServer` 尚未對測試專案開放 internal，在 `src/Specurai.McpServer/Specurai.McpServer.csproj` 加入：

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Specurai.McpServer.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~ConnectionToolsTests"`
Expected: FAIL — `SwitchConnection` 回傳「找不到名稱或 ID 為「正式庫」的連線設定。」、輸出無 `IsEnabled`。

- [ ] **Step 3: 改 ProfileResolver**

把 `src/Specurai.McpServer/Tools/ProfileResolver.cs` 整個換成：

```csharp
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線設定檔解析輔助工具（只解析已啟用的連線）
/// </summary>
internal static class ProfileResolver
{
    /// <summary>
    /// 依名稱或 ID 解析單一已啟用的連線設定檔
    /// </summary>
    public static ConnectionProfile? Resolve(IConnectionManager cm, string nameOrId)
    {
        var profiles = cm.GetEnabledProfiles();
        return profiles.FirstOrDefault(p =>
            p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析逗號分隔的名稱/ID 清單為 Guid 清單，空字串回傳所有已啟用的 Profile ID
    /// </summary>
    public static List<Guid> ResolveMultiple(IConnectionManager cm, string commaSeparated)
    {
        var profiles = cm.GetEnabledProfiles();

        if (string.IsNullOrWhiteSpace(commaSeparated))
            return profiles.Select(p => p.Id).ToList();

        var result = new List<Guid>();

        foreach (var item in commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var profile = profiles.FirstOrDefault(p =>
                p.Name.Equals(item, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Equals(item, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
                result.Add(profile.Id);
        }

        return result;
    }

    /// <summary>
    /// 產生「找不到連線」的錯誤訊息；若該連線存在但已停用，回傳更明確的說明。
    /// </summary>
    public static string DescribeMissing(IConnectionManager cm, string nameOrId)
    {
        var disabled = cm.GetAllProfiles()
            .FirstOrDefault(p =>
                !p.IsEnabled &&
                (p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
                 p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase)));

        return disabled != null
            ? $"連線「{disabled.Name}」已停用，請先在連線設定中啟用。"
            : $"找不到名稱或 ID 為「{nameOrId}」的連線設定。";
    }
}
```

- [ ] **Step 4: 改 ConnectionTools**

`ListConnections`（`:22`）維持 `GetAllProfiles()`（管理型，要看得到停用的），但投影加上 `IsEnabled`。把 `p.IsDefault` 那行改成：

```csharp
            p.IsDefault,
            p.IsEnabled
```

`SwitchConnection`（`:52-59`）改為走 resolver：

```csharp
        var profile = ProfileResolver.Resolve(connectionManager, nameOrId);

        if (profile == null)
            return ProfileResolver.DescribeMissing(connectionManager, nameOrId);
```

（連同刪掉原本的 `var profiles = connectionManager.GetAllProfiles();` 與那段 `FirstOrDefault`。）

- [ ] **Step 5: 改 MigrationTools 與其餘工具的錯誤訊息**

`MigrationTools.cs:26, 30, 228` 的 `cm.GetAllProfiles()` 改為 `cm.GetEnabledProfiles()`。

`SchemaCompareTools.cs`（`:27, 31, 81, 94`）與 `UsageAnalysisTools.cs`（`:62`）已經走 `ProfileResolver.Resolve`，會自動只解析啟用的連線；把它們在 `profile == null` 時回傳的字串字面值改為 `ProfileResolver.DescribeMissing(connectionManager, <對應的名稱變數>)`。

`ConnectionCrudTools.cs`（`:68, 99, 124`）**維持不變**——連線 CRUD 是管理型工具，要能編輯停用的連線。

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet build && dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 建置成功、測試全過。既有測試若因 mock 只設 `GetAllProfiles()` 而失敗，補上 `GetEnabledProfiles()` 回傳相同清單。

- [ ] **Step 7: 全套測試**

Run: `dotnet test`
Expected: 全部通過（基準為 604 支，本次新增約 18 支）。

- [ ] **Step 8: Commit**

```bash
git add src/Specurai.McpServer/Tools/ProfileResolver.cs src/Specurai.McpServer/Tools/ConnectionTools.cs src/Specurai.McpServer/Tools/MigrationTools.cs src/Specurai.McpServer/Tools/SchemaCompareTools.cs src/Specurai.McpServer/Tools/UsageAnalysisTools.cs tests/Specurai.McpServer.Tests/ConnectionToolsTests.cs
git commit -m "feat: MCP 只選用啟用的連線，指定停用連線時明確提示

list_connections 輸出加上 IsEnabled。"
```

> 注意：重新建置 McpServer 前要先結束執行中的 MCP 行程，否則 DLL 被鎖會導致全方案 build 失敗。
