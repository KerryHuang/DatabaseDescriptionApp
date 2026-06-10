# B1 · CLI 連線對齊（conn update / conn export）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 `Specurai.Cli` 補上 `conn update`（編輯既有連線）與 `conn export`（匯出連線為 JSON），鏡像 MCP 既有 `update_connection` / `export_connections` 的行為。

**Architecture:** 純展示層接線。於 `ConnCommand` 新增兩個子命令；可測邏輯抽成 internal static 方法（`ApplyProfileUpdates`、`ExportProfilesToFile`），沿用既有測試慣例（測純邏輯、不測依賴 `Program.Services` 的 handler）。服務 `IConnectionManager`、`IConnectionExportService` 皆已由 `AddSpecuraiCore()` 註冊，無需改 DI。

**Tech Stack:** .NET 8、System.CommandLine、Spectre.Console、xUnit + NSubstitute + FluentAssertions。

---

## File Structure

- Modify: `src/Specurai.Cli/Commands/ConnCommand.cs` — 新增 `CreateUpdateCommand()`、`CreateExportCommand()`、internal static 邏輯方法，並在 `Create()` 註冊兩個子命令。
- Create: `tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs` — 新邏輯的單元測試。

參考鏡像來源：`src/Specurai.McpServer/Tools/ConnectionCrudTools.cs`（`UpdateConnection`、`ExportConnections`）。

---

## Task 1: `conn update` 的更新邏輯（ApplyProfileUpdates）

**Files:**
- Modify: `src/Specurai.Cli/Commands/ConnCommand.cs`
- Test: `tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs`：

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Cli.Commands;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Tests;

public class ConnCommandUpdateExportTests
{
    private static ConnectionProfile NewProfile() => new()
    {
        Name = "原名",
        Server = "old-server",
        Database = "old-db",
        AuthType = AuthenticationType.WindowsAuthentication,
        Username = "old-user",
        Password = "old-pass"
    };

