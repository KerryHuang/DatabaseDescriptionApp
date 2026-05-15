# 效能診斷 — 實例健康分頁 實作計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在「效能診斷」TabControl 新增「實例健康」分頁,提供 VLF 數量、TempDB 配置、Max Server Memory 三項實例層級健康快照。

**Architecture:** 沿用「完整性檢查」分頁(v1.14.0)同樣的 Repository → Service → ViewModel → View 鏈路。新增 6 個 Domain Entity(3 final + 3 raw DTO + 1 共用 enum),擴充 `IPerformanceDiagnosticsRepository`/`IPerformanceDiagnosticsService` 各三個 query method,ViewModel 加集合/單值欄位 + Command,View 加新 TabItem。

**Tech Stack:** .NET 8、CommunityToolkit.Mvvm、Avalonia 11、Dapper、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions

**Spec:** `docs/superpowers/specs/2026-05-15-performance-diagnostics-instance-health-design.md`

---

## File Structure

| 動作 | 檔案 | 責任 |
|------|------|------|
| Create | `src/Specurai.Domain/Entities/InstanceHealth.cs` | 共用健康分級 enum |
| Create | `src/Specurai.Domain/Entities/VlfStatus.cs` | 單一 DB 的 VLF 健康(含分級) |
| Create | `src/Specurai.Domain/Entities/VlfRow.cs` | Repository 回傳的原始 VLF 資料 |
| Create | `src/Specurai.Domain/Entities/TempDbConfiguration.cs` | TempDB 健康(含分級與 TF 自動旗標) |
| Create | `src/Specurai.Domain/Entities/TempDbConfigurationRaw.cs` | Repository 回傳的原始 TempDB 資料 |
| Create | `src/Specurai.Domain/Entities/MaxServerMemoryConfiguration.cs` | Max Memory 健康(含 IsUnlimited / 分級) |
| Create | `src/Specurai.Domain/Entities/MaxServerMemoryConfigurationRaw.cs` | Repository 原始資料 |
| Modify | `src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs` | 新增 3 個查詢方法 |
| Modify | `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs` | 實作 3 個查詢 |
| Modify | `src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs` | 新增 3 個對應方法 |
| Modify | `src/Specurai.Application/Services/PerformanceDiagnosticsService.cs` | 實作 + 4 個健康分類純函式 |
| Modify | `src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs` | 集合 + 單值欄位 + IsLoading + Command |
| Modify | `src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml` | 新 TabItem「實例健康」(三段) |
| Create | `tests/Specurai.Domain.Tests/Entities/MaxServerMemoryConfigurationTests.cs` | `IsUnlimited` 邏輯 |
| Modify | `tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs` | 三組健康分類邊界 + 三方法轉發 |
| Modify | `tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs` | 設計時建構、Command 存在 |

---

## Task 1:Domain — 共用 Enum + 三組 Entity(final + raw)

**Files:**
- Create: 7 個 Entity 檔案(見下)
- Create: `tests/Specurai.Domain.Tests/Entities/MaxServerMemoryConfigurationTests.cs`

- [ ] **Step 1:寫失敗測試 — MaxServerMemoryConfigurationTests**

```csharp
// tests/Specurai.Domain.Tests/Entities/MaxServerMemoryConfigurationTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class MaxServerMemoryConfigurationTests
{
    [Fact]
    public void IsUnlimited_當CurrentMB為SQL預設值2147483647_應為True()
    {
        var c = new MaxServerMemoryConfiguration
        {
            CurrentMB = 2147483647,
            OsTotalMB = 16384,
            RecommendedMB = 14336,
            Health = InstanceHealth.Critical
        };
        c.IsUnlimited.Should().BeTrue();
    }

    [Fact]
    public void IsUnlimited_當已設定_應為False()
    {
        var c = new MaxServerMemoryConfiguration
        {
            CurrentMB = 12288,
            OsTotalMB = 16384,
            RecommendedMB = 14336,
            Health = InstanceHealth.Healthy
        };
        c.IsUnlimited.Should().BeFalse();
    }
}
```

- [ ] **Step 2:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~MaxServerMemoryConfigurationTests"`
Expected: FAIL — type not found

- [ ] **Step 3:建立 Entity 檔案**

```csharp
// src/Specurai.Domain/Entities/InstanceHealth.cs
namespace Specurai.Domain.Entities;

/// <summary>實例層級健康分級(共用於 VLF / TempDB / Max Memory)</summary>
public enum InstanceHealth
{
    Healthy,
    Warning,
    Critical,
    Unknown
}
```

