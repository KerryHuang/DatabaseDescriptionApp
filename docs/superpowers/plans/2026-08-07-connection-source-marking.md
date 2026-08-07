# 連線來源標記與匯入修復 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 連線設定檔加上外部／自建來源標記與顯示，修復 MCP 匯入不去重與 CLI 匯入環境誤分類，並一次性修復既有資料。

**Architecture:** Domain 的 `ConnectionProfile` 加 `bool IsExternal` 持久欄位；三條外部建立路徑（inventory 同步、CLI parser、MCP 匯入）設為 true；Desktop converter 顯示【外部】/【自建】。MCP 匯入改為按名稱去重（比照 CLI）。資料修復用一次性腳本處理 `%APPDATA%\Specurai\connections.json`。

**Tech Stack:** .NET 8、xUnit + NSubstitute + FluentAssertions、System.Text.Json。

**Spec:** `docs/superpowers/specs/2026-08-07-connection-source-marking-design.md`

## Global Constraints

- 一律以繁體中文寫 UI 文字、註解、commit 訊息。
- Clean Architecture 分層：Domain 無相依；Application 只依 Domain；測試放對應層的測試專案。
- 測試命名：`[Method]_[Condition]_[Expected]` 繁體中文。
- 檔案 UTF-8 無 BOM。
- 每個 task 結尾 commit（逐檔指名 `git add`，禁止 `git add -A`）。

---

### Task 1: Domain — `ConnectionProfile.IsExternal`

**Files:**
- Modify: `src/Specurai.Domain/Entities/ConnectionProfile.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs`

**Interfaces:**
- Produces: `public bool IsExternal { get; set; }`（預設 false = 自建），後續 task 全部依賴此屬性名。

- [ ] **Step 1: 寫失敗測試**

在 `ConnectionProfileTests.cs` 加入（沿用該檔既有建構樣式）：

```csharp
[Fact]
public void IsExternal_未設定_預設為自建()
{
    var profile = new ConnectionProfile { Name = "n", Server = "s", Database = "d" };

    profile.IsExternal.Should().BeFalse();
}

[Fact]
public void IsExternal_舊版JSON無此欄位_反序列化為自建()
{
    var json = """{"Name":"n","Server":"s","Database":"d"}""";

    var profile = System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>(json);

    profile!.IsExternal.Should().BeFalse();
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~IsExternal"`
Expected: 編譯失敗（`IsExternal` 不存在）。

- [ ] **Step 3: 實作**

`ConnectionProfile.cs` 在 `IsEnabled` 之後加：

```csharp
    /// <summary>
    /// 是否來自外部（外部來源同步、CLI/MCP 匯入）；false 表示使用者自建
    /// </summary>
    public bool IsExternal { get; set; }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~IsExternal"`
Expected: 2 個測試 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Domain/Entities/ConnectionProfile.cs tests/Specurai.Domain.Tests/Entities/ConnectionProfileTests.cs
git commit -m "feat: ConnectionProfile 新增 IsExternal 來源欄位"
```

---

### Task 2: CLI parser — 環境對應與外部標記

**Files:**
- Modify: `src/Specurai.Cli/ConnectionProfileParser.cs`
- Test: `tests/Specurai.Cli.Tests/ConnectionProfileParserTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `ConnectionProfile.IsExternal`。
- Produces: `FromMpeJson` 讀 `envTag`（`prod`→Production、`dev`→Development、其他/缺→Staging）；`FromSimpleJson` 讀選用 `environment`/`Environment` 字串欄位；兩者皆設 `IsExternal = true`。

- [ ] **Step 1: 寫失敗測試**

在 `ConnectionProfileParserTests.cs` 加入（沿用該檔既有的 JSON 字串測試樣式）：

