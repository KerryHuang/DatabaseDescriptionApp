# 備份頁磁碟空間提示與伺服器端資料夾瀏覽 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在備份頁顯示伺服器各磁碟的總量／可用空間表格，並提供 SSMS 式的伺服器端（xp_dirtree）資料夾瀏覽對話框選取備份路徑。

**Architecture:** 所有伺服器查詢集中於 `IBackupService`／`MssqlBackupService`（修正原本 ViewModel 內嵌 SQL 的分層違規）。Domain 新增純資料實體與跨平台路徑輔助類別。Desktop 新增磁碟空間卡片、`ServerFolderBrowserWindow` 對話框（TreeView 惰性載入），並改寫備份頁 ViewModel。

**Tech Stack:** .NET 8、Avalonia 11、CommunityToolkit.Mvvm、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- UI 文字、程式碼註解、Commit 訊息一律使用繁體中文。
- 遵守 Clean Architecture 分層：Domain 無外部相依；Infrastructure 不參考 Desktop；查詢邏輯不得寫在 ViewModel。
- 每個 ViewModel／對話框 ViewModel 必須同時提供「無參數設計時建構函式」與「DI／執行時建構函式」（CommunityToolkit.Mvvm）。
- 跨平台（Windows／Linux SQL Server）：路徑分隔字元由 `ServerPathHelper` 依實際路徑判定，不得硬編 `\`。
- 檔案存 UTF-8 無 BOM。
- TDD：先寫失敗測試再實作；頻繁 commit。
- 磁碟查詢／目錄瀏覽失敗一律不得中斷備份流程或使 App 崩潰。

---

### Task 1: Domain 實體與跨平台路徑輔助類別

**Files:**
- Create: `src/Specurai.Domain/Entities/ServerVolumeInfo.cs`
- Create: `src/Specurai.Domain/Entities/ServerDirectoryEntry.cs`
- Create: `src/Specurai.Domain/ServerPathHelper.cs`
- Test: `tests/Specurai.Domain.Tests/ServerVolumeInfoTests.cs`
- Test: `tests/Specurai.Domain.Tests/ServerPathHelperTests.cs`

**Interfaces:**
- Produces:
  - `ServerVolumeInfo { string Name; string? Label; long FreeBytes; long? TotalBytes; double? UsedPercent; double UsedPercentValue; bool IsLowSpace; string FormattedFree; string FormattedTotal; string UsedPercentText }`
  - `ServerDirectoryEntry { string Name; string FullPath; bool IsDirectory }`
  - `ServerPathHelper.Combine(string parent, string name) : string`
  - `ServerPathHelper.GetSeparator(string path) : char`
  - `ServerPathHelper.GetFileName(string path) : string`
  - `ServerPathHelper.IsBackupFile(string name) : bool`

- [ ] **Step 1: 寫失敗測試（實體計算屬性）**

Create `tests/Specurai.Domain.Tests/ServerVolumeInfoTests.cs`:

```csharp
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests;

public class ServerVolumeInfoTests
{
    [Fact]
    public void UsedPercent_有總量_回傳正確百分比()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 25, TotalBytes = 100 };
        v.UsedPercent.Should().BeApproximately(75, 0.001);
        v.UsedPercentValue.Should().BeApproximately(75, 0.001);
    }

    [Fact]
    public void UsedPercent_無總量_回傳null且值為0()
    {
        var v = new ServerVolumeInfo { Name = "D:\\", FreeBytes = 25, TotalBytes = null };
        v.UsedPercent.Should().BeNull();
        v.UsedPercentValue.Should().Be(0);
        v.FormattedTotal.Should().Be("—");
        v.UsedPercentText.Should().Be("—");
    }

    [Fact]
    public void IsLowSpace_可用低於一成_為真並於文字加註警示()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 5, TotalBytes = 100 };
        v.IsLowSpace.Should().BeTrue();
        v.UsedPercentText.Should().Contain("⚠");
    }

    [Fact]
    public void FormattedFree_大於1GB_以GB顯示()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 2L * 1024 * 1024 * 1024, TotalBytes = 10L * 1024 * 1024 * 1024 };
        v.FormattedFree.Should().Be("2.0 GB");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ServerVolumeInfoTests"`
Expected: 編譯失敗（`ServerVolumeInfo` 不存在）。

- [ ] **Step 3: 建立 `ServerVolumeInfo`**

Create `src/Specurai.Domain/Entities/ServerVolumeInfo.cs`:

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 伺服器磁碟區空間資訊
/// </summary>
public sealed class ServerVolumeInfo
{
    /// <summary>磁碟名稱或掛載點（例：C:\ 或 /var/opt/mssql）</summary>
    public required string Name { get; init; }

    /// <summary>磁碟區標籤（可空）</summary>
    public string? Label { get; init; }

    /// <summary>可用空間（bytes）</summary>
    public long FreeBytes { get; init; }

    /// <summary>總空間（bytes）；無法取得時為 null（例如無資料庫檔案的空碟）</summary>
    public long? TotalBytes { get; init; }

    /// <summary>使用率（百分比）；無總量時為 null</summary>
    public double? UsedPercent =>
        TotalBytes is > 0 ? (double)(TotalBytes.Value - FreeBytes) / TotalBytes.Value * 100 : null;

    /// <summary>供進度條綁定的使用率值（無總量時為 0）</summary>
    public double UsedPercentValue => UsedPercent ?? 0;

    /// <summary>可用空間是否偏低（可用 &lt; 總量的 10%）</summary>
    public bool IsLowSpace => TotalBytes is > 0 && FreeBytes < TotalBytes.Value * 0.10;

    /// <summary>格式化的可用空間</summary>
    public string FormattedFree => FormatBytes(FreeBytes);