```csharp
// src/Specurai.Domain/Entities/VlfRow.cs
namespace Specurai.Domain.Entities;

/// <summary>單一 DB 的 VLF 原始查詢結果(Repository 回傳)</summary>
public class VlfRow
{
    public required string DatabaseName { get; init; }
    /// <summary>VLF 數量;null 表示查詢失敗(權限/版本不支援)</summary>
    public int? VlfCount { get; init; }
    public int? LogSizeMB { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/VlfStatus.cs
namespace Specurai.Domain.Entities;

/// <summary>單一 DB 的 VLF 健康狀態(含分級,Service 計算)</summary>
public class VlfStatus
{
    public required string DatabaseName { get; init; }
    public int? VlfCount { get; init; }
    public int? LogSizeMB { get; init; }
    public required InstanceHealth Health { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/TempDbConfigurationRaw.cs
namespace Specurai.Domain.Entities;

/// <summary>TempDB 原始組態查詢結果</summary>
public class TempDbConfigurationRaw
{
    public required int DataFileCount { get; init; }
    public required int CpuCount { get; init; }
    public required bool AllFilesEqualSize { get; init; }
    /// <summary>SERVERPROPERTY('ProductMajorVersion') 數值(例 16=2022, 15=2019, 13=2016)</summary>
    public required int SqlMajorVersion { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/TempDbConfiguration.cs
namespace Specurai.Domain.Entities;

/// <summary>TempDB 配置健康狀態(含分級)</summary>
public class TempDbConfiguration
{
    public required int DataFileCount { get; init; }
    public required int CpuCount { get; init; }
    public required int RecommendedFileCount { get; init; }
    public required bool AllFilesEqualSize { get; init; }
    /// <summary>SQL 2016+ 自動啟用 TF1117/1118</summary>
    public required bool TfAutoEnabled { get; init; }
    public required InstanceHealth Health { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/MaxServerMemoryConfigurationRaw.cs
namespace Specurai.Domain.Entities;

/// <summary>Max Server Memory 原始組態查詢結果</summary>
public class MaxServerMemoryConfigurationRaw
{
    public required long CurrentMB { get; init; }
    public required long OsTotalMB { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/MaxServerMemoryConfiguration.cs
namespace Specurai.Domain.Entities;

/// <summary>Max Server Memory 健康狀態(含建議值與分級)</summary>
public class MaxServerMemoryConfiguration
{
    public required long CurrentMB { get; init; }
    public required long OsTotalMB { get; init; }
    public required long RecommendedMB { get; init; }
    public required InstanceHealth Health { get; init; }

    /// <summary>SQL Server 預設值 2147483647 表示「無限制」</summary>
    public bool IsUnlimited => CurrentMB == 2147483647;
}
```

- [ ] **Step 4:執行測試確認 PASS**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~MaxServerMemoryConfigurationTests"`
Expected: PASS (2/2)

- [ ] **Step 5:Commit**

```bash
git add src/Specurai.Domain/Entities/InstanceHealth.cs src/Specurai.Domain/Entities/VlfRow.cs src/Specurai.Domain/Entities/VlfStatus.cs src/Specurai.Domain/Entities/TempDbConfigurationRaw.cs src/Specurai.Domain/Entities/TempDbConfiguration.cs src/Specurai.Domain/Entities/MaxServerMemoryConfigurationRaw.cs src/Specurai.Domain/Entities/MaxServerMemoryConfiguration.cs tests/Specurai.Domain.Tests/Entities/MaxServerMemoryConfigurationTests.cs
git commit -m "feat(domain): 新增實例健康相關 entity (VLF/TempDB/MaxMem + InstanceHealth enum)"
```

---

## Task 2:Repository 介面新增三個方法

**Files:**
- Modify: `src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:在介面尾端追加**

確保 `using Specurai.Domain.Entities;` 已存在(上次 spec 已加),於檔案最後一個方法之後追加:

```csharp
/// <summary>
/// 取得各資料庫的 VLF 數量(原始資料,Health 分級由 Service 計算)
/// </summary>
/// <param name="progress">每處理一個 DB 回報一次</param>
Task<IReadOnlyList<VlfRow>> GetVlfCountsAsync(IProgress<string>? progress = null, CancellationToken ct = default);