```csharp
[Theory]
[InlineData("prod", DatabaseEnvironment.Production)]
[InlineData("dev", DatabaseEnvironment.Development)]
[InlineData("staging", DatabaseEnvironment.Staging)]
public void ParseSingle_mpe格式envTag_對應正確環境(string envTag, DatabaseEnvironment expected)
{
    var json = $$"""{"envTag":"{{envTag}}","mssql":{"host":"h"}}""";
    using var doc = JsonDocument.Parse(json);

    var profile = ConnectionProfileParser.ParseSingle(doc.RootElement);

    profile!.Environment.Should().Be(expected);
}

[Fact]
public void ParseSingle_mpe格式無envTag_環境預設預備()
{
    using var doc = JsonDocument.Parse("""{"mssql":{"host":"h"}}""");

    var profile = ConnectionProfileParser.ParseSingle(doc.RootElement);

    profile!.Environment.Should().Be(DatabaseEnvironment.Staging);
}

[Fact]
public void ParseSingle_簡易格式environment欄位_對應正確環境()
{
    using var doc = JsonDocument.Parse("""{"server":"s","environment":"Production"}""");

    var profile = ConnectionProfileParser.ParseSingle(doc.RootElement);

    profile!.Environment.Should().Be(DatabaseEnvironment.Production);
}

[Fact]
public void ParseSingle_任一格式_應標記為外部()
{
    using var mpe = JsonDocument.Parse("""{"mssql":{"host":"h"}}""");
    using var simple = JsonDocument.Parse("""{"server":"s"}""");

    ConnectionProfileParser.ParseSingle(mpe.RootElement)!.IsExternal.Should().BeTrue();
    ConnectionProfileParser.ParseSingle(simple.RootElement)!.IsExternal.Should().BeTrue();
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Cli.Tests --filter "FullyQualifiedName~ConnectionProfileParserTests"`
Expected: 新增測試 FAIL（環境為 Staging 預設、IsExternal 為 false）。

- [ ] **Step 3: 實作**

`FromMpeJson` 的物件初始化器補兩個屬性：

```csharp
            Environment = (root.TryGetProperty("envTag", out var et) ? et.GetString() : null) switch
            {
                "prod" => DatabaseEnvironment.Production,
                "dev" => DatabaseEnvironment.Development,
                _ => DatabaseEnvironment.Staging
            },
            IsExternal = true
```

`FromSimpleJson` 的物件初始化器補：

```csharp
            Environment = ParseEnvironment(root),
            IsExternal = true
```

並在類別內加：

```csharp
    private static DatabaseEnvironment ParseEnvironment(JsonElement root)
    {
        var text = root.TryGetProperty("environment", out var e) ? e.GetString() :
                   root.TryGetProperty("Environment", out e) ? e.GetString() : null;
        return Enum.TryParse<DatabaseEnvironment>(text, ignoreCase: true, out var env)
            ? env
            : DatabaseEnvironment.Staging;
    }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Cli.Tests`
Expected: 全數 PASS（含既有測試——若既有測試斷言 Environment/IsExternal 舊值需一併檢視，預期不會，因既有測試未涉及這兩個欄位）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Cli/ConnectionProfileParser.cs tests/Specurai.Cli.Tests/ConnectionProfileParserTests.cs
git commit -m "fix: CLI 匯入解析 envTag 對應環境並標記外部來源"
```

---

### Task 3: MCP `import_connections` — 按名稱去重＋外部標記

**Files:**
- Modify: `src/Specurai.McpServer/Tools/ConnectionCrudTools.cs:138-178`
- Test: `tests/Specurai.McpServer.Tests/ConnectionCrudToolsImportTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 1 的 `IsExternal`；`IConnectionManager.GetAllProfiles/AddProfile/UpdateProfile`；`IConnectionExportService.ImportFromJson`。
- Produces: 回傳訊息格式「已匯入 N 個、已更新 M 個連線設定。」

- [ ] **Step 1: 寫失敗測試**

