# MCP confirm 閘門 + DI Smoke Test 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 4 個破壞性 MCP 工具加 `confirm` 閘門（預設回摘要、`confirm:true` 才執行），並新增 `Specurai.McpServer.Tests` 專案承載 DI 解析 smoke test 與 confirm 行為單元測試。

**Architecture:** 在 `set_recovery_model`、`restore_run`、`migration_apply`、`migration_log_resize` 各加最後一個參數 `bool confirm = false`；前置解析/驗證共用，於呼叫破壞性服務前 `if (!confirm) return 摘要;`。新測試專案以反射檢查所有 `[McpServerTool]` 方法的 `Specurai.*` 介面參數皆可由 `AddSpecuraiCore()` 解析。

**Tech Stack:** .NET 8、ModelContextProtocol SDK、xUnit + NSubstitute + FluentAssertions、Microsoft.Extensions.DependencyInjection。

---

## File Structure

- Create: `tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
- Create: `tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs`
- Create: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`
- Modify: `Specurai.slnx`（登錄測試專案）
- Modify: `src/Specurai.McpServer/Tools/RecoveryModelTools.cs`（set 加 confirm）
- Modify: `src/Specurai.McpServer/Tools/RestoreTools.cs`（restore_run 加 confirm）
- Modify: `src/Specurai.McpServer/Tools/MigrationTools.cs`（apply / log_resize 加 confirm）

---

## Task 1: 建立 McpServer.Tests 專案 + DI smoke test

**Files:**
- Create: `tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
- Create: `tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs`
- Modify: `Specurai.slnx`

- [ ] **Step 1: 建立測試專案 csproj**

`tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="8.8.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Specurai.McpServer\Specurai.McpServer.csproj" />
    <ProjectReference Include="..\..\src\Specurai.Infrastructure\Specurai.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 登錄到 Specurai.slnx**

在 `Specurai.slnx` 的 `<Folder Name="/tests/">` 內，`Specurai.Cli.Tests` 那行之後加入：

```xml
    <Project Path="tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj" />
```

- [ ] **Step 3: 寫 DI smoke test**

`tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs`：

```csharp
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Infrastructure;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class DiResolutionSmokeTests
{
    private static bool HasAttribute(MemberInfo member, string attributeName) =>
        member.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().Name == attributeName);

    [Fact(DisplayName = "所有 MCP 工具注入的 Specurai 服務都應能由 AddSpecuraiCore 解析")]
    public void AllMcpToolInjectedServices_ShouldBeResolvable()
    {
        using var provider = new ServiceCollection().AddSpecuraiCore().BuildServiceProvider();
        var assembly = typeof(BackupTools).Assembly;
        var missing = new List<string>();

        var toolTypes = assembly.GetTypes()
            .Where(t => HasAttribute(t, "McpServerToolTypeAttribute"));

        foreach (var type in toolTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => HasAttribute(m, "McpServerToolAttribute"));

            foreach (var method in methods)
            {
                foreach (var p in method.GetParameters())
                {
                    var pt = p.ParameterType;
                    if (pt.IsInterface && pt.Namespace?.StartsWith("Specurai") == true)
                    {
                        if (provider.GetService(pt) == null)
                            missing.Add($"{type.Name}.{method.Name}({pt.Name})");
                    }
                }
            }
        }

        missing.Should().BeEmpty(
            "以下 MCP 工具注入的服務無法解析：\n" + string.Join("\n", missing));
    }
}
```

- [ ] **Step 4: 執行 smoke test（應通過，因 B5 已修 DI）**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~DiResolutionSmokeTests"`
Expected: PASS（驗證：若還原 B5 的 ServiceRegistration 修正則此測試會失敗，列出 `RecoveryModelTools.*`）。

- [ ] **Step 5: Commit**

```bash
git add tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs Specurai.slnx
git commit -m "test(mcp): 新增 McpServer.Tests 與 DI 解析 smoke test"
```

---

## Task 2: `set_recovery_model` confirm 閘門

**Files:**
- Modify: `src/Specurai.McpServer/Tools/RecoveryModelTools.cs`
- Test: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`：

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConfirmGateTests
{
    [Fact(DisplayName = "set_recovery_model: confirm=false 不應呼叫服務並回摘要")]
    public async Task SetRecoveryModel_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var service = Substitute.For<IDatabaseRecoveryModelService>();

        var result = await RecoveryModelTools.SetRecoveryModel(service, "DBA", "simple", confirm: false);

        await service.DidNotReceive().SaveChangesAsync(
            Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
        result.Should().Contain("SIMPLE");
    }

    [Fact(DisplayName = "set_recovery_model: confirm=true 應呼叫服務")]
    public async Task SetRecoveryModel_ConfirmTrue_ShouldExecute()
    {
        var service = Substitute.For<IDatabaseRecoveryModelService>();

        var result = await RecoveryModelTools.SetRecoveryModel(service, "DBA", "simple", confirm: true);

        await service.Received(1).SaveChangesAsync(
            Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("已設定");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~SetRecoveryModel"`