/// <summary>
/// 取得 TempDB 配置原始資料
/// </summary>
Task<TempDbConfigurationRaw> GetTempDbConfigurationAsync(CancellationToken ct = default);

/// <summary>
/// 取得 Max Server Memory 原始資料
/// </summary>
Task<MaxServerMemoryConfigurationRaw> GetMaxServerMemoryAsync(CancellationToken ct = default);
```

- [ ] **Step 2:Build(會 fail 在 Repository 實作)**

Run: `dotnet build`
Expected: FAIL — `PerformanceDiagnosticsRepository` 未實作三個新方法。

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs
git commit -m "feat(domain): IPerformanceDiagnosticsRepository 新增實例健康三個查詢方法"
```

---

## Task 3:Infrastructure — GetVlfCountsAsync 實作 + 兩個 stub

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:Read 現有 Repository 確認連線字串欄位 `_connectionStringProvider`**

- [ ] **Step 2:在類別尾端追加實作 + 兩個 stub**

```csharp
public async Task<IReadOnlyList<VlfRow>> GetVlfCountsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    const string listDbSql = @"
SELECT name, database_id FROM sys.databases
WHERE state = 0 AND database_id <> 2
ORDER BY database_id;";
    var dbs = (await conn.QueryAsync<(string Name, int DatabaseId)>(new CommandDefinition(listDbSql, cancellationToken: ct))).ToList();

    var results = new List<VlfRow>();
    int idx = 0;
    foreach (var (name, dbId) in dbs)
    {
        ct.ThrowIfCancellationRequested();
        idx++;
        progress?.Report($"VLF ({idx}/{dbs.Count}): {name}");

        int? vlfCount = null;
        int? logSizeMB = null;
        try
        {
            // sys.dm_db_log_info 需 SQL 2016 SP2+;若不可用 throw,catch 後保留 null
            var sql = $@"
SET NOCOUNT ON;
SELECT
    COUNT(*)                                          AS VlfCount,
    CAST(SUM(file_size) / 1024 / 1024 AS INT)         AS LogSizeMB
FROM sys.dm_db_log_info({dbId});";
            var row = await conn.QuerySingleOrDefaultAsync<(int VlfCount, int? LogSizeMB)>(
                new CommandDefinition(sql, cancellationToken: ct));
            vlfCount = row.VlfCount;
            logSizeMB = row.LogSizeMB;
        }
        catch
        {
            // 舊版 SQL 或權限不足:標 null(Service 會分類為 Unknown)
        }

        results.Add(new VlfRow { DatabaseName = name, VlfCount = vlfCount, LogSizeMB = logSizeMB });
    }

    return results;
}

public Task<TempDbConfigurationRaw> GetTempDbConfigurationAsync(CancellationToken ct = default)
    => throw new NotImplementedException();

public Task<MaxServerMemoryConfigurationRaw> GetMaxServerMemoryAsync(CancellationToken ct = default)
    => throw new NotImplementedException();
```

- [ ] **Step 3:Build + 全測試(無回歸)**

Run: `dotnet build && dotnet test`
Expected: 全 PASS(此 method 不寫 unit test,屬整合層 SQL,行為由 UI smoke test 驗證)

- [ ] **Step 4:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetVlfCountsAsync;其他實例健康查詢暫以 stub 保持 build green"
```

---

## Task 4:Infrastructure — GetTempDbConfigurationAsync 實作

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:替換 stub**

找到:
```csharp
public Task<TempDbConfigurationRaw> GetTempDbConfigurationAsync(CancellationToken ct = default)
    => throw new NotImplementedException();
```

替換為:

```csharp
public async Task<TempDbConfigurationRaw> GetTempDbConfigurationAsync(CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
DECLARE @cpuCount INT = (SELECT cpu_count FROM sys.dm_os_sys_info);
DECLARE @major    INT = CAST(SERVERPROPERTY('ProductMajorVersion') AS INT);

WITH FileSizes AS (
    SELECT size FROM tempdb.sys.database_files WHERE type = 0
)
SELECT
    (SELECT COUNT(*) FROM FileSizes)                                                         AS DataFileCount,
    @cpuCount                                                                                AS CpuCount,
    CASE WHEN (SELECT COUNT(DISTINCT size) FROM FileSizes) <= 1 THEN 1 ELSE 0 END           AS AllFilesEqualSizeFlag,
    @major                                                                                   AS SqlMajorVersion;";

    var row = await conn.QuerySingleAsync<TempDbRawRow>(new CommandDefinition(sql, cancellationToken: ct));
    return new TempDbConfigurationRaw
    {
        DataFileCount = row.DataFileCount,
        CpuCount = row.CpuCount,
        AllFilesEqualSize = row.AllFilesEqualSizeFlag == 1,
        SqlMajorVersion = row.SqlMajorVersion
    };
}

