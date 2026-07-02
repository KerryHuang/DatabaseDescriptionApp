# 維護計劃頁平台自動偵測 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 維護計劃頁開啟時，依目前連線伺服器的實際平台（`host_platform`）自動帶入「平台」下拉，使用者仍可手動覆寫。

**Architecture:** `IBackupService` 新增 `GetServerPlatformAsync`（Infrastructure 查 `sys.dm_os_host_info`）。維護計劃 ViewModel 建構時 fire-and-forget 呼叫可 await 的 `DetectServerPlatformAsync()`，成功則設 `SelectedPlatform`（觸發既有路徑自動填入）。

**Tech Stack:** .NET 8、Avalonia 11、CommunityToolkit.Mvvm、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- UI 文字、程式碼註解、Commit 訊息一律使用繁體中文。
- Clean Architecture：平台查詢邏輯集中於服務層，ViewModel 僅呼叫。
- 偵測失敗 / 無目前連線 / 非 Windows/Linux → 維持現有預設 `Windows`，不崩潰。
- 偵測只設初始預設值，使用者仍可手動改下拉。
- 檔案存 UTF-8 無 BOM。TDD：先寫失敗測試再實作；頻繁 commit。

---

### Task 1: `IBackupService.GetServerPlatformAsync` 與實作

**Files:**
- Modify: `src/Specurai.Domain/Interfaces/IBackupService.cs`（在 `GetServerDefaultBackupPathAsync` 之後、介面結尾 `}` 之前新增方法宣告）
- Modify: `src/Specurai.Infrastructure/Services/MssqlBackupService.cs`（在「伺服器磁碟與目錄查詢」region 內新增實作）

**Interfaces:**
- Produces: `IBackupService.GetServerPlatformAsync(string connectionString, CancellationToken ct = default) : Task<string?>` — 回傳 `"Windows"`/`"Linux"`/`"其他"`，取不到為 `null`

> 說明：SQL 需真實伺服器，故本任務以「編譯通過 + 既有測試無回歸」驗收，不新增單元測試（與 `MssqlBackupService` 既有查詢一致）。

- [ ] **Step 1: 在 `IBackupService` 新增方法宣告**

在 `src/Specurai.Domain/Interfaces/IBackupService.cs` 的 `GetServerDefaultBackupPathAsync(...)` 宣告（結尾 `CancellationToken cancellationToken = default);`）之後、介面的結尾 `}` 之前，插入：

```csharp

    /// <summary>
    /// 偵測伺服器作業系統平台，回傳 "Windows"/"Linux"/"其他"；取不到時為 null
    /// </summary>
    Task<string?> GetServerPlatformAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 2: 執行建置確認失敗（介面未實作）**

Run: `dotnet build src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: 失敗，`MssqlBackupService` 未實作 `GetServerPlatformAsync`。

- [ ] **Step 3: 在 `MssqlBackupService` 新增實作**

在 `src/Specurai.Infrastructure/Services/MssqlBackupService.cs` 的「伺服器磁碟與目錄查詢」region 內，於 `GetServerDefaultBackupPathAsync` 方法（結尾 `return result is null || result == DBNull.Value ? null : result.ToString();` 之後的 `}`）之後、region 的 `#endregion` 之前，插入：

```csharp

    /// <inheritdoc />
    public async Task<string?> GetServerPlatformAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand("SELECT host_platform FROM sys.dm_os_host_info", connection);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var raw = result?.ToString();
            if (string.IsNullOrEmpty(raw)) return null;
            return raw switch
            {
                "Windows" => "Windows",
                "Linux" => "Linux",
                _ => "其他"
            };
        }
        catch
        {
            return null;
        }
    }
```

- [ ] **Step 4: 執行建置確認通過**

Run: `dotnet build src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: Build succeeded。

- [ ] **Step 5: 執行既有測試確認無回歸**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 全部通過。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Interfaces/IBackupService.cs src/Specurai.Infrastructure/Services/MssqlBackupService.cs
git commit -m "feat: IBackupService 新增 GetServerPlatformAsync（host_platform 偵測）"
```

---