    /// <summary>格式化的總空間（無法取得時顯示「—」）</summary>
    public string FormattedTotal => TotalBytes.HasValue ? FormatBytes(TotalBytes.Value) : "—";

    /// <summary>使用率文字（無總量時「—」；偏低時加註 ⚠）</summary>
    public string UsedPercentText =>
        UsedPercent is null ? "—" : $"{UsedPercent.Value:F0}%{(IsLowSpace ? " ⚠" : string.Empty)}";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
```

- [ ] **Step 4: 建立 `ServerDirectoryEntry`**

Create `src/Specurai.Domain/Entities/ServerDirectoryEntry.cs`:

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 伺服器端目錄項目（資料夾或備份檔）
/// </summary>
public sealed class ServerDirectoryEntry
{
    /// <summary>名稱（資料夾名或檔名）</summary>
    public required string Name { get; init; }

    /// <summary>完整伺服器端路徑</summary>
    public required string FullPath { get; init; }

    /// <summary>是否為資料夾</summary>
    public required bool IsDirectory { get; init; }
}
```

- [ ] **Step 5: 寫失敗測試（路徑輔助）**

Create `tests/Specurai.Domain.Tests/ServerPathHelperTests.cs`:

```csharp
using FluentAssertions;
using Specurai.Domain;
using Xunit;

namespace Specurai.Domain.Tests;

public class ServerPathHelperTests
{
    [Theory]
    [InlineData("C:\\", "a.bak", "C:\\a.bak")]
    [InlineData("D:\\SQLBackup", "a.bak", "D:\\SQLBackup\\a.bak")]
    [InlineData("/var/opt/mssql", "a.bak", "/var/opt/mssql/a.bak")]
    [InlineData("/var/opt/mssql/", "a.bak", "/var/opt/mssql/a.bak")]
    public void Combine_依平台分隔字元組合(string parent, string name, string expected)
    {
        ServerPathHelper.Combine(parent, name).Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Backup\\a.bak", "a.bak")]
    [InlineData("/var/opt/mssql/a.trn", "a.trn")]
    public void GetFileName_取最後一段(string path, string expected)
    {
        ServerPathHelper.GetFileName(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("a.bak", true)]
    [InlineData("A.BAK", true)]
    [InlineData("a.trn", true)]
    [InlineData("a.txt", false)]
    [InlineData("folder", false)]
    public void IsBackupFile_辨識副檔名(string name, bool expected)
    {
        ServerPathHelper.IsBackupFile(name).Should().Be(expected);
    }
}
```

- [ ] **Step 6: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ServerPathHelperTests"`
Expected: 編譯失敗（`ServerPathHelper` 不存在）。

- [ ] **Step 7: 建立 `ServerPathHelper`**

Create `src/Specurai.Domain/ServerPathHelper.cs`:

```csharp
namespace Specurai.Domain;

/// <summary>
/// 伺服器端路徑處理輔助方法（跨 Windows／Linux）
/// </summary>
public static class ServerPathHelper
{
    /// <summary>依父路徑判定分隔字元後組合子路徑。</summary>
    public static string Combine(string parent, string name)
    {
        var sep = GetSeparator(parent);
        var trimmed = parent.TrimEnd('\\', '/');
        return $"{trimmed}{sep}{name}";
    }

    /// <summary>判定路徑所屬平台的分隔字元（Windows 路徑用 '\\'，否則 '/'）。</summary>
    public static char GetSeparator(string path)
    {
        if (path.Contains('\\')) return '\\';
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') return '\\';
        return '/';
    }

    /// <summary>取路徑最後一段（檔名）。</summary>
    public static string GetFileName(string path)
    {
        var sep = GetSeparator(path);
        var idx = path.LastIndexOf(sep);
        return idx < 0 ? path : path[(idx + 1)..];
    }

    /// <summary>判斷檔名是否為備份檔（.bak 或 .trn）。</summary>
    public static bool IsBackupFile(string name) =>
        name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".trn", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 8: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~ServerVolumeInfoTests|FullyQualifiedName~ServerPathHelperTests"`
Expected: PASS（全部通過）。

- [ ] **Step 9: Commit**

```bash
git add src/Specurai.Domain/Entities/ServerVolumeInfo.cs src/Specurai.Domain/Entities/ServerDirectoryEntry.cs src/Specurai.Domain/ServerPathHelper.cs tests/Specurai.Domain.Tests/ServerVolumeInfoTests.cs tests/Specurai.Domain.Tests/ServerPathHelperTests.cs
git commit -m "feat: 新增伺服器磁碟區、目錄項目實體與跨平台路徑輔助類別"
```

---

### Task 2: IBackupService 查詢方法與 MssqlBackupService 實作

**Files:**
- Modify: `src/Specurai.Domain/Interfaces/IBackupService.cs`（在介面尾端 `RemoveFromHistory` 之後新增三個方法宣告）
- Modify: `src/Specurai.Infrastructure/Services/MssqlBackupService.cs`（新增實作與私有輔助方法）

**Interfaces:**
- Consumes: `ServerVolumeInfo`、`ServerDirectoryEntry`、`ServerPathHelper`（Task 1）
- Produces（`IBackupService` 新增）:
  - `Task<IReadOnlyList<ServerVolumeInfo>> GetServerVolumesAsync(string connectionString, CancellationToken ct = default)`
  - `Task<IReadOnlyList<ServerDirectoryEntry>> ListServerDirectoryAsync(string connectionString, string path, CancellationToken ct = default)`
  - `Task<string?> GetServerDefaultBackupPathAsync(string connectionString, CancellationToken ct = default)`

> 說明：`MssqlBackupService` 的 SQL 需連上實際伺服器，故本任務以「編譯通過 + 既有測試不回歸 + 選用真實連線煙霧測試」驗收，不寫單元測試（與現有 `MssqlBackupService` 一致）。純邏輯（副檔名過濾、路徑組合）已於 Task 1 單元測試涵蓋。

- [ ] **Step 1: 在 `IBackupService` 新增方法宣告**

Modify `src/Specurai.Domain/Interfaces/IBackupService.cs`，在 `void RemoveFromHistory(Guid backupId);`（第 94 行）之後、介面結尾 `}`（第 95 行）之前插入：

```csharp

    /// <summary>
    /// 取得伺服器各磁碟區的空間資訊（跨 Windows／Linux）
    /// </summary>
    Task<IReadOnlyList<ServerVolumeInfo>> GetServerVolumesAsync(
        string connectionString,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出伺服器端指定路徑下一層的資料夾與備份檔（path 為空時回傳各磁碟根節點）
    /// </summary>
    Task<IReadOnlyList<ServerDirectoryEntry>> ListServerDirectoryAsync(
        string connectionString,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得 SQL Server 的預設備份目錄；查不到時回傳 null
    /// </summary>
    Task<string?> GetServerDefaultBackupPathAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 2: 執行建置確認失敗（介面未實作）**

Run: `dotnet build src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: 失敗，`MssqlBackupService` 未實作 `IBackupService` 三個新成員。

- [ ] **Step 3: 在 `MssqlBackupService` 新增 using 與磁碟查詢實作**

確認 `src/Specurai.Infrastructure/Services/MssqlBackupService.cs` 頂端已有 `using Specurai.Domain;`（供 `ServerPathHelper`）。若無則加入。

在類別結尾 `}`（檔案最後一行）之前、`#endregion` 之後插入以下方法：

```csharp
    #region 伺服器磁碟與目錄查詢

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerVolumeInfo>> GetServerVolumesAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            return await QueryVolumesModernAsync(connection, cancellationToken);
        }
        catch (SqlException)
        {
            // 舊版 SQL Server 無 sys.dm_os_enumerate_fixed_drives，改走平台 fallback
            return await QueryVolumesFallbackAsync(connection, cancellationToken);
        }
    }

    // SQL 2019 CU2+：一次取得所有固定磁碟 + 可用 + 總量
    private static async Task<IReadOnlyList<ServerVolumeInfo>> QueryVolumesModernAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SELECT d.fixed_drive_path      AS Name,
       d.free_space_in_bytes   AS FreeBytes,
       v.total_bytes           AS TotalBytes,
       v.logical_volume_name   AS Label
FROM sys.dm_os_enumerate_fixed_drives AS d
OUTER APPLY (
    SELECT TOP 1 vs.total_bytes, vs.logical_volume_name
    FROM sys.master_files AS mf
    CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) AS vs
    WHERE vs.volume_mount_point = d.fixed_drive_path
) AS v
ORDER BY d.fixed_drive_path;";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadVolumesAsync(reader, ct);
    }

    // Fallback：先偵測平台，Windows 用 xp_fixeddrives、Linux 用 dm_os_volume_stats
    private static async Task<IReadOnlyList<ServerVolumeInfo>> QueryVolumesFallbackAsync(
        SqlConnection connection, CancellationToken ct)
    {
        var platform = await GetHostPlatformAsync(connection, ct);
        if (string.Equals(platform, "Linux", StringComparison.OrdinalIgnoreCase))
            return await QueryVolumesFromVolumeStatsAsync(connection, ct);
        return await QueryVolumesFromFixedDrivesAsync(connection, ct);
    }

    private static async Task<string> GetHostPlatformAsync(SqlConnection connection, CancellationToken ct)
    {
        try
        {
            await using var cmd = new SqlCommand("SELECT host_platform FROM sys.dm_os_host_info", connection);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result?.ToString() ?? "Windows";
        }
        catch
        {
            return "Windows"; // dm_os_host_info 不存在（SQL 2016 以前）→ 視為 Windows
        }
    }

    private static async Task<IReadOnlyList<ServerVolumeInfo>> QueryVolumesFromFixedDrivesAsync(
        SqlConnection connection, CancellationToken ct)
    {
        var result = new List<ServerVolumeInfo>();
        await using var cmd = new SqlCommand("EXEC master.dbo.xp_fixeddrives", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var drive = reader.GetValue(0)?.ToString() ?? string.Empty;
            var freeMb = Convert.ToInt64(reader.GetValue(1));
            result.Add(new ServerVolumeInfo
            {
                Name = $"{drive}:\\",
                FreeBytes = freeMb * 1024 * 1024,
                TotalBytes = null
            });
        }
        return result;
    }

    private static async Task<IReadOnlyList<ServerVolumeInfo>> QueryVolumesFromVolumeStatsAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SELECT DISTINCT vs.volume_mount_point AS Name,
       vs.available_bytes             AS FreeBytes,
       vs.total_bytes                 AS TotalBytes,
       vs.logical_volume_name         AS Label
FROM sys.master_files AS mf
CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) AS vs
ORDER BY vs.volume_mount_point;";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadVolumesAsync(reader, ct);
    }

    // 共用讀取：欄位順序固定為 Name, FreeBytes, TotalBytes, Label
    private static async Task<IReadOnlyList<ServerVolumeInfo>> ReadVolumesAsync(
        SqlDataReader reader, CancellationToken ct)
    {
        var list = new List<ServerVolumeInfo>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ServerVolumeInfo
            {
                Name = reader.GetString(0),
                FreeBytes = reader.GetInt64(1),
                TotalBytes = await reader.IsDBNullAsync(2, ct) ? null : reader.GetInt64(2),
                Label = await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3)
            });
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerDirectoryEntry>> ListServerDirectoryAsync(
        string connectionString,
        string path,
        CancellationToken cancellationToken = default)
    {
        // 根層：以各磁碟作為資料夾節點
        if (string.IsNullOrEmpty(path))
        {
            var volumes = await GetServerVolumesAsync(connectionString, cancellationToken);
            return volumes.Select(v => new ServerDirectoryEntry
            {
                Name = v.Name,
                FullPath = v.Name,
                IsDirectory = true
            }).ToList();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // xp_dirtree 'path', 1, 1 → 單層、含檔案；欄位：subdirectory, depth, file(1=檔案)
        await using var cmd = new SqlCommand("EXEC master.sys.xp_dirtree @path, 1, 1", connection);
        cmd.Parameters.AddWithValue("@path", path);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var entries = new List<ServerDirectoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetValue(0)?.ToString() ?? string.Empty;
            var isFile = Convert.ToInt32(reader.GetValue(2)) == 1;
            if (isFile && !ServerPathHelper.IsBackupFile(name)) continue; // 僅顯示 .bak/.trn
            entries.Add(new ServerDirectoryEntry
            {
                Name = name,
                FullPath = ServerPathHelper.Combine(path, name),
                IsDirectory = !isFile
            });
        }
        // 資料夾在前、檔案在後，各自依名稱排序
        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetServerDefaultBackupPathAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(
            "SELECT SERVERPROPERTY('InstanceDefaultBackupPath')", connection);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : result.ToString();
    }

    #endregion
```

> 註：若檔案頂端 using 區缺少 `using System.Linq;` 或 `using Specurai.Domain;`，請補上（`.Select`／`.OrderByDescending`／`ServerPathHelper` 需要）。

- [ ] **Step 4: 執行建置確認通過**

Run: `dotnet build src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: Build succeeded。

- [ ] **Step 5: 執行既有測試確認無回歸**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 全部通過（無新增測試，確認未破壞既有）。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Interfaces/IBackupService.cs src/Specurai.Infrastructure/Services/MssqlBackupService.cs
git commit -m "feat: IBackupService 新增磁碟空間、伺服器目錄與預設備份路徑查詢"
```

---

### Task 3: 伺服器端資料夾瀏覽對話框

**Files:**
- Create: `src/Specurai.Desktop/ViewModels/ServerFolderNode.cs`
- Create: `src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs`
- Create: `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml`
- Create: `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml.cs`
- Test: `tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs`

**Interfaces:**
- Consumes: `IBackupService.ListServerDirectoryAsync`（Task 2）、`ServerDirectoryEntry`、`ServerPathHelper`（Task 1）
- Produces:
  - `ServerFolderNode(ServerDirectoryEntry entry, Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>> loadChildren)`；成員 `Name`、`FullPath`、`IsDirectory`、`IsPlaceholder`、`ObservableCollection<ServerFolderNode> Children`、`bool IsExpanded`、`Task LoadChildrenAsync()`
  - `ServerFolderBrowserViewModel(IBackupService backupService, string connectionString, string initialFileName)`；成員 `ObservableCollection<ServerFolderNode> RootNodes`、`ServerFolderNode? SelectedNode`、`string SelectedPath`、`string FileName`、`string ErrorMessage`、`string? ResultPath`、`event Action<bool>? RequestClose`、`Task LoadRootAsync()`、`ConfirmCommand`、`CancelCommand`

- [ ] **Step 1: 寫失敗測試（對話框 ViewModel 與節點）**

Create `tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class ServerFolderBrowserViewModelTests
{
    private static IBackupService BuildService()
    {
        var svc = Substitute.For<IBackupService>();
        svc.ListServerDirectoryAsync("cs", "", Arg.Any<CancellationToken>())
            .Returns(new List<ServerDirectoryEntry>
            {
                new() { Name = "C:\\", FullPath = "C:\\", IsDirectory = true },
                new() { Name = "D:\\", FullPath = "D:\\", IsDirectory = true }
            });
        svc.ListServerDirectoryAsync("cs", "D:\\", Arg.Any<CancellationToken>())
            .Returns(new List<ServerDirectoryEntry>
            {
                new() { Name = "SQLBackup", FullPath = "D:\\SQLBackup", IsDirectory = true },
                new() { Name = "old.bak", FullPath = "D:\\old.bak", IsDirectory = false }
            });
        return svc;
    }

    [Fact]
    public async Task LoadRootAsync_填入磁碟根節點()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        await vm.LoadRootAsync();
        vm.RootNodes.Should().HaveCount(2);
        vm.RootNodes[0].FullPath.Should().Be("C:\\");
    }

    [Fact]
    public async Task LoadChildrenAsync_展開節點載入子項()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        await vm.LoadRootAsync();
        var dNode = vm.RootNodes[1]; // D:\
        await dNode.LoadChildrenAsync();
        dNode.Children.Should().HaveCount(2);
        dNode.Children[0].Name.Should().Be("SQLBackup");
    }

    [Fact]
    public void Confirm_組合資料夾與檔名並要求關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak")
        {
            SelectedPath = "D:\\SQLBackup"
        };
        bool? closedWith = null;
        vm.RequestClose += ok => closedWith = ok;

        vm.ConfirmCommand.Execute(null);

        vm.ResultPath.Should().Be("D:\\SQLBackup\\my.bak");
        closedWith.Should().BeTrue();
    }

    [Fact]
    public void Confirm_未選資料夾_顯示錯誤不關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        bool closed = false;
        vm.RequestClose += _ => closed = true;

        vm.ConfirmCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void SelectFileNode_帶入所在資料夾與檔名()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        var loader = new System.Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>>(
            _ => Task.FromResult<IReadOnlyList<ServerDirectoryEntry>>(new List<ServerDirectoryEntry>()));
        var fileNode = new ServerFolderNode(
            new ServerDirectoryEntry { Name = "old.bak", FullPath = "D:\\SQLBackup\\old.bak", IsDirectory = false },
            loader);

        vm.SelectedNode = fileNode;

        vm.SelectedPath.Should().Be("D:\\SQLBackup");
        vm.FileName.Should().Be("old.bak");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ServerFolderBrowserViewModelTests"`
Expected: 編譯失敗（型別不存在）。

- [ ] **Step 3: 建立 `ServerFolderNode`**

Create `src/Specurai.Desktop/ViewModels/ServerFolderNode.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 伺服器資料夾樹節點（惰性載入子項）
/// </summary>
public partial class ServerFolderNode : ObservableObject
{
    private readonly Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>>? _loadChildren;
    private bool _loaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    /// <summary>是否為「載入中…」佔位節點</summary>
    public bool IsPlaceholder { get; }

    public ObservableCollection<ServerFolderNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    // 佔位節點建構函式
    private ServerFolderNode(string name)
    {
        Name = name;
        FullPath = string.Empty;
        IsDirectory = false;
        IsPlaceholder = true;
    }

    public ServerFolderNode(
        ServerDirectoryEntry entry,
        Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>> loadChildren)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        _loadChildren = loadChildren;

        // 資料夾預置佔位子節點，讓 TreeView 顯示展開箭頭
        if (IsDirectory)
            Children.Add(new ServerFolderNode("載入中…"));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && IsDirectory)
            _ = LoadChildrenAsync();
    }

    /// <summary>載入實際子項（首次展開時呼叫）</summary>
    public async Task LoadChildrenAsync()
    {
        if (_loaded || _loadChildren is null) return;
        _loaded = true;

        var children = await _loadChildren(FullPath);
        Children.Clear();
        foreach (var c in children)
            Children.Add(new ServerFolderNode(c, _loadChildren));
    }
}
```

- [ ] **Step 4: 建立 `ServerFolderBrowserViewModel`**

Create `src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Domain;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 伺服器端資料夾瀏覽對話框 ViewModel
/// </summary>
public partial class ServerFolderBrowserViewModel : ObservableObject
{
    private readonly IBackupService? _backupService;
    private readonly string _connectionString;

    public ObservableCollection<ServerFolderNode> RootNodes { get; } = [];

    [ObservableProperty]
    private ServerFolderNode? _selectedNode;

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>確定後的完整路徑（資料夾 + 檔名）</summary>
    public string? ResultPath { get; private set; }

    /// <summary>要求關閉視窗：true = 確定、false = 取消</summary>
    public event Action<bool>? RequestClose;

    /// <summary>設計時建構函式</summary>
    public ServerFolderBrowserViewModel()
    {
        _connectionString = string.Empty;
    }

    /// <summary>執行時建構函式</summary>
    public ServerFolderBrowserViewModel(IBackupService backupService, string connectionString, string initialFileName)
    {
        _backupService = backupService;
        _connectionString = connectionString;
        _fileName = initialFileName;
    }

    /// <summary>載入根節點（各磁碟）</summary>
    public async Task LoadRootAsync()
    {
        if (_backupService is null) return;
        try
        {
            var roots = await _backupService.ListServerDirectoryAsync(_connectionString, string.Empty);
            RootNodes.Clear();
            foreach (var r in roots)
                RootNodes.Add(new ServerFolderNode(r, LoadChildrenAsync));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽伺服器目錄：{ex.Message}";
        }
    }

    private async Task<IReadOnlyList<ServerDirectoryEntry>> LoadChildrenAsync(string path)
    {
        if (_backupService is null) return [];
        try
        {
            return await _backupService.ListServerDirectoryAsync(_connectionString, path);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽「{path}」：{ex.Message}";
            return [];
        }
    }

    partial void OnSelectedNodeChanged(ServerFolderNode? value)
    {
        if (value is null || value.IsPlaceholder) return;

        if (value.IsDirectory)
        {
            SelectedPath = value.FullPath;
        }
        else
        {
            // 選到現有備份檔：帶入其所在資料夾與檔名
            SelectedPath = ParentOf(value.FullPath);
            FileName = value.Name;
        }
    }

    private static string ParentOf(string fullPath)
    {
        var sep = ServerPathHelper.GetSeparator(fullPath);
        var idx = fullPath.TrimEnd(sep).LastIndexOf(sep);
        return idx <= 0 ? fullPath : fullPath[..idx];
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath) || string.IsNullOrWhiteSpace(FileName))
        {
            ErrorMessage = "請選擇資料夾並輸入檔案名稱";
            return;
        }
        ResultPath = ServerPathHelper.Combine(SelectedPath, FileName);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~ServerFolderBrowserViewModelTests"`
Expected: PASS。

- [ ] **Step 6: 建立對話框 View（AXAML）**

Create `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Specurai.Desktop.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="560" d:DesignHeight="520"
        x:Class="Specurai.Desktop.Views.ServerFolderBrowserWindow"
        x:DataType="vm:ServerFolderBrowserViewModel"
        Title="尋找備份資料夾" Width="560" Height="520"
        WindowStartupLocation="CenterOwner">

    <Design.DataContext>
        <vm:ServerFolderBrowserViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto,Auto" Margin="14">
        <TextBlock Grid.Row="0" Text="選取資料夾：" Margin="0,0,0,6"/>

        <Border Grid.Row="1" BorderBrush="Gray" BorderThickness="1" CornerRadius="4">
            <TreeView ItemsSource="{Binding RootNodes}"
                      SelectedItem="{Binding SelectedNode}">
                <TreeView.ItemTemplate>
                    <TreeDataTemplate ItemsSource="{Binding Children}" x:DataType="vm:ServerFolderNode">
                        <StackPanel Orientation="Horizontal" Spacing="5">
                            <TextBlock Text="{Binding IsDirectory, Converter={x:Static vm:FolderIconConverter.Instance}}"/>
                            <TextBlock Text="{Binding Name}"/>
                        </StackPanel>
                    </TreeDataTemplate>
                </TreeView.ItemTemplate>
                <TreeView.Styles>
                    <Style Selector="TreeViewItem">
                        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"/>
                    </Style>
                </TreeView.Styles>
            </TreeView>
        </Border>

        <Grid Grid.Row="2" ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" Margin="0,12,0,0">
            <TextBlock Grid.Row="0" Grid.Column="0" Text="選取的路徑：" VerticalAlignment="Center" Margin="0,0,10,6"/>
            <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding SelectedPath}" IsReadOnly="True" Margin="0,0,0,6"/>

            <TextBlock Grid.Row="1" Grid.Column="0" Text="檔案名稱：" VerticalAlignment="Center" Margin="0,0,10,6"/>
            <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding FileName}" Margin="0,0,0,6"/>

            <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding ErrorMessage}" Foreground="#E06C6C" FontSize="12"/>
        </Grid>

        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10" Margin="0,12,0,0">
            <Button Content="確定" Command="{Binding ConfirmCommand}" MinWidth="80"/>
            <Button Content="取消" Command="{Binding CancelCommand}" MinWidth="80"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 7: 建立資料夾圖示轉換器**

Create `src/Specurai.Desktop/ViewModels/FolderIconConverter.cs`（放在 ViewModels 命名空間以配合 AXAML `vm:` 前綴；純顯示用途）:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Specurai.Desktop.ViewModels;

/// <summary>資料夾／檔案圖示轉換器（true → 📁、false → 📄）</summary>
public class FolderIconConverter : IValueConverter
{
    public static readonly FolderIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "📁" : "📄";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 8: 建立對話框 code-behind**

Create `src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class ServerFolderBrowserWindow : Window
{
    public ServerFolderBrowserWindow()
    {
        InitializeComponent();
    }

    public ServerFolderBrowserWindow(ServerFolderBrowserViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += confirmed => Close(confirmed);
        Opened += async (_, _) => await viewModel.LoadRootAsync();
    }
}
```

- [ ] **Step 9: 執行建置確認通過**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded。

- [ ] **Step 10: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/ServerFolderNode.cs src/Specurai.Desktop/ViewModels/ServerFolderBrowserViewModel.cs src/Specurai.Desktop/ViewModels/FolderIconConverter.cs src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml src/Specurai.Desktop/Views/ServerFolderBrowserWindow.axaml.cs tests/Specurai.Desktop.Tests/ServerFolderBrowserViewModelTests.cs
git commit -m "feat: 新增伺服器端資料夾瀏覽對話框（xp_dirtree 惰性樹狀載入）"
```

---

### Task 4: 備份頁 ViewModel 整合磁碟清單與伺服器瀏覽

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/BackupRestoreDocumentViewModelServerTests.cs`

**Interfaces:**
- Consumes: `IBackupService.GetServerVolumesAsync` / `GetServerDefaultBackupPathAsync`（Task 2）、`ServerFolderBrowserViewModel` / `ServerFolderBrowserWindow`（Task 3）、`ServerPathHelper`（Task 1）
- Produces（`BackupRestoreDocumentViewModel` 新增）:
  - `ObservableCollection<ServerVolumeInfo> ServerVolumes`
  - `bool IsLoadingVolumes`、`string VolumesMessage`
  - `RefreshVolumesCommand`
  - 改寫 `BrowseBackupPathCommand`（開啟伺服器瀏覽對話框）

- [ ] **Step 1: 寫失敗測試（磁碟清單載入）**

Create `tests/Specurai.Desktop.Tests/BackupRestoreDocumentViewModelServerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
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

public class BackupRestoreDocumentViewModelServerTests
{
    private static (BackupRestoreDocumentViewModel vm, IBackupService svc) Build()
    {
        var svc = Substitute.For<IBackupService>();
        var conn = Substitute.For<IConnectionManager>();

        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "測試連線",
            Server = "localhost",
            Database = "TestDb"
        };
        conn.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("Server=localhost;Database=TestDb;");