    [Fact(DisplayName = "ApplyProfileUpdates: 只提供 server 應只更新 server")]
    public void ApplyProfileUpdates_OnlyServerProvided_ShouldUpdateServerOnly()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile, newServer: "new-server");

        profile.Server.Should().Be("new-server");
        profile.Name.Should().Be("原名");
        profile.Database.Should().Be("old-db");
        profile.Username.Should().Be("old-user");
    }

    [Fact(DisplayName = "ApplyProfileUpdates: 全部為 null 應保持不變")]
    public void ApplyProfileUpdates_AllNull_ShouldLeaveUnchanged()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile);

        profile.Name.Should().Be("原名");
        profile.Server.Should().Be("old-server");
        profile.Database.Should().Be("old-db");
        profile.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
        profile.Username.Should().Be("old-user");
        profile.Password.Should().Be("old-pass");
    }

    [Fact(DisplayName = "ApplyProfileUpdates: auth=SqlServer 應設為 SQL 認證")]
    public void ApplyProfileUpdates_AuthSqlServer_ShouldSetSqlAuth()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile, newAuthType: "SqlServer");

        profile.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
    }

    [Fact(DisplayName = "ApplyProfileUpdates: auth 非 SqlServer 應設為 Windows 認證")]
    public void ApplyProfileUpdates_AuthOther_ShouldSetWindowsAuth()
    {
        var profile = NewProfile();
        profile.AuthType = AuthenticationType.SqlServerAuthentication;

        ConnCommand.ApplyProfileUpdates(profile, newAuthType: "Windows");

        profile.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnCommandUpdateExportTests"`
Expected: 編譯失敗或 FAIL（`ApplyProfileUpdates` 不存在）。

- [ ] **Step 3: 實作 ApplyProfileUpdates**

在 `src/Specurai.Cli/Commands/ConnCommand.cs` 的 `ConnCommand` class 內（例如 `ParseImportJson` 之前）新增：

```csharp
/// <summary>
/// 將非 null 的新值套用到既有連線設定（鏡像 MCP UpdateConnection 行為）。
/// </summary>
internal static ConnectionProfile ApplyProfileUpdates(
    ConnectionProfile profile,
    string? newName = null,
    string? newServer = null,
    string? newDatabase = null,
    string? newAuthType = null,
    string? newUsername = null,
    string? newPassword = null)
{
    if (newName != null) profile.Name = newName;
    if (newServer != null) profile.Server = newServer;
    if (newDatabase != null) profile.Database = newDatabase;
    if (newAuthType != null)
        profile.AuthType = newAuthType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? AuthenticationType.SqlServerAuthentication
            : AuthenticationType.WindowsAuthentication;
    if (newUsername != null) profile.Username = newUsername;
    if (newPassword != null) profile.Password = newPassword;
    return profile;
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnCommandUpdateExportTests"`
Expected: 4 個測試 PASS。

- [ ] **Step 5: 接上 `conn update` 子命令**

在 `ConnCommand` class 內新增子命令方法：

```csharp
private static Command CreateUpdateCommand()
{
    var nameArg = new Argument<string>("name", "要更新的連線名稱");
    var newNameOpt = new Option<string?>("--new-name", "新名稱");
    var serverOpt = new Option<string?>("--server", "新伺服器位址");
    var databaseOpt = new Option<string?>("--database", "新資料庫名稱");
    var authOpt = new Option<string?>("--auth", "新認證方式（Windows 或 SqlServer）");
    var userOpt = new Option<string?>("--user", "新 SQL 帳號");
    var passwordOpt = new Option<string?>("--password", "新 SQL 密碼");

    var command = new Command("update", "更新既有連線") { nameArg };
    command.AddOption(newNameOpt);
    command.AddOption(serverOpt);
    command.AddOption(databaseOpt);
    command.AddOption(authOpt);
    command.AddOption(userOpt);
    command.AddOption(passwordOpt);

    command.SetHandler((context) =>
    {
        var name = context.ParseResult.GetValueForArgument(nameArg);
        var newName = context.ParseResult.GetValueForOption(newNameOpt);
        var server = context.ParseResult.GetValueForOption(serverOpt);
        var database = context.ParseResult.GetValueForOption(databaseOpt);
        var auth = context.ParseResult.GetValueForOption(authOpt);
        var user = context.ParseResult.GetValueForOption(userOpt);
        var password = context.ParseResult.GetValueForOption(passwordOpt);

        var cm = Program.Services.GetRequiredService<IConnectionManager>();
        var profile = cm.GetAllProfiles()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            CliOutput.Error($"找不到連線「{name}」");
            Environment.ExitCode = 1;
            return;
        }

        ApplyProfileUpdates(profile, newName, server, database, auth, user, password);
        cm.UpdateProfile(profile);

        if (CliOutput.JsonMode)
            CliOutput.Success(new { profile.Id, profile.Name, Message = "連線已更新" });
        else
            CliOutput.SuccessMessage($"已更新連線「{profile.Name}」");
    });

    return command;
}
```

> 註：此處使用 `SetHandler(Action<InvocationContext>)` 多參數版以避免 7 個以上委派多載限制；需 `using System.CommandLine.Invocation;`（若檔案尚未引入則補上）。

在 `Create()` 內 `CreateImportCommand()` 那行之後加入註冊：

```csharp
command.AddCommand(CreateUpdateCommand());
```

- [ ] **Step 6: 建置整個方案確認無誤**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Cli/Commands/ConnCommand.cs tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs
git commit -m "feat(cli): conn update 子命令對齊 MCP update_connection"
```

---

## Task 2: `conn export` 的匯出邏輯（ExportProfilesToFile）

**Files:**
- Modify: `src/Specurai.Cli/Commands/ConnCommand.cs`
- Test: `tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs`

- [ ] **Step 1: 寫失敗測試**

在 `ConnCommandUpdateExportTests` class 內新增：

```csharp
[Fact(DisplayName = "ExportProfilesToFile: 應將服務輸出的位元組寫入指定路徑")]
public void ExportProfilesToFile_ShouldWriteServiceBytesToPath()
{
    var profiles = new List<ConnectionProfile> { NewProfile() };
    var exportService = Substitute.For<IConnectionExportService>();
    exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
        .Returns(new byte[] { 1, 2, 3 });
    var path = Path.Combine(Path.GetTempPath(), $"specurai-export-test-{Guid.NewGuid():N}.json");

    try
    {
        var count = ConnCommand.ExportProfilesToFile(exportService, profiles, path, includePasswords: false);

        count.Should().Be(1);
        File.ReadAllBytes(path).Should().Equal(1, 2, 3);
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

[Fact(DisplayName = "ExportProfilesToFile: 應將 includePasswords 旗標傳給服務")]
public void ExportProfilesToFile_ShouldPassIncludePasswordsFlag()
{
    var profiles = new List<ConnectionProfile> { NewProfile() };
    var exportService = Substitute.For<IConnectionExportService>();
    exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
        .Returns(new byte[] { 9 });
    var path = Path.Combine(Path.GetTempPath(), $"specurai-export-test-{Guid.NewGuid():N}.json");

    try
    {
        ConnCommand.ExportProfilesToFile(exportService, profiles, path, includePasswords: true);

        exportService.Received(1).ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), true);
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnCommandUpdateExportTests"`
Expected: 編譯失敗或 FAIL（`ExportProfilesToFile` 不存在）。

- [ ] **Step 3: 實作 ExportProfilesToFile**

在 `ConnCommand` class 內（`ApplyProfileUpdates` 之後）新增：

```csharp
/// <summary>
/// 將連線設定透過匯出服務序列化並寫入檔案，回傳匯出筆數（鏡像 MCP ExportConnections 行為）。
/// </summary>
internal static int ExportProfilesToFile(
    IConnectionExportService exportService,
    IReadOnlyList<ConnectionProfile> profiles,
    string outputPath,
    bool includePasswords)
{
    var data = exportService.ExportToJson(profiles, includePasswords);
    File.WriteAllBytes(outputPath, data);
    return profiles.Count;
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj --filter "FullyQualifiedName~ConnCommandUpdateExportTests"`
Expected: 全部測試 PASS（含 Task 1 共 6 個）。

- [ ] **Step 5: 接上 `conn export` 子命令**

在 `ConnCommand` class 內新增：

```csharp
private static Command CreateExportCommand()
{
    var outputOpt = new Option<string>(["--output", "-o"], "輸出檔案路徑") { IsRequired = true };
    var includePwdOpt = new Option<bool>("--include-passwords", "匯出時包含密碼（預設 false）");

    var command = new Command("export", "匯出連線設定為 JSON") { outputOpt, includePwdOpt };

    command.SetHandler((output, includePasswords) =>
    {
        var cm = Program.Services.GetRequiredService<IConnectionManager>();
        var exportService = Program.Services.GetRequiredService<IConnectionExportService>();

        var profiles = cm.GetAllProfiles();
        if (profiles.Count == 0)
        {
            CliOutput.Error("沒有連線設定可匯出。");
            Environment.ExitCode = 1;
            return;
        }

        var count = ExportProfilesToFile(exportService, profiles, output, includePasswords);

        if (CliOutput.JsonMode)
            CliOutput.Success(new { Output = output, Count = count });
        else
            CliOutput.SuccessMessage($"已匯出 {count} 個連線設定至 {output}");
    }, outputOpt, includePwdOpt);

    return command;
}
```

在 `Create()` 內 `CreateUpdateCommand()` 註冊那行之後加入：

```csharp
command.AddCommand(CreateExportCommand());
```

- [ ] **Step 6: 建置確認無誤**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Cli/Commands/ConnCommand.cs tests/Specurai.Cli.Tests/ConnCommandUpdateExportTests.cs
git commit -m "feat(cli): conn export 子命令對齊 MCP export_connections"
```

---

## Task 3: 整批驗證與審查

**Files:** 無（驗證用）。

- [ ] **Step 1: 全測試綠燈**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj`
Expected: 全部 PASS，0 fail。

- [ ] **Step 2: 命令樹確認**

Run: `dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- conn --help`
Expected: 子命令清單中出現 `update` 與 `export`。

- [ ] **Step 3: 煙霧測試 export（不需資料庫）**

Run: `dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- conn export -o specurai-conn-export.json`
Expected：若已有連線設定 → 顯示「已匯出 N 個連線設定至 ...」並產生檔案；若無連線 → 顯示「沒有連線設定可匯出。」。事後刪除測試檔。

- [ ] **Step 4: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本批變更（`ConnCommand.cs`、`ConnCommandUpdateExportTests.cs`），通過後回報，再進入 B2。

---

## Self-Review 紀錄

- **Spec 覆蓋**：B1 範圍（`conn update`、`conn export`）皆有對應 Task。✅
- **Placeholder 掃描**：無 TBD/TODO，所有程式碼步驟均含完整程式碼。✅
- **型別一致性**：`ApplyProfileUpdates` / `ExportProfilesToFile` 簽章在測試與實作一致；`IConnectionExportService.ExportToJson(IReadOnlyList<ConnectionProfile>, bool)` 與介面定義相符；`IConnectionManager.UpdateProfile/GetAllProfiles` 與介面相符。✅
- **環境欄位**：`conn export` 的環境欄位由 `ConnectionExportService` 內部處理（對齊 2026-06-09 修正），CLI 無需額外處理；`conn update` 比照 MCP `UpdateConnection` 不含 environment 參數（YAGNI）。