Expected: 編譯失敗（`SetRecoveryModel` 尚無 `confirm` 參數）。

- [ ] **Step 3: 加 confirm 閘門**

將 `RecoveryModelTools.SetRecoveryModel` 改為：

```csharp
    [McpServerTool, Description("設定指定資料庫的 Recovery Model（⚠️ 變更資料庫設定；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static async Task<string> SetRecoveryModel(
        IDatabaseRecoveryModelService service,
        [Description("資料庫名稱")] string database,
        [Description("Recovery Model：FULL / SIMPLE / BULK_LOGGED")] string model,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            var normalized = model.ToUpperInvariant().Replace("-", "_");
            if (normalized is not ("FULL" or "SIMPLE" or "BULK_LOGGED"))
                return "Model 必須為 FULL / SIMPLE / BULK_LOGGED。";

            if (!confirm)
                return $"將把 [{database}] 的 Recovery Model 設為 {normalized}。加 confirm:true 執行。";

            await service.SaveChangesAsync(new[] { (database, normalized) });
            return $"已設定 [{database}] 的 Recovery Model = {normalized}。";
        }
        catch (Exception ex)
        {
            return $"設定 Recovery Model 失敗：{ex.Message}";
        }
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~SetRecoveryModel"`
Expected: 2 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/RecoveryModelTools.cs tests/Specurai.McpServer.Tests/ConfirmGateTests.cs
git commit -m "feat(mcp): set_recovery_model 加 confirm 閘門"
```

---

## Task 3: `restore_run` confirm 閘門

**Files:**
- Modify: `src/Specurai.McpServer/Tools/RestoreTools.cs`
- Test: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`

- [ ] **Step 1: 加失敗測試**

在 `ConfirmGateTests` 內新增（檔頂補 `using Specurai.Domain.Entities;`、`using Specurai.Domain.Interfaces;`）：

```csharp
    private static ConnectionProfile SampleProfile() => new()
    {
        Name = "目前連線",
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p"
    };

    [Fact(DisplayName = "restore_run: confirm=false 不應還原並回摘要")]
    public async Task RestoreRun_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var backup = Substitute.For<IBackupService>();

        var result = await RestoreTools.RestoreRun(cm, backup, "/x.bak", "overwrite", confirm: false);

        await backup.DidNotReceive().RestoreDatabaseAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RestoreOptions>(),
            Arg.Any<IProgress<RestoreProgress>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
    }

    [Fact(DisplayName = "restore_run: confirm=true 應執行還原")]
    public async Task RestoreRun_ConfirmTrue_ShouldExecute()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var backup = Substitute.For<IBackupService>();

        var result = await RestoreTools.RestoreRun(cm, backup, "/x.bak", "overwrite", confirm: true);

        await backup.Received(1).RestoreDatabaseAsync(
            Arg.Any<string>(), "/x.bak", Arg.Any<RestoreOptions>(),
            Arg.Any<IProgress<RestoreProgress>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("還原完成");
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~RestoreRun"`
Expected: 編譯失敗（無 `confirm` 參數）。

- [ ] **Step 3: 加 confirm 閘門**

在 `RestoreTools.RestoreRun` 簽章末加 `[Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false`，描述加上「預設僅回摘要，需 confirm:true 才實際執行」，並在建立 `masterProfile` 之前（即實際 `RestoreDatabaseAsync` 之前、`options` 建好後）插入：

```csharp
            if (!confirm)
            {
                var overwriteNote = restoreMode == RestoreMode.OverwriteExisting
                    ? "；overwrite 會覆蓋現有資料庫，無法復原" : "";
                return $"將從 {path} 還原到 {target ?? profile.Database}（模式 {restoreMode}{overwriteNote}）。加 confirm:true 執行。";
            }
```

（放在 `var options = new RestoreOptions {...};` 之後、`var masterProfile = ...;` 之前。）

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~RestoreRun"`
Expected: 2 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/RestoreTools.cs tests/Specurai.McpServer.Tests/ConfirmGateTests.cs
git commit -m "feat(mcp): restore_run 加 confirm 閘門"
```

---

## Task 4: `migration_log_resize` confirm 閘門

**Files:**
- Modify: `src/Specurai.McpServer/Tools/MigrationTools.cs`
- Test: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`

- [ ] **Step 1: 加失敗測試**

在 `ConfirmGateTests` 內新增（檔頂補 `using Specurai.Domain.Enums;` 若未引入；`ResizeLogResult` 在 `Specurai.Application.Services` 命名空間，已隨 `using` 涵蓋）：