        svc.GetServerVolumesAsync("Server=localhost;Database=TestDb;", Arg.Any<CancellationToken>())
            .Returns(new List<ServerVolumeInfo>
            {
                new() { Name = "C:\\", FreeBytes = 100, TotalBytes = 200 },
                new() { Name = "D:\\", FreeBytes = 50, TotalBytes = null }
            });

        var vm = new BackupRestoreDocumentViewModel(svc, conn);
        return (vm, svc);
    }

    [Fact]
    public async Task RefreshVolumes_填入磁碟清單()
    {
        var (vm, _) = Build();
        await vm.RefreshVolumesCommand.ExecuteAsync(null);
        vm.ServerVolumes.Should().HaveCount(2);
        vm.ServerVolumes[0].Name.Should().Be("C:\\");
    }

    [Fact]
    public async Task RefreshVolumes_查詢例外_設定訊息且不丟例外()
    {
        var (vm, svc) = Build();
        svc.GetServerVolumesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ServerVolumeInfo>>>(_ => throw new InvalidOperationException("boom"));

        await vm.RefreshVolumesCommand.ExecuteAsync(null);

        vm.ServerVolumes.Should().BeEmpty();
        vm.VolumesMessage.Should().Contain("無法取得磁碟資訊");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~BackupRestoreDocumentViewModelServerTests"`
Expected: 編譯失敗（`RefreshVolumesCommand`／`ServerVolumes` 不存在）。

- [ ] **Step 3: 新增 using 與磁碟清單成員**

Modify `src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs`：

在檔案頂端 using 區加入（若尚無）：

```csharp
using Avalonia.Controls.ApplicationLifetimes;
using Specurai.Desktop.Views;
using Specurai.Domain;
```

在 `#region 備份設定` 內、`BackupTypes` 集合宣告（第 65-70 行）之後加入：

```csharp

    /// <summary>伺服器磁碟區清單</summary>
    public ObservableCollection<ServerVolumeInfo> ServerVolumes { get; } = [];

    [ObservableProperty]
    private bool _isLoadingVolumes;

    [ObservableProperty]
    private string _volumesMessage = string.Empty;
```

- [ ] **Step 4: 新增磁碟載入方法與命令**

在 `#region 備份命令` 內、`BrowseBackupPathAsync`（原第 448-469 行）之前加入：

```csharp
    /// <summary>載入伺服器磁碟空間清單</summary>
    private async Task LoadServerVolumesAsync()
    {
        ServerVolumes.Clear();
        VolumesMessage = string.Empty;

        if (_backupService == null || _connectionManager == null || SelectedProfile == null)
            return;

        var connectionString = _connectionManager.GetConnectionString(SelectedProfile.Id);
        if (string.IsNullOrEmpty(connectionString))
            return;

        try
        {
            IsLoadingVolumes = true;
            var volumes = await _backupService.GetServerVolumesAsync(connectionString);
            foreach (var v in volumes)
                ServerVolumes.Add(v);
            if (ServerVolumes.Count == 0)
                VolumesMessage = "無磁碟資訊";
        }
        catch (Exception ex)
        {
            VolumesMessage = $"無法取得磁碟資訊：{ex.Message}";
        }
        finally
        {
            IsLoadingVolumes = false;
        }
    }

    [RelayCommand]
    private async Task RefreshVolumesAsync() => await LoadServerVolumesAsync();
```

- [ ] **Step 5: 選連線時自動載入磁碟清單**

在 `OnSelectedProfileChanged`（第 266-281 行）的 `if (value != null)` 區塊內，`GenerateDefaultBackupPath();`（第 273 行）之後加入一行：

```csharp
            _ = LoadServerVolumesAsync();
```

- [ ] **Step 6: 改寫預設備份路徑改用服務層（移除內嵌 SQL）**

將 `GenerateDefaultBackupPath()`（第 310-338 行）整個方法內容改為：

```csharp
    private async void GenerateDefaultBackupPath()
    {
        if (SelectedProfile == null || _connectionManager == null || _backupService == null) return;

        var fileName = $"{SelectedProfile.Database}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        var connectionString = _connectionManager.GetConnectionString(SelectedProfile.Id);

        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                var defaultPath = await _backupService.GetServerDefaultBackupPathAsync(connectionString);
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    BackupPath = ServerPathHelper.Combine(defaultPath, fileName);
                    return;
                }
            }
            catch
            {
                // 忽略錯誤，改僅帶入檔名
            }
        }

        // 查不到伺服器預設路徑：僅留檔名，待使用者以「瀏覽」選擇資料夾
        BackupPath = fileName;
    }
```

刪除 `GetSqlServerDefaultBackupPathAsync` 靜態方法（原第 340-354 行，含其 XML 註解）——此為原本的分層違規查詢，已由服務層取代。

- [ ] **Step 7: 改寫 `BrowseBackupPathAsync` 開啟伺服器瀏覽對話框**

將 `BrowseBackupPathAsync()`（原第 448-469 行）整個方法改為：

```csharp
    [RelayCommand]
    private async Task BrowseBackupPathAsync()
    {
        if (_backupService == null || _connectionManager == null || SelectedProfile == null)
        {
            StatusMessage = "請先選擇連線";
            return;
        }

        var connectionString = _connectionManager.GetConnectionString(SelectedProfile.Id);
        if (string.IsNullOrEmpty(connectionString))
        {
            StatusMessage = "無法取得連線字串";
            return;
        }

        var initialFileName = string.IsNullOrWhiteSpace(BackupPath)
            ? $"{SelectedProfile.Database}_{DateTime.Now:yyyyMMdd_HHmmss}.bak"
            : ServerPathHelper.GetFileName(BackupPath);

        var dialogViewModel = new ServerFolderBrowserViewModel(_backupService, connectionString, initialFileName);
        var dialog = new ServerFolderBrowserWindow(dialogViewModel);

        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (owner == null) return;

        var confirmed = await dialog.ShowDialog<bool>(owner);
        if (confirmed && !string.IsNullOrEmpty(dialogViewModel.ResultPath))
            BackupPath = dialogViewModel.ResultPath;
    }
```

- [ ] **Step 8: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~BackupRestoreDocumentViewModelServerTests"`
Expected: PASS。

- [ ] **Step 9: 執行 Desktop 全部測試確認無回歸**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 全部通過。

- [ ] **Step 10: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs tests/Specurai.Desktop.Tests/BackupRestoreDocumentViewModelServerTests.cs
git commit -m "feat: 備份頁載入伺服器磁碟清單並改用伺服器端資料夾瀏覽；移除 ViewModel 內嵌 SQL"
```

---

### Task 5: 備份頁 View 加入磁碟空間卡片與瀏覽按鈕

**Files:**
- Modify: `src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml`

**Interfaces:**
- Consumes: `ServerVolumes`、`RefreshVolumesCommand`、`VolumesMessage`、`BrowseBackupPathCommand`（Task 4）；`ServerVolumeInfo`（Task 1，透過既有 `xmlns:domain`）

- [ ] **Step 1: 插入「伺服器磁碟空間」卡片**

Modify `src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml`：在「來源資料庫」`Border` 結束（第 84 行 `</Border>`）之後、「備份設定」`Border` 開始（第 87 行）之前，插入：

```xml
                        <!-- 伺服器磁碟空間 -->
                        <Border Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
                                CornerRadius="5" Padding="15">
                            <StackPanel Spacing="10">
                                <Grid ColumnDefinitions="*,Auto">
                                    <TextBlock Grid.Column="0" Text="伺服器磁碟空間" FontWeight="Bold" FontSize="14"
                                               VerticalAlignment="Center"/>
                                    <Button Grid.Column="1" Command="{Binding RefreshVolumesCommand}" Padding="8,4">
                                        <StackPanel Orientation="Horizontal" Spacing="5">
                                            <TextBlock Text="🔃" FontSize="12"/>
                                            <TextBlock Text="重新整理"/>
                                        </StackPanel>
                                    </Button>
                                </Grid>

                                <DataGrid ItemsSource="{Binding ServerVolumes}"
                                          AutoGenerateColumns="False" IsReadOnly="True"
                                          HeadersVisibility="Column" MaxHeight="160"
                                          CanUserResizeColumns="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="磁碟" Binding="{Binding Name}" Width="Auto"/>
                                        <DataGridTextColumn Header="總量" Binding="{Binding FormattedTotal}" Width="Auto"/>
                                        <DataGridTextColumn Header="可用" Binding="{Binding FormattedFree}" Width="Auto"/>
                                        <DataGridTemplateColumn Header="使用率" Width="*">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="domain:ServerVolumeInfo">
                                                    <StackPanel Orientation="Horizontal" Spacing="8"
                                                                VerticalAlignment="Center" Margin="4,0">
                                                        <ProgressBar Minimum="0" Maximum="100"
                                                                     Value="{Binding UsedPercentValue}"
                                                                     Width="120" Height="8"/>
                                                        <TextBlock Text="{Binding UsedPercentText}" VerticalAlignment="Center"/>
                                                    </StackPanel>
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>

                                <TextBlock Text="{Binding VolumesMessage}" Foreground="Gray" FontSize="11"
                                           IsVisible="{Binding VolumesMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
                            </StackPanel>
                        </Border>
```

- [ ] **Step 2: 在備份路徑列加入「瀏覽…」按鈕**

於「備份設定」區塊，將備份路徑的 `Grid`（第 106-117 行）整段替換為（把欄位定義由 `Auto,*` 改為 `Auto,*,Auto`，並在 Row 0 加瀏覽按鈕、讓說明與描述列跨欄）：

```xml
                                <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto,Auto">
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="備份路徑：" VerticalAlignment="Center" Margin="0,0,10,5"/>
                                    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding BackupPath}" Margin="0,0,0,5"
                                             Watermark="SQL Server 伺服器上的路徑，例如：C:\Backup\MyDB.bak"/>
                                    <Button Grid.Row="0" Grid.Column="2" Content="瀏覽…" Command="{Binding BrowseBackupPathCommand}"
                                            Margin="8,0,0,5" ToolTip.Tip="瀏覽 SQL Server 伺服器端資料夾"/>