private sealed class TempDbRawRow
{
    public int DataFileCount { get; set; }
    public int CpuCount { get; set; }
    public int AllFilesEqualSizeFlag { get; set; }
    public int SqlMajorVersion { get; set; }
}
```

- [ ] **Step 2:Build + tests**

Run: `dotnet build && dotnet test`
Expected: 全 PASS

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetTempDbConfigurationAsync 查詢 TempDB 檔案數/CPU/版本"
```

---

## Task 5:Infrastructure — GetMaxServerMemoryAsync 實作

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:替換 stub**

```csharp
public async Task<MaxServerMemoryConfigurationRaw> GetMaxServerMemoryAsync(CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT
    CAST((SELECT value_in_use FROM sys.configurations WHERE name = 'max server memory (MB)') AS BIGINT) AS CurrentMB,
    CAST((SELECT total_physical_memory_kb / 1024 FROM sys.dm_os_sys_memory) AS BIGINT)                  AS OsTotalMB;";

    var row = await conn.QuerySingleAsync<MaxServerMemoryConfigurationRaw>(new CommandDefinition(sql, cancellationToken: ct));
    return row;
}
```

- [ ] **Step 2:Build + tests**

Run: `dotnet build && dotnet test`
Expected: 全 PASS

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetMaxServerMemoryAsync 查詢實例記憶體組態"
```

---

## Task 6:Application — Service 三方法 + 健康分類純函式

**Files:**
- Modify: `src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs`
- Modify: `src/Specurai.Application/Services/PerformanceDiagnosticsService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs`

- [ ] **Step 1:介面新增方法**

於 `IPerformanceDiagnosticsService.cs` 尾端追加:

```csharp
/// <summary>
/// 取得各資料庫的 VLF 健康狀態
/// </summary>
Task<IReadOnlyList<VlfStatus>> GetVlfStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default);

/// <summary>
/// 取得 TempDB 配置健康狀態
/// </summary>
Task<TempDbConfiguration> GetTempDbConfigurationAsync(CancellationToken ct = default);

/// <summary>
/// 取得 Max Server Memory 健康狀態
/// </summary>
Task<MaxServerMemoryConfiguration> GetMaxServerMemoryAsync(CancellationToken ct = default);
```

- [ ] **Step 2:寫失敗測試**

於 `PerformanceDiagnosticsServiceTests` 追加:

```csharp
[Theory]
[InlineData(null, InstanceHealth.Unknown)]
[InlineData(0, InstanceHealth.Healthy)]
[InlineData(499, InstanceHealth.Healthy)]
[InlineData(500, InstanceHealth.Warning)]
[InlineData(999, InstanceHealth.Warning)]
[InlineData(1000, InstanceHealth.Critical)]
[InlineData(5000, InstanceHealth.Critical)]
public async Task GetVlfStatus_應依VLF數量正確分級(int? vlfCount, InstanceHealth expected)
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetVlfCountsAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
        .Returns(new List<VlfRow> { new() { DatabaseName = "DB", VlfCount = vlfCount, LogSizeMB = 100 } });

    var svc = new PerformanceDiagnosticsService(repo);
    var results = await svc.GetVlfStatusAsync();

    results.Single().Health.Should().Be(expected);
}

[Theory]
// (DataFileCount, CpuCount, EqualSize, expected)
[InlineData(8, 8, true, InstanceHealth.Healthy)]      // 完全 OK
[InlineData(4, 8, true, InstanceHealth.Warning)]      // 檔案數不足
[InlineData(8, 8, false, InstanceHealth.Warning)]     // 大小不一致
[InlineData(4, 8, false, InstanceHealth.Critical)]    // 多項不符
[InlineData(8, 16, true, InstanceHealth.Healthy)]     // CPU>8 取上限 8
public async Task GetTempDbConfiguration_應依檔案數與一致性分級(
    int dataFiles, int cpuCount, bool equalSize, InstanceHealth expected)
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetTempDbConfigurationAsync(Arg.Any<CancellationToken>())
        .Returns(new TempDbConfigurationRaw
        {
            DataFileCount = dataFiles,
            CpuCount = cpuCount,
            AllFilesEqualSize = equalSize,
            SqlMajorVersion = 16
        });

    var svc = new PerformanceDiagnosticsService(repo);
    var result = await svc.GetTempDbConfigurationAsync();

    result.Health.Should().Be(expected);
    result.RecommendedFileCount.Should().Be(Math.Min(cpuCount, 8));
    result.TfAutoEnabled.Should().BeTrue();   // major=16
}