```csharp
    [Fact(DisplayName = "migration_log_resize: confirm=false 不應調整並回摘要")]
    public async Task MigrationLogResize_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { SampleProfile() with { Name = "Target" } });
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var executor = Substitute.For<ISchemaMigrationExecutor>();

        var result = await MigrationTools.MigrationLogResize(cm, executor, "Target", 1024, confirm: false);

        await executor.DidNotReceive().ResizeLogAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
        result.Should().Contain("1024");
    }

    [Fact(DisplayName = "migration_log_resize: confirm=true 應執行調整")]
    public async Task MigrationLogResize_ConfirmTrue_ShouldExecute()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { SampleProfile() with { Name = "Target" } });
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var executor = Substitute.For<ISchemaMigrationExecutor>();
        executor.ResizeLogAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ResizeLogResult { IsSuccess = true });

        await MigrationTools.MigrationLogResize(cm, executor, "Target", 1024, confirm: true);

        await executor.Received(1).ResizeLogAsync("conn", 1024, Arg.Any<CancellationToken>());
    }
```

> 註：`ConnectionProfile` 為 class，若不支援 `with` 運算式則改為直接 `new ConnectionProfile { Name = "Target", Server="srv", Database="AppDb", AuthType=AuthenticationType.SqlServerAuthentication }`。實作時依實際型別調整。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~MigrationLogResize"`
Expected: 編譯失敗（無 `confirm` 參數）。

- [ ] **Step 3: 加 confirm 閘門**

在 `MigrationTools.MigrationLogResize` 簽章末加 `[Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false`，描述加「預設僅回摘要，需 confirm:true 才實際執行」，並在範圍檢查之後、`BuildConnectionString` 之前插入：

```csharp
            if (!confirm)
                return $"將把 [{target}] 的 LDF 調整為 {sizeMb} MB。加 confirm:true 執行。";
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~MigrationLogResize"`
Expected: 2 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/MigrationTools.cs tests/Specurai.McpServer.Tests/ConfirmGateTests.cs
git commit -m "feat(mcp): migration_log_resize 加 confirm 閘門"
```

---

## Task 5: `migration_apply` confirm 閘門

**Files:** Modify `src/Specurai.McpServer/Tools/MigrationTools.cs`

> 說明：`migration_apply` 的 confirm 閘門結構與 Task 2~4 一致，但其前置需 analyze + generate，單元測試需構造完整 `MigrationAnalysis`/`SyncScript`（含 Comparison/BaseSchema/TargetSchema），mock 成本與型別構造過高，**不另寫單元測試**；以「與已測工具相同的 confirm 模式 + code review + DI smoke test」保證，並由 `migration_preview`（唯讀，已存在）提供等價的差異預覽能力。

- [ ] **Step 1: 加 confirm 閘門**

在 `MigrationTools.MigrationApply` 簽章末加 `[Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false`，描述加「預設僅回摘要（含將套用的差異數），需 confirm:true 才實際執行」，並把「產生 script 並檢查差異數」之後、`executor.ExecuteAsync(..., dryRun:false)` 之前改為：

```csharp
            var script = GenerateScript(scriptGenerator, analysis, includeHighRisk: false);
            if (script.Differences.Count == 0)
                return "沒有可執行的差異（高風險已排除）。";

            if (!confirm)
                return $"將對 {analysis.TargetSchema.ConnectionName} 套用 {script.Differences.Count} 項變更（高風險已排除）。加 confirm:true 執行。";

            var report = await executor.ExecuteAsync(script, targetConn, dryRun: false);
```

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.McpServer/Tools/MigrationTools.cs
git commit -m "feat(mcp): migration_apply 加 confirm 閘門（預設回差異摘要）"
```

---

## Task 6: 整體驗證與審查

- [ ] **Step 1: McpServer.Tests 全綠**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 全部 PASS（DI smoke 1 + confirm 6）。

- [ ] **Step 2: McpServer 建置綠燈**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded。

- [ ] **Step 3: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本批變更（4 工具的 confirm 閘門、新測試專案、smoke test），確認 confirm 前置共用、摘要訊息正確、smoke test 覆蓋完整。

---

## Self-Review 紀錄

- **Spec 覆蓋**：Part A（4 工具 confirm 閘門，backup_run 不含）→ Task 2~5；Part B（測試專案 + DI smoke）→ Task 1。✅
- **Placeholder 掃描**：無 TBD/TODO；每個程式碼步驟均含完整程式碼。✅
- **型別一致性**：`confirm = false` 參數一致；`SaveChangesAsync`/`RestoreDatabaseAsync`/`ResizeLogAsync` 簽章與 B4/B5 既有定義相符；smoke test 以屬性名稱字串比對避免相依 ModelContextProtocol attribute 型別可見性。✅
- **刻意取捨**：`migration_apply` 不寫 confirm 單元測試（mock 成本過高），以相同模式 + review + smoke 保證；`backup_run` 不加 confirm（非破壞性）。首次為 MCP 工具引入測試專案，屬合理演進。
- **驗證點**：DI smoke test 在 B5 ServiceRegistration 修正前會失敗——構成對該類 bug 的回歸防護。