### Task 2: 維護計劃 ViewModel 開啟時偵測平台

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelPlatformTests.cs`（新檔）

**Interfaces:**
- Consumes: `IBackupService.GetServerPlatformAsync`（Task 1）；既有 `SelectedPlatform`、`PlatformOptions`、`OnSelectedPlatformChanged`（自動填入平台預設路徑）
- Produces: `MaintenancePlanDocumentViewModel.DetectServerPlatformAsync() : Task`（public，可 await）

- [ ] **Step 1: 寫失敗測試**

Create `tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelPlatformTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelPlatformTests
{
    private static ConnectionProfile Profile() => new()
    {
        Id = Guid.NewGuid(),
        Name = "測試連線",
        Server = "localhost",
        Database = "TestDb"
    };

    private static MaintenancePlanDocumentViewModel Build(IConnectionManager conn, IBackupService backup)
    {
        var job = Substitute.For<IAgentJobService>();
        var plan = Substitute.For<IMaintenancePlanService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task DetectServerPlatform_回傳Linux_設定平台與Linux預設路徑()
    {
        var profile = Profile();
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("cs");
        var backup = Substitute.For<IBackupService>();
        backup.GetServerPlatformAsync("cs", Arg.Any<CancellationToken>()).Returns("Linux");

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Linux");
        vm.BackupPath.Should().Be("/var/opt/mssql/backup/");
        vm.RestorePath.Should().Be("/var/opt/mssql/data/");
    }

    [Fact]
    public async Task DetectServerPlatform_回傳null_維持Windows預設()
    {
        var profile = Profile();
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("cs");
        var backup = Substitute.For<IBackupService>();
        backup.GetServerPlatformAsync("cs", Arg.Any<CancellationToken>()).Returns((string?)null);

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Windows");
    }

    [Fact]
    public async Task DetectServerPlatform_無目前連線_維持預設且不丟例外()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var backup = Substitute.For<IBackupService>();

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Windows");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelPlatformTests"`
Expected: 編譯失敗（`DetectServerPlatformAsync` 不存在）。

- [ ] **Step 3: 新增 `DetectServerPlatformAsync` 方法**

在 `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs` 的「伺服器路徑瀏覽」region（前一功能新增的、含 `BrowsePathAsync` 的區塊）內、其 `#endregion` 之前，新增：

```csharp

    /// <summary>偵測目前連線伺服器平台，成功則帶入「平台」下拉（使用者仍可覆寫）</summary>
    public async Task DetectServerPlatformAsync()
    {
        if (_backupService == null || _connectionManager == null) return;

        var profile = _connectionManager.GetCurrentProfile();
        if (profile == null) return;

        var connectionString = _connectionManager.GetConnectionString(profile.Id);
        if (string.IsNullOrEmpty(connectionString)) return;

        try
        {
            var platform = await _backupService.GetServerPlatformAsync(connectionString);
            if (!string.IsNullOrEmpty(platform) && PlatformOptions.Contains(platform))
                SelectedPlatform = platform;
        }
        catch
        {
            // 偵測失敗維持預設平台
        }
    }
```

> 說明：設 `SelectedPlatform` 直接指派（不透過 `Dispatcher.UIThread.Post`），沿用本專案既有 `GenerateDefaultBackupPath` 於 `await` 後直接設定可觀察屬性的模式——app 內 async 接續回到 UI 執行緒（Avalonia 同步內容），單元測試亦可直接 await 驗證。此為 spec §3.3 的等效精修。

- [ ] **Step 4: 建構時 fire-and-forget 呼叫偵測**

在同檔 DI 建構函式尾端的 `_ = LoadJobsAsync();`（於 `// 進入頁面時自動載入 Job 清單` 註解下方）之後，新增一行：

```csharp
        _ = DetectServerPlatformAsync();
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelPlatformTests"`
Expected: PASS（3/3）。

- [ ] **Step 6: 執行 Desktop 全部測試確認無回歸**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 全部通過。

> 若 Desktop DLL 被執行中的桌面程式鎖定，先 `taskkill //F //IM Specurai.Desktop.exe`（Git Bash）再重試。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs tests/Specurai.Desktop.Tests/MaintenancePlanDocumentViewModelPlatformTests.cs
git commit -m "feat: 維護計劃頁開啟時依實際伺服器平台自動帶入下拉"
```

---

## 完成後

- [ ] 執行 `superpowers:requesting-code-review` 進行程式碼審查（專案憲章要求）。
- [ ] 全部測試綠燈後回報完成。

## Self-Review 對照（spec → task）

| Spec 需求 | 對應 Task |
|-----------|-----------|
| §3.1 IBackupService.GetServerPlatformAsync | Task 1 |
| §3.2 MssqlBackupService 實作 + host_platform 對應 + 失敗回 null | Task 1 |
| §3.3 DetectServerPlatformAsync + 建構時呼叫 + 設 SelectedPlatform | Task 2 |
| §3.4 只設初始值、相同值無副作用（OnSelectedPlatformChanged 僅值變更時觸發）| Task 2 |
| §4 錯誤處理（無連線/查詢失敗/其他值）| Task 1（回 null / 其他）＋ Task 2（早退 + try/catch）|
| §5 測試（Linux / null / 無連線）| Task 2 |

> 註（spec 精修）：spec §3.3 原述以 `Dispatcher.UIThread.Post` 設定；實作改為 `await` 後直接指派，沿用既有 `GenerateDefaultBackupPath` 模式，行為等效且可單元測試。