                                    <TextBlock Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2"
                                               Text="* 此路徑為 SQL Server 伺服器端路徑，非本機路徑"
                                               FontSize="11" Foreground="Gray" Margin="0,0,0,10"/>

                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="備份描述：" VerticalAlignment="Center" Margin="0,0,10,0"/>
                                    <TextBox Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Text="{Binding BackupDescription}"
                                             Watermark="選填，例如：Schema Compare 前的備份"/>
                                </Grid>
```

- [ ] **Step 3: 建置整個解決方案確認通過**

Run: `dotnet build`
Expected: Build succeeded（AXAML 編譯無誤）。

> 若 Desktop 專案 DLL 被執行中的程式鎖定導致建置失敗，先關閉執行中的桌面程式再重試（見專案記憶 republish-mcp-lock）。

- [ ] **Step 4: 執行全部測試確認無回歸**

Run: `dotnet test`
Expected: 全部通過。

- [ ] **Step 5: 手動煙霧驗證（選用但建議）**

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`
步驟：開啟「備份與還原」→ 選一個連線 → 確認「伺服器磁碟空間」表格出現 C/D 磁碟與使用率 → 點「瀏覽…」→ 展開磁碟樹、選資料夾 → 確定後備份路徑正確帶回。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop/Views/BackupRestoreDocumentView.axaml
git commit -m "feat: 備份頁加入伺服器磁碟空間表格與伺服器端資料夾瀏覽按鈕"
```

---

## 完成後

- [ ] 執行 `superpowers:requesting-code-review` 進行程式碼審查（專案憲章要求）。
- [ ] 依審查結果修正，全部測試綠燈後回報完成。

## Self-Review 對照（spec → task）

| Spec 需求 | 對應 Task |
|-----------|-----------|
| §5.1 ServerVolumeInfo | Task 1 |
| §5.2 ServerDirectoryEntry | Task 1 |
| §5.3 IBackupService 三方法 | Task 2 |
| §6.1 磁碟查詢 + 版本 fallback | Task 2 |
| §6.2 xp_dirtree 目錄樹 + .bak/.trn 過濾 | Task 2（服務）＋ Task 3（樹 UI）|
| §6.3 GetServerDefaultBackupPathAsync + 移除硬編路徑 | Task 2 + Task 4 |
| §7.1 磁碟卡片 + 瀏覽按鈕 | Task 5 |
| §7.2 ViewModel 磁碟載入/刷新/改寫瀏覽/移除內嵌 SQL | Task 4 |
| §7.3 對話框（惰性 TreeView、選路徑/檔名、權限容錯）| Task 3 |
| §8 錯誤處理（不阻擋備份）| Task 2/3/4（try-catch + 訊息）|
| §9 測試 | Task 1/3/4 單元測試 |

> 註（spec 精修）：spec §6.2 原以 `host_platform` 決定子路徑分隔字元；實作改由 `ServerPathHelper` 依實際路徑字串判定，等效且更簡潔，無需額外查詢。`host_platform` 僅用於 §6.1 舊版磁碟查詢 fallback。