新檔 `tests/Specurai.McpServer.Tests/ConnectionCrudToolsImportTests.cs`：

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConnectionCrudToolsImportTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private static ConnectionProfile P(string name, string server = "srv") => new()
    {
        Name = name, Server = server, Database = "db"
    };

    private static (IConnectionManager cm, IConnectionExportService svc) Mocks(
        ConnectionProfile[] existing, ConnectionProfile[] imported)
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(existing);
        var svc = Substitute.For<IConnectionExportService>();
        svc.ImportFromJson(Arg.Any<byte[]>())
            .Returns(new ConnectionExportData { Profiles = imported });
        return (cm, svc);
    }

    [Fact]
    public void ImportConnections_名稱已存在_應更新而非新增()
    {
        var existing = P("甲", "old-server");
        var (cm, svc) = Mocks([existing], [P("甲", "new-server")]);

        var result = ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.DidNotReceive().AddProfile(Arg.Any<ConnectionProfile>());
        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(
            p => p.Id == existing.Id && p.Server == "new-server"));
        result.Should().Be("已匯入 0 個、已更新 1 個連線設定。");
    }

    [Fact]
    public void ImportConnections_名稱不存在_應新增且標記外部()
    {
        var (cm, svc) = Mocks([], [P("乙")]);

        var result = ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.Received(1).AddProfile(Arg.Is<ConnectionProfile>(
            p => p.Name == "乙" && p.IsExternal && !p.IsDefault));
        result.Should().Be("已匯入 1 個、已更新 0 個連線設定。");
    }

    [Fact]
    public void ImportConnections_名稱比對_不分大小寫()
    {
        var existing = P("Alpha");
        var (cm, svc) = Mocks([existing], [P("ALPHA")]);

        ConnectionCrudTools.ImportConnections(cm, svc, _tempFile);

        cm.DidNotReceive().AddProfile(Arg.Any<ConnectionProfile>());
        cm.Received(1).UpdateProfile(Arg.Is<ConnectionProfile>(p => p.Id == existing.Id));
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests --filter "FullyQualifiedName~ConnectionCrudToolsImportTests"`
Expected: FAIL（現行實作全部走 AddProfile、訊息格式不同）。

- [ ] **Step 3: 實作**

`ImportConnections` 的 foreach 迴圈整段改為：

```csharp
            var existingProfiles = connectionManager.GetAllProfiles();
            var imported = 0;
            var updated = 0;

            foreach (var profile in exportData.Profiles)
            {
                var existing = existingProfiles.FirstOrDefault(p =>
                    p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.Server = profile.Server;
                    existing.Database = profile.Database;
                    existing.AuthType = profile.AuthType;
                    existing.Username = profile.Username;
                    existing.Password = profile.Password;
                    existing.Environment = profile.Environment;
                    existing.IsExternal = profile.IsExternal;
                    connectionManager.UpdateProfile(existing);
                    updated++;
                }
                else
                {
                    var newProfile = new ConnectionProfile
                    {
                        Id = Guid.NewGuid(),
                        Name = profile.Name,
                        Server = profile.Server,
                        Database = profile.Database,
                        AuthType = profile.AuthType,
                        Username = profile.Username,
                        Password = profile.Password,
                        IsDefault = false,
                        Environment = profile.Environment,
                        IsEnabled = profile.IsEnabled,
                        IsExternal = true
                    };
                    connectionManager.AddProfile(newProfile);
                    imported++;
                }
            }

            return $"已匯入 {imported} 個、已更新 {updated} 個連線設定。";
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests`
Expected: 全數 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/ConnectionCrudTools.cs tests/Specurai.McpServer.Tests/ConnectionCrudToolsImportTests.cs
git commit -m "fix: MCP import_connections 按名稱去重並標記外部來源"
```

---

### Task 4: 外部來源同步 — 標記外部

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/InventoryConnectionSource.cs:181-190`
- Test: `tests/Specurai.Infrastructure.Tests/Services/InventoryConnectionSourceTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `IsExternal`。

- [ ] **Step 1: 寫失敗測試**

在 `InventoryConnectionSourceTests.cs` 加入（沿用既有 `WriteHostsYml`/`WriteDatabaseYml` helper）：

```csharp
[Fact]
public async Task SyncAsync_產出的連線_應標記為外部()
{
    WriteHostsYml("""
        all:
          children:
            customer_acme:
              vars:
                mssql_host: 192.168.1.10
                customer: acme
              hosts:
                acme-prod:
                  env: production
        """);
    WriteDatabaseYml("customer_acme_production", """
        main_sql_override:
          database: acme_db
        """);

    var result = await _sut.SyncAsync();

    result.Profiles.Should().OnlyContain(p => p.IsExternal);
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~應標記為外部"`
Expected: FAIL（IsExternal 為 false）。

- [ ] **Step 3: 實作**

`BuildProfileAsync` 回傳的物件初始化器補一行：

```csharp
            Environment = ToDatabaseEnvironment(env),
            IsExternal = true
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests`
Expected: 全數 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Services/InventoryConnectionSource.cs tests/Specurai.Infrastructure.Tests/Services/InventoryConnectionSourceTests.cs
git commit -m "feat: 外部來源同步產出的連線標記為外部"
```

---

### Task 5: 顯示標記 —【外部】/【自建】

**Files:**
- Modify: `src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs:27`
- Test: `tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `IsExternal`。
- Produces: 顯示格式 `【環境】【外部|自建】名稱 (預設)`。

- [ ] **Step 1: 更新既有測試＋加新測試**

既有測試期望值全部補上 `【自建】`（測試 helper 未設 IsExternal，預設自建）：

```csharp
    [Theory]
    [InlineData(DatabaseEnvironment.Development, "【開發】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Testing, "【測試】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Staging, "【預備】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Production, "【正式】【自建】Dev-Local")]
```

`Convert_預設連線_應附加預設標記` 期望值改為 `"【正式】【自建】MoldPlan-Schema (預設)"`。

新增：

```csharp
[Fact]
public void Convert_外部連線_應標記外部()
{
    var profile = new ConnectionProfile
    {
        Name = "嘉泰 Production", Server = "s", Database = "d",
        Environment = DatabaseEnvironment.Production, IsExternal = true
    };

    var result = _converter.Convert(profile, typeof(string), null, CultureInfo.InvariantCulture);

    result.Should().Be("【正式】【外部】嘉泰 Production");
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~ConnectionProfileDisplayConverter"`
Expected: FAIL（輸出無來源標記）。

- [ ] **Step 3: 實作**

`ConnectionProfileDisplayConverter.Convert` 回傳行改為：

```csharp
        var source = p.IsExternal ? "外部" : "自建";
        return p.IsDefault
            ? $"【{tag}】【{source}】{p.Name} (預設)"
            : $"【{tag}】【{source}】{p.Name}";
```

同步更新類別 XML 註解為 `【環境簡稱】【外部|自建】名稱 (預設)`。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests`
Expected: 全數 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/Converters/ConnectionProfileDisplayConverter.cs tests/Specurai.Desktop.Tests/Converters/ConnectionProfileDisplayConverterTests.cs
git commit -m "feat: 連線顯示標記【外部】/【自建】來源"
```

---

### Task 6: 一次性資料修復（不入版控）

**Files:**
- 操作對象：`%APPDATA%\Specurai\connections.json`（使用者機器資料，非 repo 檔案）
- 腳本放 scratchpad，不 commit。

**Interfaces:**
- Consumes: Task 1 定義的 JSON 欄位名 `IsExternal`。

- [ ] **Step 1: 備份**

```bash
cp "$APPDATA/Specurai/connections.json" "$APPDATA/Specurai/connections.backup-20260807.json"
```

- [ ] **Step 2: 執行修復腳本**

Python 腳本（寫入 scratchpad 後執行）：

```python
import json, os

path = os.path.expandvars(r'%APPDATA%\Specurai\connections.json')
data = json.load(open(path, encoding='utf-8'))

# 1) 去重：同 (Name, Server, Database, Username) 保留第一筆
seen, deduped = set(), []
for p in data['Profiles']:
    key = (p['Name'].lower(), p['Server'].lower(), p['Database'].lower(),
           (p.get('Username') or '').lower())
    if key in seen:
        continue
    seen.add(key)
    deduped.append(p)
removed = len(data['Profiles']) - len(deduped)

# 2) 名稱含 Production 且環境為預備(2) → 正式(3) 並標記外部
fixed = 0
for p in deduped:
    if 'Production' in p['Name'] and p['Environment'] == 2:
        p['Environment'] = 3
        p['IsExternal'] = True
        fixed += 1

data['Profiles'] = deduped
with open(path, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
print(f'刪除重複 {removed} 筆，環境修正 {fixed} 筆，剩餘 {len(deduped)} 筆')
```

Expected: 刪除重複 24 筆、環境修正 12 筆（名稱為中文客戶名＋Production 那批；
`Digwin-Production` 原本就是正式環境不受影響）。

- [ ] **Step 3: 驗證**

重新列出所有 profile 確認：無同名重複、`MoldPlan-Schema` 仍為預設、
12 筆 Production 連線 `Environment == 3` 且 `IsExternal == true`。

---

### Task 8: `ConnectionManager` — 臨時連線可成為目前連線

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/ConnectionManager.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs`（新檔）

**Interfaces:**
- Consumes: 既有 `RegisterTemporaryProfiles(IReadOnlyList<ConnectionProfile>)`。
- Produces: `SetCurrentProfile` / `GetCurrentProfile` 涵蓋臨時連線；`SaveProfiles` 在目前連線為臨時時不寫入其 Id。

**背景：** 這是既有 bug。`SetCurrentProfile` 與 `GetCurrentProfile` 目前只查 `_profiles`，
所以 `RegisterTemporaryProfiles` 註冊的外部連線永遠選不到（靜默失敗）。後續 Task 9、10
的「外部連線可從清單直接使用」都依賴此修復。

- [ ] **Step 1: 寫失敗測試**

新檔 `tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs`：

```csharp
using System.Text.Json;
using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class ConnectionManagerTemporaryProfileTests : IDisposable
{
    private readonly string _configPath =
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-connections.json");

    public void Dispose()
    {
        if (File.Exists(_configPath)) File.Delete(_configPath);
    }

    private static ConnectionProfile Temp(string name = "外部連線") => new()
    {
        Name = name, Server = "ext-srv", Database = "ext-db",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u", Password = "p", IsExternal = true
    };

    [Fact]
    public void SetCurrentProfile_臨時連線_應可成為目前連線()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);

        sut.SetCurrentProfile(temp.Id);

        sut.GetCurrentProfile().Should().BeSameAs(temp);
        sut.GetCurrentConnectionString().Should().Contain("ext-srv");
    }

    [Fact]
    public void SetCurrentProfile_臨時連線_應觸發連線變更事件()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);
        ConnectionProfile? raised = null;
        sut.CurrentProfileChanged += (_, p) => raised = p;

        sut.SetCurrentProfile(temp.Id);

        raised.Should().BeSameAs(temp);
    }

    [Fact]
    public void SaveProfiles_目前連線為臨時連線_不寫入其Id()
    {
        var sut = new ConnectionManager(_configPath);
        var temp = Temp();
        sut.RegisterTemporaryProfiles([temp]);
        sut.SetCurrentProfile(temp.Id);

        // AddProfile 會觸發存檔
        sut.AddProfile(new ConnectionProfile
        {
            Name = "自建", Server = "s", Database = "d"
        });

        using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
        var names = doc.RootElement.GetProperty("Profiles")
            .EnumerateArray().Select(e => e.GetProperty("Name").GetString()).ToList();
        names.Should().ContainSingle().Which.Should().Be("自建");
        doc.RootElement.GetProperty("CurrentProfileId").GetGuid()
            .Should().NotBe(temp.Id);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~ConnectionManagerTemporaryProfileTests"`
Expected: FAIL——`GetCurrentProfile()` 回 null（`SetCurrentProfile` 找不到臨時連線）。

- [ ] **Step 3: 實作**

`ConnectionManager.cs` 加一個私有查找 helper，並改三處：

```csharp
    /// <summary>依 Id 查找連線（臨時連線優先，其次已落地連線）。</summary>
    private ConnectionProfile? FindProfile(Guid id) =>
        _temporaryProfiles.FirstOrDefault(p => p.Id == id)
        ?? _profiles.FirstOrDefault(p => p.Id == id);
```

`SetCurrentProfile` 開頭的查找改為：

```csharp
        var profile = FindProfile(profileId);
        if (profile is { IsEnabled: true })
```

`GetCurrentProfile` 中原本 `_profiles.FirstOrDefault(p => p.Id == _currentProfileId && p.IsEnabled)`
的那一行改為：

```csharp
        var current = FindProfile(_currentProfileId ?? Guid.Empty) is { IsEnabled: true } found
            ? found
            : null;
```

（其餘自我修復邏輯——`_currentProfileId` 指向停用連線時退回啟用的預設連線——維持不變，
且退回時仍只從 `_profiles` 找預設連線，因為臨時連線不會是預設連線。）

`SaveProfiles` 的 `ConnectionData` 建構改為：

```csharp
            // 目前連線若是臨時連線（外部同步而來），不寫入檔案——它下次啟動並不存在
            var persistedCurrentId =
                _currentProfileId != null && _temporaryProfiles.Any(p => p.Id == _currentProfileId)
                    ? null
                    : _currentProfileId;

            var data = new ConnectionData
            {
                Profiles = _profiles,
                CurrentProfileId = persistedCurrentId
            };
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests`
Expected: 全數 PASS（含既有 ConnectionManager 測試）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Services/ConnectionManager.cs tests/Specurai.Infrastructure.Tests/Services/ConnectionManagerTemporaryProfileTests.cs
git commit -m "fix: 臨時連線可設為目前連線且不污染已落地設定"
```

---

### Task 9: Desktop — 同步後整批註冊為臨時連線

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs:238-286`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs`

**Interfaces:**
- Consumes: Task 8 的 `SetCurrentProfile` 已涵蓋臨時連線。

- [ ] **Step 1: 寫失敗測試**

在 `ConnectionSetupViewModelTests.cs` 加入（沿用該檔既有的 mock 建構樣式）：

```csharp
[Fact]
public async Task SyncExternalSourceAsync_同步成功_應整批註冊為臨時連線()
{
    var cm = Substitute.For<IConnectionManager>();
    cm.GetAllProfiles().Returns([]);
    var source = Substitute.For<IExternalConnectionSource>();
    var external = new[]
    {
        new ConnectionProfile { Name = "甲 正式", Server = "s1", Database = "d1" },
        new ConnectionProfile { Name = "乙 正式", Server = "s2", Database = "d2" }
    };
    source.SyncAsync().Returns(new ExternalConnectionResult(external, []));
    var settings = Substitute.For<IExternalSourceSettings>();
    settings.Load().Returns(new ExternalSourceConfig("dir", "key"));
    var vm = new ConnectionSetupViewModel(cm, source, settings);

    await vm.SyncExternalSourceCommand.ExecuteAsync(null);

    cm.Received(1).RegisterTemporaryProfiles(
        Arg.Is<IReadOnlyList<ConnectionProfile>>(list => list.Count == 2));
}

[Fact]
public void Connect_選取外部連線_應直接設為目前連線不重複註冊()
{
    var cm = Substitute.For<IConnectionManager>();
    cm.GetAllProfiles().Returns([]);
    var source = Substitute.For<IExternalConnectionSource>();
    var settings = Substitute.For<IExternalSourceSettings>();
    settings.Load().Returns(new ExternalSourceConfig("dir", "key"));
    var vm = new ConnectionSetupViewModel(cm, source, settings);
    var external = new ConnectionProfile { Name = "甲 正式", Server = "s1", Database = "d1" };
    vm.SelectedExternalProfile = external;

    vm.ConnectCommand.Execute(null);

    cm.DidNotReceive().RegisterTemporaryProfiles(Arg.Any<IReadOnlyList<ConnectionProfile>>());
    cm.Received(1).SetCurrentProfile(external.Id);
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~ConnectionSetupViewModelTests"`
Expected: FAIL（同步未註冊；Connect 仍會呼叫 RegisterTemporaryProfiles）。

- [ ] **Step 3: 實作**

`SyncExternalSourceAsync` 在 `ExternalProfiles` 填完之後、設定 `SyncStatusMessage` 之前加：

```csharp
            // 外部連線僅存活於本次執行：註冊為臨時連線（不落地），關閉 App 即消失
            _connectionManager?.RegisterTemporaryProfiles(newProfiles);
```

`Connect` 中的外部分支改為：

```csharp
        if (SelectedExternalProfile != null)
        {
            _connectionManager.SetCurrentProfile(SelectedExternalProfile.Id);
            return;
        }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests`
Expected: 全數 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/ConnectionSetupViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/ConnectionSetupViewModelTests.cs
git commit -m "feat: 外部同步結果整批註冊為臨時連線供主畫面選用"
```

---

### Task 10: 外部來源 DI 共用化 ＋ MCP 同步工具

**Files:**
- Modify: `src/Specurai.Infrastructure/ServiceRegistration.cs`（在 `AddSpecuraiCore` 內註冊外部來源）
- Modify: `src/Specurai.Desktop/Program.cs:61-63`（移除重複註冊）
- Modify: `src/Specurai.McpServer/Tools/ConnectionTools.cs`（新增工具）
- Test: `tests/Specurai.McpServer.Tests/ExternalSyncToolTests.cs`（新檔）

**Interfaces:**
- Consumes: `IExternalConnectionSource.SyncAsync()` 回傳 `ExternalConnectionResult(Profiles, FailedItems)`；Task 8 的臨時連線支援。
- Produces: MCP 工具 `sync_external_connections`。

- [ ] **Step 1: 寫失敗測試**

新檔 `tests/Specurai.McpServer.Tests/ExternalSyncToolTests.cs`：

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ExternalSyncToolTests
{
    [Fact]
    public async Task SyncExternalConnections_同步成功_應註冊臨時連線並回報筆數()
    {
        var cm = Substitute.For<IConnectionManager>();
        var source = Substitute.For<IExternalConnectionSource>();
        var profiles = new[]
        {
            new ConnectionProfile { Name = "甲 正式", Server = "s1", Database = "d1" },
            new ConnectionProfile { Name = "乙 正式", Server = "s2", Database = "d2" }
        };
        source.SyncAsync().Returns(new ExternalConnectionResult(profiles, ["丙/production"]));

        var result = await ConnectionTools.SyncExternalConnections(cm, source);

        cm.Received(1).RegisterTemporaryProfiles(
            Arg.Is<IReadOnlyList<ConnectionProfile>>(list => list.Count == 2));
        result.Should().Contain("2").And.Contain("1");
    }

    [Fact]
    public async Task SyncExternalConnections_來源未設定_回傳未取得任何連線()
    {
        var cm = Substitute.For<IConnectionManager>();
        var source = Substitute.For<IExternalConnectionSource>();
        source.SyncAsync().Returns(new ExternalConnectionResult([], []));

        var result = await ConnectionTools.SyncExternalConnections(cm, source);

        result.Should().Be("未取得任何外部連線，請確認外部來源目錄設定。");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests --filter "FullyQualifiedName~ExternalSyncToolTests"`
Expected: 編譯失敗（`SyncExternalConnections` 不存在）。

- [ ] **Step 3: 實作**

3a. `ServiceRegistration.AddSpecuraiCore` 在「Infrastructure - 連線管理器」區塊之後加：

```csharp
        // Infrastructure - 外部連線來源（三端共用：Desktop / Cli / McpServer）
        services.AddSingleton<IExternalSourceSettings, ExternalSourceSettings>();
        services.AddSingleton<IExternalConnectionSource, InventoryConnectionSource>();
```

3b. `src/Specurai.Desktop/Program.cs` 移除這兩行重複註冊（連同其上方的
`// Infrastructure - External Source` 註解）：

```csharp
        services.AddSingleton<IExternalSourceSettings, ExternalSourceSettings>();
        services.AddSingleton<IExternalConnectionSource, InventoryConnectionSource>();
```

3c. `ConnectionTools.cs` 新增工具（沿用該檔既有的 `[McpServerTool, Description(...)]` 樣式）：

```csharp
    [McpServerTool, Description("同步外部來源連線（僅存活於本次 server 執行，不寫入設定檔）")]
    public static async Task<string> SyncExternalConnections(
        IConnectionManager connectionManager,
        IExternalConnectionSource externalConnectionSource)
    {
        try
        {
            var result = await externalConnectionSource.SyncAsync();
            if (result.Profiles.Count == 0)
                return "未取得任何外部連線，請確認外部來源目錄設定。";

            connectionManager.RegisterTemporaryProfiles(result.Profiles);

            var message = $"已同步 {result.Profiles.Count} 個外部連線（僅本次執行有效）";
            if (result.FailedItems.Count > 0)
                message += $"，{result.FailedItems.Count} 個失敗：{string.Join("、", result.FailedItems)}";
            return message + "。";
        }
        catch (Exception ex)
        {
            return $"同步外部連線失敗：{ex.Message}";
        }
    }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet build && dotnet test tests/Specurai.McpServer.Tests tests/Specurai.Desktop.Tests`
Expected: 建置成功、全數 PASS（`DiResolutionSmokeTests` 亦須通過）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/ServiceRegistration.cs src/Specurai.Desktop/Program.cs src/Specurai.McpServer/Tools/ConnectionTools.cs tests/Specurai.McpServer.Tests/ExternalSyncToolTests.cs
git commit -m "feat: 新增 MCP 外部來源同步工具並共用外部來源註冊"
```

---

### Task 11: 移除已落地的外部連線（一次性資料修復，不入版控）

**Files:**
- 操作對象：`%APPDATA%\Specurai\connections.json`

- [ ] **Step 1: 備份**

```bash
cp "$APPDATA/Specurai/connections.json" "$APPDATA/Specurai/connections.backup-20260807-b.json"
```

- [ ] **Step 2: 刪除 `IsExternal` 為 true 的連線**

```python
import json, os

path = os.path.expandvars(r'%APPDATA%\Specurai\connections.json')
data = json.load(open(path, encoding='utf-8'))
kept = [p for p in data['Profiles'] if not p.get('IsExternal')]
removed = len(data['Profiles']) - len(kept)
data['Profiles'] = kept
if data.get('CurrentProfileId') and not any(p['Id'] == data['CurrentProfileId'] for p in kept):
    data['CurrentProfileId'] = None
with open(path, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
print(f'移除外部連線 {removed} 筆，剩餘 {len(kept)} 筆')
```

Expected: 移除 12 筆，剩餘 22 筆。

- [ ] **Step 3: 驗證**

確認剩餘連線皆為自建、`CurrentProfileId` 仍指向存在的連線。

---

### Task 7: 全方案驗證

- [ ] **Step 1: 建置與全測試**

Run: `dotnet build && dotnet test`
Expected: 建置成功、全部測試 PASS。

- [ ] **Step 2: 程式碼審查**

依 CLAUDE.md 規範使用 `superpowers:requesting-code-review` 審查本次變更。