[Fact]
public async Task GetTempDbConfiguration_當SqlMajorVersion小於13_TfAutoEnabled應為False()
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetTempDbConfigurationAsync(Arg.Any<CancellationToken>())
        .Returns(new TempDbConfigurationRaw
        {
            DataFileCount = 8, CpuCount = 8, AllFilesEqualSize = true, SqlMajorVersion = 12
        });

    var result = await new PerformanceDiagnosticsService(repo).GetTempDbConfigurationAsync();
    result.TfAutoEnabled.Should().BeFalse();
}

[Theory]
// (CurrentMB, OsTotalMB, expectedHealth)
[InlineData(2147483647L, 16384L, InstanceHealth.Critical)]  // Unlimited
[InlineData(12288L, 16384L, InstanceHealth.Healthy)]         // 12GB <= 14336 推薦
[InlineData(15000L, 16384L, InstanceHealth.Warning)]         // 超過建議
public async Task GetMaxServerMemory_應依設定與建議分級(long current, long os, InstanceHealth expected)
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetMaxServerMemoryAsync(Arg.Any<CancellationToken>())
        .Returns(new MaxServerMemoryConfigurationRaw { CurrentMB = current, OsTotalMB = os });

    var result = await new PerformanceDiagnosticsService(repo).GetMaxServerMemoryAsync();
    result.Health.Should().Be(expected);
}

[Theory]
// (osTotalMB, expectedRecommended)
[InlineData(8192L, 6144L)]      // 10% = 819 MB < 2GB,故扣 2GB
[InlineData(16384L, 14336L)]    // 10% = 1638 MB < 2GB,故扣 2GB
[InlineData(32768L, 30720L)]    // 10% = 3276 MB > 2GB,故扣 2GB(等等,3276>2048,應扣 3276?)
[InlineData(131072L, 117965L)]  // 128 GB:10% = 13107 > 2048,扣 13107
public async Task GetMaxServerMemory_RecommendedMB_應為OS扣除max2GB或10pct(long os, long expected)
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetMaxServerMemoryAsync(Arg.Any<CancellationToken>())
        .Returns(new MaxServerMemoryConfigurationRaw { CurrentMB = 1024, OsTotalMB = os });

    var result = await new PerformanceDiagnosticsService(repo).GetMaxServerMemoryAsync();
    result.RecommendedMB.Should().Be(expected);
}
```

> 第三組 InlineData 我寫錯了:32 GB 的 10% = 3276 MB > 2048,所以建議扣 3276,結果為 32768-3276 = 29492。請使用 `[InlineData(32768L, 29492L)]`。

修正後的第三組:`[InlineData(32768L, 29492L)]`

確保 `using Specurai.Domain.Entities;` 存在。

- [ ] **Step 3:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~GetVlfStatus|FullyQualifiedName~GetTempDbConfiguration|FullyQualifiedName~GetMaxServerMemory"`
Expected: FAIL

- [ ] **Step 4:Service 實作**

於 `PerformanceDiagnosticsService.cs` 類別尾端追加:

```csharp
public async Task<IReadOnlyList<VlfStatus>> GetVlfStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default)
{
    var rows = await _repository.GetVlfCountsAsync(progress, ct);
    return rows.Select(r => new VlfStatus
    {
        DatabaseName = r.DatabaseName,
        VlfCount = r.VlfCount,
        LogSizeMB = r.LogSizeMB,
        Health = ClassifyVlfHealth(r.VlfCount)
    }).ToList();
}

public async Task<TempDbConfiguration> GetTempDbConfigurationAsync(CancellationToken ct = default)
{
    var raw = await _repository.GetTempDbConfigurationAsync(ct);
    var recommended = Math.Min(raw.CpuCount, 8);
    return new TempDbConfiguration
    {
        DataFileCount = raw.DataFileCount,
        CpuCount = raw.CpuCount,
        RecommendedFileCount = recommended,
        AllFilesEqualSize = raw.AllFilesEqualSize,
        TfAutoEnabled = raw.SqlMajorVersion >= 13,
        Health = ClassifyTempDbHealth(raw.DataFileCount, recommended, raw.AllFilesEqualSize)
    };
}

public async Task<MaxServerMemoryConfiguration> GetMaxServerMemoryAsync(CancellationToken ct = default)
{
    var raw = await _repository.GetMaxServerMemoryAsync(ct);
    var recommended = CalcMaxMemRecommended(raw.OsTotalMB);
    return new MaxServerMemoryConfiguration
    {
        CurrentMB = raw.CurrentMB,
        OsTotalMB = raw.OsTotalMB,
        RecommendedMB = recommended,
        Health = ClassifyMaxMemHealth(raw.CurrentMB, recommended)
    };
}

private static InstanceHealth ClassifyVlfHealth(int? count) => count switch
{
    null => InstanceHealth.Unknown,
    < 500 => InstanceHealth.Healthy,
    < 1000 => InstanceHealth.Warning,
    _ => InstanceHealth.Critical
};

private static InstanceHealth ClassifyTempDbHealth(int actual, int recommended, bool equalSize)
{
    int issues = 0;
    if (actual < recommended) issues++;
    if (!equalSize) issues++;
    return issues switch
    {
        0 => InstanceHealth.Healthy,
        1 => InstanceHealth.Warning,
        _ => InstanceHealth.Critical
    };
}

private static InstanceHealth ClassifyMaxMemHealth(long current, long recommended) =>
    current == 2147483647 ? InstanceHealth.Critical
    : current <= recommended ? InstanceHealth.Healthy
    : InstanceHealth.Warning;

/// <summary>建議 Max Memory = OS 總記憶體 - max(2GB, 10%)</summary>
private static long CalcMaxMemRecommended(long osTotalMB) =>
    osTotalMB - Math.Max(2048L, osTotalMB / 10);
```

確保 `using Specurai.Domain.Entities;` 與 `using System;` 存在。

- [ ] **Step 5:執行所有測試 PASS**

Run: `dotnet test`
Expected: 全 PASS(新增約 22 筆)

- [ ] **Step 6:Commit**

```bash
git add src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs src/Specurai.Application/Services/PerformanceDiagnosticsService.cs tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs
git commit -m "feat(application): PerformanceDiagnosticsService 新增實例健康三個方法 + 健康分類"
```

---

## Task 7:Desktop ViewModel — 集合 + 單值欄位 + Command

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs`
- Modify: `tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs`

- [ ] **Step 1:寫失敗測試**

於 `PerformanceDiagnosticsDocumentViewModelTests` 追加:

```csharp
[Fact]
public void 設計時建構_應有實例健康空狀態()
{
    var vm = new PerformanceDiagnosticsDocumentViewModel();
    vm.VlfStatuses.Should().BeEmpty();
    vm.TempDbConfig.Should().BeNull();
    vm.MaxMemConfig.Should().BeNull();
    vm.IsLoadingInstance.Should().BeFalse();
    vm.RunInstanceHealthAnalysisCommand.Should().NotBeNull();
}
```

- [ ] **Step 2:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~設計時建構_應有實例健康空狀態"`
Expected: FAIL

- [ ] **Step 3:加入新 region(於 `#region 完整性檢查` 之後)**

```csharp
#region 實例健康

public ObservableCollection<VlfStatus> VlfStatuses { get; } = [];

[ObservableProperty]
private TempDbConfiguration? _tempDbConfig;

[ObservableProperty]
private MaxServerMemoryConfiguration? _maxMemConfig;

[ObservableProperty]
private bool _isLoadingInstance;

[ObservableProperty]
private string _instanceProgressMessage = string.Empty;

[RelayCommand]
private async Task RunInstanceHealthAnalysisAsync()
{
    if (_service is null) return;

    IsLoadingInstance = true;
    InstanceProgressMessage = "開始載入實例健康資料...";
    VlfStatuses.Clear();
    TempDbConfig = null;
    MaxMemConfig = null;

    _cancellationTokenSource = new CancellationTokenSource();
    try
    {
        var progress = new Progress<string>(m => InstanceProgressMessage = m);

        var vlfTask = _service.GetVlfStatusAsync(progress, _cancellationTokenSource.Token);
        var tempDbTask = _service.GetTempDbConfigurationAsync(_cancellationTokenSource.Token);
        var maxMemTask = _service.GetMaxServerMemoryAsync(_cancellationTokenSource.Token);

        await Task.WhenAll(vlfTask, tempDbTask, maxMemTask);

        foreach (var s in await vlfTask) VlfStatuses.Add(s);
        TempDbConfig = await tempDbTask;
        MaxMemConfig = await maxMemTask;

        InstanceProgressMessage = $"完成:{VlfStatuses.Count} 個 DB 的 VLF / TempDB {TempDbConfig?.DataFileCount} 檔 / Max Memory {(MaxMemConfig?.IsUnlimited == true ? "未設定" : MaxMemConfig?.CurrentMB + " MB")}";
    }
    catch (OperationCanceledException)
    {
        InstanceProgressMessage = "已取消";
    }
    catch (Exception ex)
    {
        InstanceProgressMessage = $"載入失敗:{ex.Message}";
    }
    finally
    {
        IsLoadingInstance = false;
    }
}

#endregion
```

確保檔案頂端 `using Specurai.Domain.Entities;` 已存在(上次 spec 已加)。

- [ ] **Step 4:執行所有測試 PASS**

Run: `dotnet test`
Expected: 全 PASS

- [ ] **Step 5:Commit**

```bash
git add src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs
git commit -m "feat(desktop): PerformanceDiagnostics ViewModel 加入實例健康集合與 Command"
```

---

## Task 8:Desktop View — TabItem「實例健康」

**Files:**
- Modify: `src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml`

- [ ] **Step 1:Grep 定位插入點**

執行 `Grep "完整性檢查" PerformanceDiagnosticsDocumentView.axaml` 找到該 TabItem 結尾的 `</TabItem>`。新 TabItem 插在其後、`</TabControl>` 之前。

- [ ] **Step 2:插入新 TabItem**

```xml
<!-- 實例健康分頁 -->
<TabItem Header="實例健康">
    <Grid RowDefinitions="Auto,Auto,*">
        <!-- 工具列 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="10" Margin="10">
            <Button Content="重新整理"
                    Command="{Binding RunInstanceHealthAnalysisCommand}"
                    IsEnabled="{Binding !IsLoadingInstance}"/>
            <TextBlock Text="{Binding InstanceProgressMessage}"
                       VerticalAlignment="Center" Opacity="0.7"/>
        </StackPanel>

        <!-- 說明 -->
        <Expander Grid.Row="1" Header="實例健康說明（點擊展開）" Margin="10,0,10,5">
            <StackPanel Spacing="4" Margin="10">
                <TextBlock Text="本頁整合三項實例層級的組態健康檢查：" TextWrapping="Wrap"/>
                <TextBlock Text="• VLF 數量：交易記錄檔的虛擬記錄檔數量；&lt;500 為健康，&gt;1000 會拖慢備份/還原/啟動" TextWrapping="Wrap"/>
                <TextBlock Text="• TempDB 配置：檔案數應約 = CPU 邏輯核心數（上限 8）且各檔大小一致" TextWrapping="Wrap"/>
                <TextBlock Text="• Max Server Memory：建議預留 max(2GB, 10%) 給 OS 與其他服務" TextWrapping="Wrap"/>
            </StackPanel>
        </Expander>

        <!-- 三段內容 -->
        <ScrollViewer Grid.Row="2" Margin="10,0,10,10">
            <StackPanel Spacing="10">
                <Expander Header="📊 VLF 數量（每資料庫）" IsExpanded="True">
                    <DataGrid ItemsSource="{Binding VlfStatuses}" AutoGenerateColumns="False"
                              IsReadOnly="True" CanUserResizeColumns="True" MaxHeight="320">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Database" Binding="{Binding DatabaseName}" Width="220"/>
                            <DataGridTextColumn Header="VLF Count" Binding="{Binding VlfCount}" Width="120"/>
                            <DataGridTextColumn Header="Log Size MB" Binding="{Binding LogSizeMB}" Width="120"/>
                            <DataGridTextColumn Header="健康" Binding="{Binding Health}" Width="100"/>
                        </DataGrid.Columns>
                    </DataGrid>
                </Expander>

                <Expander Header="🗂️ TempDB 配置" IsExpanded="True">
                    <Grid Margin="10" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto"
                          ColumnDefinitions="200,*"
                          IsVisible="{Binding TempDbConfig, Converter={x:Static ObjectConverters.IsNotNull}}">
                        <TextBlock Grid.Row="0" Grid.Column="0" Text="資料檔數量" FontWeight="Bold"/>
                        <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding TempDbConfig.DataFileCount}"/>
                        <TextBlock Grid.Row="1" Grid.Column="0" Text="CPU 邏輯核心數" FontWeight="Bold"/>
                        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding TempDbConfig.CpuCount}"/>
                        <TextBlock Grid.Row="2" Grid.Column="0" Text="建議檔案數" FontWeight="Bold"/>
                        <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding TempDbConfig.RecommendedFileCount}"/>
                        <TextBlock Grid.Row="3" Grid.Column="0" Text="各檔大小一致" FontWeight="Bold"/>
                        <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding TempDbConfig.AllFilesEqualSize}"/>
                        <TextBlock Grid.Row="4" Grid.Column="0" Text="TF1117/1118 自動啟用" FontWeight="Bold"/>
                        <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding TempDbConfig.TfAutoEnabled}"/>
                        <TextBlock Grid.Row="5" Grid.Column="0" Text="健康" FontWeight="Bold"/>
                        <TextBlock Grid.Row="5" Grid.Column="1" Text="{Binding TempDbConfig.Health}"/>
                    </Grid>
                </Expander>

                <Expander Header="💾 Max Server Memory" IsExpanded="True">
                    <Grid Margin="10" RowDefinitions="Auto,Auto,Auto,Auto"
                          ColumnDefinitions="200,*"
                          IsVisible="{Binding MaxMemConfig, Converter={x:Static ObjectConverters.IsNotNull}}">
                        <TextBlock Grid.Row="0" Grid.Column="0" Text="目前設定 (MB)" FontWeight="Bold"/>
                        <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="10">
                            <TextBlock Text="{Binding MaxMemConfig.CurrentMB}"/>
                            <TextBlock Text="（未設定，無限制）" Foreground="Red"
                                       IsVisible="{Binding MaxMemConfig.IsUnlimited}"/>
                        </StackPanel>
                        <TextBlock Grid.Row="1" Grid.Column="0" Text="OS 總記憶體 (MB)" FontWeight="Bold"/>
                        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding MaxMemConfig.OsTotalMB}"/>
                        <TextBlock Grid.Row="2" Grid.Column="0" Text="建議值 (MB)" FontWeight="Bold"/>
                        <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding MaxMemConfig.RecommendedMB}"/>
                        <TextBlock Grid.Row="3" Grid.Column="0" Text="健康" FontWeight="Bold"/>
                        <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding MaxMemConfig.Health}"/>
                    </Grid>
                </Expander>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</TabItem>
```

- [ ] **Step 3:Build + tests**

Run: `dotnet build && dotnet test`
Expected: 全 PASS

- [ ] **Step 4:Commit**

```bash
git add src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml
git commit -m "feat(desktop): 效能診斷新增實例健康分頁(VLF/TempDB/MaxMem)"
```

---

## Task 9:全測試 + Code Review

- [ ] **Step 1:全測試**

Run: `dotnet test`
Expected: 全 PASS

- [ ] **Step 2:Code Review** — 透過 `superpowers:requesting-code-review` 對本批 8 commit 審查。

審查重點:
- Repository 純查詢 / Service 算 Health
- VLF SQL 用 `database_id` 直接代入(int 安全)
- 各健康分類純函式邊界正確
- ViewModel 設計時建構 + 並行 Task.WhenAll
- TempDB 與 MaxMem 為單值,UI 用 `Converter={x:Static ObjectConverters.IsNotNull}` 控制可見性

- [ ] **Step 3:依審查回饋修正,每修一項 commit 一次**

---

## Self-Review

- ✅ Spec 三區塊皆有對應 task:VLF(T3, T6)、TempDB(T4, T6)、MaxMem(T5, T6)
- ✅ Domain Entity 7 個齊備(T1)
- ✅ Repository 介面與實作(T2-T5)
- ✅ Service Health 分類純函式可單測(T6)涵蓋邊界
- ✅ ViewModel 設計時建構 + Command + 並行(T7)
- ✅ View 三段:VLF DataGrid + TempDB Grid + MaxMem Grid + 空狀態(T8)
- ✅ 無 placeholder
- ✅ 型別命名一致:`InstanceHealth`、`VlfStatus`、`VlfRow`、`TempDbConfiguration`、`TempDbConfigurationRaw`、`MaxServerMemoryConfiguration`、`MaxServerMemoryConfigurationRaw`
