# 效能診斷 — 完整性檢查分頁 實作計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在「效能診斷」TabControl 新增「完整性檢查」分頁,提供三段資料庫完整性快照(最後 CHECKDB 時間 / Suspect Pages / CheckDB Job 紀錄)。

**Architecture:** 沿用既有效能診斷功能的 Repository → Service → ViewModel → View 鏈路。新增三個 Domain Entity、擴充 `IPerformanceDiagnosticsRepository`/`IPerformanceDiagnosticsService` 各三個 query method、ViewModel 加三個集合 + 一個 Command,View 加一個 TabItem 內含三個 DataGrid。

**Tech Stack:** .NET 8、CommunityToolkit.Mvvm、Avalonia 11、Dapper、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions

**Spec:** `docs/superpowers/specs/2026-05-15-performance-diagnostics-integrity-check-design.md`

---

## File Structure

| 動作 | 檔案 | 責任 |
|------|------|------|
| Create | `src/Specurai.Domain/Entities/IntegrityCheckStatus.cs` | 單一 DB 的最後 CHECKDB 時間 + 健康分級 + 健康 enum |
| Create | `src/Specurai.Domain/Entities/SuspectPage.cs` | msdb.dbo.suspect_pages 的單筆紀錄 + EventType 解碼 |
| Create | `src/Specurai.Domain/Entities/CheckDbJobHistory.cs` | sysjobhistory 篩選 CheckDb Job 的單筆紀錄 + 狀態解碼 |
| Modify | `src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs` | 新增 3 個查詢方法 |
| Modify | `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs` | 實作 3 個查詢方法 |
| Modify | `src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs` | 介面新增 3 個對應方法(Service 多了 Health 分類邏輯) |
| Modify | `src/Specurai.Application/Services/PerformanceDiagnosticsService.cs` | 實作 + Health 分類私有方法 |
| Modify | `src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs` | 三個 ObservableCollection、IsLoading 旗標、HasSuspectPages 計算屬性、RunIntegrityCheckAnalysisCommand |
| Modify | `src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml` | 在「錯誤記錄」TabItem 之後追加新 TabItem「完整性檢查」 |
| Create | `tests/Specurai.Domain.Tests/Entities/IntegrityCheckStatusTests.cs` | EventType 解碼/Health 屬性測試 |
| Create | `tests/Specurai.Domain.Tests/Entities/SuspectPageTests.cs` | EventTypeText 解碼測試 |
| Create | `tests/Specurai.Domain.Tests/Entities/CheckDbJobHistoryTests.cs` | StatusText 解碼測試 |
| Modify | `tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs` | Health 分類邊界測試 + 三個轉發方法測試 |
| Modify | `tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs` | 設計時建構、Command、HasSuspectPages 變化 |

---

## Task 1:Domain — 三個 Entity 與 Enum

**Files:**
- Create: `src/Specurai.Domain/Entities/IntegrityCheckStatus.cs`
- Create: `src/Specurai.Domain/Entities/SuspectPage.cs`
- Create: `src/Specurai.Domain/Entities/CheckDbJobHistory.cs`
- Create: `tests/Specurai.Domain.Tests/Entities/IntegrityCheckStatusTests.cs`
- Create: `tests/Specurai.Domain.Tests/Entities/SuspectPageTests.cs`
- Create: `tests/Specurai.Domain.Tests/Entities/CheckDbJobHistoryTests.cs`

- [ ] **Step 1:寫失敗測試 — IntegrityCheckStatusTests**

```csharp
// tests/Specurai.Domain.Tests/Entities/IntegrityCheckStatusTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class IntegrityCheckStatusTests
{
    [Fact]
    public void 建立_所有屬性正確設定()
    {
        var s = new IntegrityCheckStatus
        {
            DatabaseName = "DB",
            LastKnownGood = new DateTime(2026, 5, 1),
            DaysSince = 14,
            Health = IntegrityHealth.Warning
        };
        s.DatabaseName.Should().Be("DB");
        s.Health.Should().Be(IntegrityHealth.Warning);
    }

    [Fact]
    public void 從未檢查_LastKnownGood可為null()
    {
        var s = new IntegrityCheckStatus { DatabaseName = "DB", Health = IntegrityHealth.Critical };
        s.LastKnownGood.Should().BeNull();
        s.DaysSince.Should().BeNull();
    }
}
```

- [ ] **Step 2:寫失敗測試 — SuspectPageTests**

```csharp
// tests/Specurai.Domain.Tests/Entities/SuspectPageTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class SuspectPageTests
{
    [Theory]
    [InlineData(1, "824 錯誤")]
    [InlineData(2, "不正常 shutdown")]
    [InlineData(3, "校驗失敗")]
    [InlineData(4, "已從備份還原")]
    [InlineData(5, "已修復")]
    [InlineData(7, "已 deallocate")]
    [InlineData(99, "未知 (99)")]
    public void EventTypeText_應依raw值正確解碼(int raw, string expected)
    {
        var p = new SuspectPage
        {
            DatabaseName = "DB", FileId = 1, PageId = 100,
            EventTypeRaw = raw, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow
        };
        p.EventTypeText.Should().Be(expected);
    }
}
```

- [ ] **Step 3:寫失敗測試 — CheckDbJobHistoryTests**

```csharp
// tests/Specurai.Domain.Tests/Entities/CheckDbJobHistoryTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class CheckDbJobHistoryTests
{
    [Theory]
    [InlineData(1, "成功")]
    [InlineData(0, "失敗")]
    [InlineData(3, "取消")]
    [InlineData(4, "重試")]
    [InlineData(99, "其他")]
    public void StatusText_應依RunStatus正確解碼(int status, string expected)
    {
        var h = new CheckDbJobHistory
        {
            JobName = "DB_CheckDb",
            RunAt = new DateTime(2026, 5, 14, 3, 0, 0),
            Duration = TimeSpan.FromMinutes(2),
            RunStatus = status,
            Message = ""
        };
        h.StatusText.Should().Be(expected);
    }
}
```

- [ ] **Step 4:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~IntegrityCheckStatusTests|FullyQualifiedName~SuspectPageTests|FullyQualifiedName~CheckDbJobHistoryTests"`
Expected: FAIL — 找不到型別

- [ ] **Step 5:建立三個 Entity 檔案**

```csharp
// src/Specurai.Domain/Entities/IntegrityCheckStatus.cs
namespace Specurai.Domain.Entities;

/// <summary>資料庫完整性健康分級</summary>
public enum IntegrityHealth
{
    Healthy,    // <14 天
    Warning,    // 14-30 天
    Critical,   // >30 天 或從未
    Unknown     // 查詢失敗(權限/錯誤)
}

/// <summary>單一資料庫的 CHECKDB 健康狀態</summary>
public class IntegrityCheckStatus
{
    public required string DatabaseName { get; init; }
    /// <summary>最後一次成功 CHECKDB 的時間;null 表示從未或無法判斷</summary>
    public DateTime? LastKnownGood { get; init; }
    /// <summary>距今天數;null 表示無資料</summary>
    public int? DaysSince { get; init; }
    public required IntegrityHealth Health { get; init; }
}
```

```csharp
// src/Specurai.Domain/Entities/SuspectPage.cs
namespace Specurai.Domain.Entities;

/// <summary>msdb.dbo.suspect_pages 單筆紀錄</summary>
public class SuspectPage
{
    public required string DatabaseName { get; init; }
    public required int FileId { get; init; }
    public required long PageId { get; init; }
    /// <summary>原始 event_type 數值</summary>
    public required int EventTypeRaw { get; init; }
    public required int ErrorCount { get; init; }
    public required DateTime LastUpdateDate { get; init; }

    /// <summary>event_type 中文解碼</summary>
    public string EventTypeText => EventTypeRaw switch
    {
        1 => "824 錯誤",
        2 => "不正常 shutdown",
        3 => "校驗失敗",
        4 => "已從備份還原",
        5 => "已修復",
        7 => "已 deallocate",
        _ => $"未知 ({EventTypeRaw})"
    };
}
```

```csharp
// src/Specurai.Domain/Entities/CheckDbJobHistory.cs
namespace Specurai.Domain.Entities;

/// <summary>CHECKDB SQL Agent Job 的單筆執行紀錄</summary>
public class CheckDbJobHistory
{
    public required string JobName { get; init; }
    public required DateTime RunAt { get; init; }
    public required TimeSpan Duration { get; init; }
    /// <summary>原始 run_status:1=成功 0=失敗 3=取消 4=重試</summary>
    public required int RunStatus { get; init; }
    public required string Message { get; init; }

    public string StatusText => RunStatus switch
    {
        1 => "成功",
        0 => "失敗",
        3 => "取消",
        4 => "重試",
        _ => "其他"
    };
}
```

- [ ] **Step 6:執行測試確認 PASS**

Run: `dotnet test tests/Specurai.Domain.Tests`
Expected: 全部 PASS(新增 9 筆)

- [ ] **Step 7:Commit**

```bash
git add src/Specurai.Domain/Entities/IntegrityCheckStatus.cs src/Specurai.Domain/Entities/SuspectPage.cs src/Specurai.Domain/Entities/CheckDbJobHistory.cs tests/Specurai.Domain.Tests/Entities/IntegrityCheckStatusTests.cs tests/Specurai.Domain.Tests/Entities/SuspectPageTests.cs tests/Specurai.Domain.Tests/Entities/CheckDbJobHistoryTests.cs
git commit -m "feat(domain): 新增完整性檢查相關 entity (IntegrityCheckStatus/SuspectPage/CheckDbJobHistory)"
```

---

## Task 2:Repository 介面 — 三個查詢方法

**Files:**
- Modify: `src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:在介面尾端追加三個方法**

在 `ExecuteDropIndexAsync` 之後追加:

```csharp
/// <summary>
/// 取得各資料庫最後一次成功 CHECKDB 的時間
/// </summary>
/// <param name="progress">進度回報(每處理一個 DB 回報一次)</param>
/// <param name="ct">取消權杖</param>
Task<IReadOnlyList<IntegrityCheckStatus>> GetLastCheckDbAsync(IProgress<string>? progress = null, CancellationToken ct = default);

/// <summary>
/// 取得 msdb.dbo.suspect_pages 內容(疑似損毀頁面)
/// </summary>
Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default);

/// <summary>
/// 取得 CHECKDB Job 的執行歷史(僅 step_id=0 整體結果)
/// </summary>
/// <param name="top">最近 N 筆(預設 50)</param>
Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default);
```

> 注意:`IntegrityCheckStatus` 由 Service 層計算 Health 分級。但由於 Repository 介面回傳型別需要 Health,Repository 將以 `IntegrityHealth.Unknown` 作為佔位回傳,讓 Service 後續覆寫。**為了保持 Repository 純查詢職責**,改成下列做法:Repository 不回 `IntegrityCheckStatus`,改回新 DTO `LastCheckDbRow { string DatabaseName; DateTime? LastKnownGood; }`。

修改方法簽章為:

```csharp
/// <summary>
/// 取得各資料庫最後一次成功 CHECKDB 的時間(原始資料,Health 分級由 Service 計算)
/// </summary>
Task<IReadOnlyList<LastCheckDbRow>> GetLastCheckDbAsync(IProgress<string>? progress = null, CancellationToken ct = default);
```

並在 `Specurai.Domain/Entities/` 新增:

```csharp
// src/Specurai.Domain/Entities/LastCheckDbRow.cs
namespace Specurai.Domain.Entities;

/// <summary>單一 DB 的 CHECKDB 原始查詢結果</summary>
public class LastCheckDbRow
{
    public required string DatabaseName { get; init; }
    public DateTime? LastKnownGood { get; init; }
}
```

- [ ] **Step 2:建置(會 fail 在 Repository 實作)**

Run: `dotnet build`
Expected: FAIL — `PerformanceDiagnosticsRepository` 未實作三個新方法。預期錯誤,Task 3-5 修復。

- [ ] **Step 3:Commit(獨立 commit 介面變更 + 新 DTO)**

```bash
git add src/Specurai.Domain/Interfaces/IPerformanceDiagnosticsRepository.cs src/Specurai.Domain/Entities/LastCheckDbRow.cs
git commit -m "feat(domain): IPerformanceDiagnosticsRepository 新增完整性檢查三個查詢方法"
```

---

## Task 3:Infrastructure — GetLastCheckDbAsync 實作

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:Read 既有 Repository 找到適合插入位置**

開啟 `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`,確認 `using` 區塊與 `_connectionStringProvider` 用法。

- [ ] **Step 2:在類別尾端新增方法 GetLastCheckDbAsync**

```csharp
public async Task<IReadOnlyList<LastCheckDbRow>> GetLastCheckDbAsync(IProgress<string>? progress = null, CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    // 取得所有 ONLINE 的 user database(排除 tempdb,但保留 master/model/msdb 因 DBA 通常也想知道)
    const string listDbSql = @"
SELECT name FROM sys.databases
WHERE state = 0 AND database_id <> 2
ORDER BY database_id;";
    var dbs = (await conn.QueryAsync<string>(new CommandDefinition(listDbSql, cancellationToken: ct))).ToList();

    var results = new List<LastCheckDbRow>();
    int idx = 0;
    foreach (var db in dbs)
    {
        ct.ThrowIfCancellationRequested();
        idx++;
        progress?.Report($"檢查 ({idx}/{dbs.Count}): {db}");

        DateTime? lastKnownGood = null;
        try
        {
            // DBC DBINFO 必須在目標 DB context 執行;用動態 SQL + 暫存表收集
            var sql = $@"
SET NOCOUNT ON;
DECLARE @v TABLE (ParentObject NVARCHAR(255), [Object] NVARCHAR(255), Field NVARCHAR(255), [Value] NVARCHAR(255));
INSERT INTO @v EXEC ('USE [{db.Replace("]", "]]")}]; DBCC DBINFO WITH TABLERESULTS, NO_INFOMSGS');
SELECT TOP 1 [Value] FROM @v WHERE Field = 'dbi_dbccLastKnownGood';";
            var raw = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, cancellationToken: ct));
            if (!string.IsNullOrEmpty(raw) && raw != "1900-01-01 00:00:00.000"
                && DateTime.TryParse(raw, out var parsed))
            {
                lastKnownGood = parsed;
            }
        }
        catch
        {
            // 忽略:單一 DB 失敗(權限/離線/等)不應中斷整體
            lastKnownGood = null;
        }

        results.Add(new LastCheckDbRow { DatabaseName = db, LastKnownGood = lastKnownGood });
    }

    return results;
}
```

> `1900-01-01` 是 SQL Server 對「從未做過 CHECKDB」的預設值。

- [ ] **Step 3:建置**

Run: `dotnet build`
Expected: 仍 FAIL(其他兩個方法未實作),但 GetLastCheckDbAsync 應已編譯通過。

> 暫時為其他兩個方法加上 stub 以保持 build green:

```csharp
public Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default)
    => throw new NotImplementedException();

public Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default)
    => throw new NotImplementedException();
```

加上 stub 後:

Run: `dotnet build`
Expected: SUCCESS

- [ ] **Step 4:執行所有測試確認無回歸**

Run: `dotnet test`
Expected: 既有測試全部 PASS(新方法尚無測試)

- [ ] **Step 5:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetLastCheckDbAsync;其他完整性查詢方法暫以 stub 保持 build green"
```

---

## Task 4:Infrastructure — GetSuspectPagesAsync 實作

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:替換 GetSuspectPagesAsync stub**

將 stub 替換為:

```csharp
public async Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT
    DB_NAME(database_id) AS DatabaseName,
    file_id              AS FileId,
    page_id              AS PageId,
    event_type           AS EventTypeRaw,
    error_count          AS ErrorCount,
    last_update_date     AS LastUpdateDate
FROM msdb.dbo.suspect_pages
ORDER BY last_update_date DESC;";

    var rows = await conn.QueryAsync<SuspectPage>(new CommandDefinition(sql, cancellationToken: ct));
    return rows.ToList();
}
```

> Dapper 會自動把 `DatabaseName`/`FileId`/`PageId`/`EventTypeRaw`/`ErrorCount`/`LastUpdateDate` 對應到 `SuspectPage` 的 `init` 屬性。

- [ ] **Step 2:建置 + 執行所有測試**

Run: `dotnet build && dotnet test`
Expected: 全部 PASS

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetSuspectPagesAsync 查詢 msdb 疑似損毀頁面"
```

---

## Task 5:Infrastructure — GetCheckDbJobHistoryAsync 實作

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs`

- [ ] **Step 1:替換 GetCheckDbJobHistoryAsync stub**

```csharp
public async Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    var sql = $@"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT TOP ({top})
    j.name                                                       AS JobName,
    -- run_date YYYYMMDD + run_time HHMMSS 合併
    msdb.dbo.agent_datetime(h.run_date, h.run_time)              AS RunAt,
    h.run_duration                                               AS DurationRaw,
    h.run_status                                                 AS RunStatus,
    h.message                                                    AS Message
FROM msdb.dbo.sysjobhistory h
JOIN msdb.dbo.sysjobs j ON h.job_id = j.job_id
WHERE j.name LIKE '%CheckDb%'
  AND h.step_id = 0
ORDER BY h.run_date DESC, h.run_time DESC;";

    var rows = await conn.QueryAsync<CheckDbJobHistoryRow>(new CommandDefinition(sql, cancellationToken: ct));
    return rows.Select(r => new CheckDbJobHistory
    {
        JobName = r.JobName,
        RunAt = r.RunAt,
        Duration = ParseSqlAgentDuration(r.DurationRaw),
        RunStatus = r.RunStatus,
        Message = r.Message ?? string.Empty
    }).ToList();
}

/// <summary>SQL Agent 的 run_duration 為 HHMMSS 整數(例 215 = 0:02:15)</summary>
private static TimeSpan ParseSqlAgentDuration(int hhmmss)
{
    var h = hhmmss / 10000;
    var m = (hhmmss / 100) % 100;
    var s = hhmmss % 100;
    return new TimeSpan(h, m, s);
}

private sealed class CheckDbJobHistoryRow
{
    public string JobName { get; set; } = string.Empty;
    public DateTime RunAt { get; set; }
    public int DurationRaw { get; set; }
    public int RunStatus { get; set; }
    public string? Message { get; set; }
}
```

- [ ] **Step 2:建置 + 執行所有測試**

Run: `dotnet build && dotnet test`
Expected: 全部 PASS

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/PerformanceDiagnosticsRepository.cs
git commit -m "feat(infra): 實作 GetCheckDbJobHistoryAsync 查詢 CHECKDB Job 執行歷史"
```

---

## Task 6:Application — Service 介面與實作

**Files:**
- Modify: `src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs`
- Modify: `src/Specurai.Application/Services/PerformanceDiagnosticsService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs`

- [ ] **Step 1:介面新增三個方法**

在 `IPerformanceDiagnosticsService.cs` 尾端追加:

```csharp
/// <summary>
/// 取得各資料庫的 CHECKDB 健康狀態(含分級)
/// </summary>
Task<IReadOnlyList<IntegrityCheckStatus>> GetIntegrityCheckStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default);

/// <summary>
/// 取得 msdb.dbo.suspect_pages 內容
/// </summary>
Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default);

/// <summary>
/// 取得 CHECKDB Job 執行歷史
/// </summary>
Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default);
```

- [ ] **Step 2:寫失敗測試 — Health 分類邊界**

在 `PerformanceDiagnosticsServiceTests` 追加:

```csharp
[Theory]
[InlineData(null, IntegrityHealth.Critical)]
[InlineData(0, IntegrityHealth.Healthy)]
[InlineData(13, IntegrityHealth.Healthy)]
[InlineData(14, IntegrityHealth.Warning)]
[InlineData(29, IntegrityHealth.Warning)]
[InlineData(30, IntegrityHealth.Critical)]
[InlineData(100, IntegrityHealth.Critical)]
public async Task GetIntegrityCheckStatus_應依距今天數正確分級(int? days, IntegrityHealth expected)
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    var rows = new List<LastCheckDbRow>
    {
        new()
        {
            DatabaseName = "DB",
            LastKnownGood = days.HasValue ? DateTime.UtcNow.Date.AddDays(-days.Value) : null
        }
    };
    repo.GetLastCheckDbAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(rows);

    var svc = new PerformanceDiagnosticsService(repo);
    var results = await svc.GetIntegrityCheckStatusAsync();

    results.Single().Health.Should().Be(expected);
}

[Fact]
public async Task GetSuspectPages_應直接轉發()
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    var data = new List<SuspectPage>
    {
        new() { DatabaseName = "X", FileId = 1, PageId = 100, EventTypeRaw = 3, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow }
    };
    repo.GetSuspectPagesAsync(Arg.Any<CancellationToken>()).Returns(data);

    var svc = new PerformanceDiagnosticsService(repo);
    var results = await svc.GetSuspectPagesAsync();

    results.Should().BeEquivalentTo(data);
}

[Fact]
public async Task GetCheckDbJobHistory_應傳遞top參數()
{
    var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
    repo.GetCheckDbJobHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<CheckDbJobHistory>());

    var svc = new PerformanceDiagnosticsService(repo);
    await svc.GetCheckDbJobHistoryAsync(20);

    await repo.Received(1).GetCheckDbJobHistoryAsync(20, Arg.Any<CancellationToken>());
}
```

確保檔案頂端 `using Specurai.Domain.Entities;`。

- [ ] **Step 3:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~GetIntegrityCheckStatus|FullyQualifiedName~GetSuspectPages|FullyQualifiedName~GetCheckDbJobHistory"`
Expected: FAIL

- [ ] **Step 4:Service 實作**

在 `PerformanceDiagnosticsService.cs` 類別尾端追加:

```csharp
public async Task<IReadOnlyList<IntegrityCheckStatus>> GetIntegrityCheckStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default)
{
    var rows = await _repo.GetLastCheckDbAsync(progress, ct);
    return rows.Select(r =>
    {
        int? days = r.LastKnownGood.HasValue
            ? Math.Max(0, (int)(DateTime.UtcNow.Date - r.LastKnownGood.Value.Date).TotalDays)
            : (int?)null;
        return new IntegrityCheckStatus
        {
            DatabaseName = r.DatabaseName,
            LastKnownGood = r.LastKnownGood,
            DaysSince = days,
            Health = ClassifyHealth(days)
        };
    }).ToList();
}

public Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default)
    => _repo.GetSuspectPagesAsync(ct);

public Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default)
    => _repo.GetCheckDbJobHistoryAsync(top, ct);

private static IntegrityHealth ClassifyHealth(int? days) => days switch
{
    null => IntegrityHealth.Critical,
    < 14 => IntegrityHealth.Healthy,
    < 30 => IntegrityHealth.Warning,
    _ => IntegrityHealth.Critical
};
```

確保 `using Specurai.Domain.Entities;` 已存在。

- [ ] **Step 5:執行所有測試 PASS**

Run: `dotnet test`
Expected: 全部 PASS(新增 9 筆 Service 測試)

- [ ] **Step 6:Commit**

```bash
git add src/Specurai.Application/Services/IPerformanceDiagnosticsService.cs src/Specurai.Application/Services/PerformanceDiagnosticsService.cs tests/Specurai.Application.Tests/Services/PerformanceDiagnosticsServiceTests.cs
git commit -m "feat(application): PerformanceDiagnosticsService 新增完整性檢查三個方法 + Health 分類"
```

---

## Task 7:Desktop ViewModel — 集合、旗標、Command

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs`
- Modify: `tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs`

- [ ] **Step 1:寫失敗測試**

在 `PerformanceDiagnosticsDocumentViewModelTests` 追加:

```csharp
[Fact]
public void 設計時建構_應有完整性檢查空集合與HasSuspectPagesFalse()
{
    var vm = new PerformanceDiagnosticsDocumentViewModel();
    vm.IntegrityChecks.Should().BeEmpty();
    vm.SuspectPages.Should().BeEmpty();
    vm.CheckDbJobHistories.Should().BeEmpty();
    vm.HasSuspectPages.Should().BeFalse();
    vm.IsLoadingIntegrity.Should().BeFalse();
}

[Fact]
public void HasSuspectPages_當SuspectPages有值_應為True()
{
    var vm = new PerformanceDiagnosticsDocumentViewModel();
    vm.SuspectPages.Add(new SuspectPage
    {
        DatabaseName = "DB", FileId = 1, PageId = 100,
        EventTypeRaw = 3, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow
    });
    vm.HasSuspectPages.Should().BeTrue();
}
```

確保檔案有 `using Specurai.Domain.Entities;`。

- [ ] **Step 2:執行測試確認 FAIL**

Run: `dotnet test tests/Specurai.Desktop.Tests --filter "FullyQualifiedName~設計時建構_應有完整性檢查空集合|FullyQualifiedName~HasSuspectPages_當SuspectPages有值"`
Expected: FAIL

- [ ] **Step 3:Read 既有 ViewModel 找到適合插入位置**

開啟 `src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs`,定位到「錯誤記錄」相關 region 結尾(尋找 `RunErrorLogAnalysisAsync`),新區塊插在其後。

- [ ] **Step 4:加入新 region**

```csharp
#region 完整性檢查

public ObservableCollection<IntegrityCheckStatus> IntegrityChecks { get; } = [];
public ObservableCollection<SuspectPage> SuspectPages { get; } = [];
public ObservableCollection<CheckDbJobHistory> CheckDbJobHistories { get; } = [];

[ObservableProperty]
private bool _isLoadingIntegrity;

[ObservableProperty]
private string _integrityProgressMessage = string.Empty;

/// <summary>是否有疑似損毀頁面(供 UI 條件顯示)</summary>
public bool HasSuspectPages => SuspectPages.Count > 0;

[RelayCommand]
private async Task RunIntegrityCheckAnalysisAsync()
{
    if (_service is null) return;

    IsLoadingIntegrity = true;
    IntegrityProgressMessage = "開始載入完整性資料...";
    IntegrityChecks.Clear();
    SuspectPages.Clear();
    CheckDbJobHistories.Clear();
    OnPropertyChanged(nameof(HasSuspectPages));

    _cancellationTokenSource = new CancellationTokenSource();
    try
    {
        var progress = new Progress<string>(m => IntegrityProgressMessage = m);

        // 並行三個查詢加快回應
        var lastCheckTask = _service.GetIntegrityCheckStatusAsync(progress, _cancellationTokenSource.Token);
        var suspectTask = _service.GetSuspectPagesAsync(_cancellationTokenSource.Token);
        var historyTask = _service.GetCheckDbJobHistoryAsync(50, _cancellationTokenSource.Token);

        await Task.WhenAll(lastCheckTask, suspectTask, historyTask);

        foreach (var s in await lastCheckTask) IntegrityChecks.Add(s);
        foreach (var p in await suspectTask) SuspectPages.Add(p);
        foreach (var h in await historyTask) CheckDbJobHistories.Add(h);

        OnPropertyChanged(nameof(HasSuspectPages));
        IntegrityProgressMessage = $"完成:{IntegrityChecks.Count} 個 DB / {SuspectPages.Count} 筆 Suspect Page / {CheckDbJobHistories.Count} 筆 Job 紀錄";
    }
    catch (OperationCanceledException)
    {
        IntegrityProgressMessage = "已取消";
    }
    catch (Exception ex)
    {
        IntegrityProgressMessage = $"載入失敗:{ex.Message}";
    }
    finally
    {
        IsLoadingIntegrity = false;
    }
}

#endregion
```

> 假設既有檔案有 `_service` 欄位(`IPerformanceDiagnosticsService?`)、`_cancellationTokenSource` 欄位。若名稱不同,請對齊既有命名(透過 Read 檢查)。

- [ ] **Step 5:執行所有測試 PASS**

Run: `dotnet test`
Expected: 全部 PASS(新增 2 筆 ViewModel 測試)

- [ ] **Step 6:Commit**

```bash
git add src/Specurai.Desktop/ViewModels/PerformanceDiagnosticsDocumentViewModel.cs tests/Specurai.Desktop.Tests/ViewModels/PerformanceDiagnosticsDocumentViewModelTests.cs
git commit -m "feat(desktop): PerformanceDiagnostics ViewModel 加入完整性檢查集合與 Command"
```

---

## Task 8:Desktop View — TabItem「完整性檢查」

**Files:**
- Modify: `src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml`

- [ ] **Step 1:在「錯誤記錄」TabItem 之後追加新 TabItem**

定位:`src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml` 約第 498 行,「錯誤記錄」`</TabItem>` 之後、`</TabControl>` 之前。

插入:

```xml
<!-- 完整性檢查分頁 -->
<TabItem Header="完整性檢查">
    <Grid RowDefinitions="Auto,Auto,*">
        <!-- 工具列 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="10" Margin="10">
            <Button Content="重新整理"
                    Command="{Binding RunIntegrityCheckAnalysisCommand}"
                    IsEnabled="{Binding !IsLoadingIntegrity}"/>
            <TextBlock Text="{Binding IntegrityProgressMessage}"
                       VerticalAlignment="Center" Opacity="0.7"/>
        </StackPanel>

        <!-- 說明 -->
        <Expander Grid.Row="1" Header="完整性檢查說明（點擊展開）" Margin="10,0,10,5">
            <StackPanel Spacing="4" Margin="10">
                <TextBlock Text="本頁整合三項資料庫完整性資訊，協助 DBA 快速判斷健康狀態：" TextWrapping="Wrap"/>
                <TextBlock Text="• 最後 CHECKDB 時間：來自 DBCC DBINFO 的 dbi_dbccLastKnownGood，&lt;14 天為健康" TextWrapping="Wrap"/>
                <TextBlock Text="• Suspect Pages：msdb.dbo.suspect_pages，記錄已被 SQL Server 偵測到的損毀頁面" TextWrapping="Wrap"/>
                <TextBlock Text="• CHECKDB Job 紀錄：SQL Agent 中名稱含 CheckDb 的 Job 最近執行歷史" TextWrapping="Wrap"/>
            </StackPanel>
        </Expander>

        <!-- 三段內容 -->
        <ScrollViewer Grid.Row="2" Margin="10,0,10,10">
            <StackPanel Spacing="10">
                <Expander Header="📋 各資料庫最後 CHECKDB 時間" IsExpanded="True">
                    <DataGrid ItemsSource="{Binding IntegrityChecks}" AutoGenerateColumns="False"
                              IsReadOnly="True" CanUserResizeColumns="True" MaxHeight="320">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Database" Binding="{Binding DatabaseName}" Width="200"/>
                            <DataGridTextColumn Header="最後成功時間" Binding="{Binding LastKnownGood, StringFormat='yyyy-MM-dd HH:mm'}" Width="160"/>
                            <DataGridTextColumn Header="距今天數" Binding="{Binding DaysSince}" Width="100"/>
                            <DataGridTextColumn Header="健康" Binding="{Binding Health}" Width="100"/>
                        </DataGrid.Columns>
                    </DataGrid>
                </Expander>

                <Expander Header="⚠️ Suspect Pages（疑似損毀頁面）" IsExpanded="True">
                    <Grid>
                        <DataGrid ItemsSource="{Binding SuspectPages}" AutoGenerateColumns="False"
                                  IsReadOnly="True" CanUserResizeColumns="True" MaxHeight="240"
                                  IsVisible="{Binding HasSuspectPages}">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Database" Binding="{Binding DatabaseName}" Width="160"/>
                                <DataGridTextColumn Header="FileId" Binding="{Binding FileId}" Width="80"/>
                                <DataGridTextColumn Header="PageId" Binding="{Binding PageId}" Width="120"/>
                                <DataGridTextColumn Header="事件類型" Binding="{Binding EventTypeText}" Width="160"/>
                                <DataGridTextColumn Header="錯誤次數" Binding="{Binding ErrorCount}" Width="100"/>
                                <DataGridTextColumn Header="最後更新" Binding="{Binding LastUpdateDate, StringFormat='yyyy-MM-dd HH:mm'}" Width="160"/>
                            </DataGrid.Columns>
                        </DataGrid>
                        <TextBlock Text="✅ 目前無疑似損毀頁面" Foreground="Green" FontSize="14"
                                   HorizontalAlignment="Center" Margin="0,20"
                                   IsVisible="{Binding !HasSuspectPages}"/>
                    </Grid>
                </Expander>

                <Expander Header="📜 最近 CHECKDB Job 執行紀錄" IsExpanded="True">
                    <DataGrid ItemsSource="{Binding CheckDbJobHistories}" AutoGenerateColumns="False"
                              IsReadOnly="True" CanUserResizeColumns="True" MaxHeight="320">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Job" Binding="{Binding JobName}" Width="200"/>
                            <DataGridTextColumn Header="執行時間" Binding="{Binding RunAt, StringFormat='yyyy-MM-dd HH:mm:ss'}" Width="160"/>
                            <DataGridTextColumn Header="時長" Binding="{Binding Duration, StringFormat='hh\\:mm\\:ss'}" Width="80"/>
                            <DataGridTextColumn Header="結果" Binding="{Binding StatusText}" Width="80"/>
                            <DataGridTextColumn Header="訊息" Binding="{Binding Message}" Width="*"/>
                        </DataGrid.Columns>
                    </DataGrid>
                </Expander>
            </StackPanel>
        </ScrollViewer>
    </Grid>
</TabItem>
```

- [ ] **Step 2:建置 + 全測試**

Run: `dotnet build && dotnet test`
Expected: 全部 PASS

- [ ] **Step 3:Commit**

```bash
git add src/Specurai.Desktop/Views/PerformanceDiagnosticsDocumentView.axaml
git commit -m "feat(desktop): 效能診斷新增完整性檢查分頁(三段 DataGrid)"
```

---

## Task 9:全測試 + Code Review

- [ ] **Step 1:全測試**

Run: `dotnet test`
Expected: 全部 PASS(既有 + 本次新增約 20 筆)

- [ ] **Step 2:Code Review**

依 CLAUDE.md 法規,使用 `superpowers:requesting-code-review` 對本批 9 個 commit 進行審查。

審查重點:
- Clean Architecture 分層相依
- SQL 安全(動態 SQL 中 `db.Replace("]", "]]")` 是否充分;Dapper 參數化是否使用)
- TDD 完整度
- ViewModel 設計時建構 pattern
- HasSuspectPages 變化通知是否完整(Add 後有觸發 OnPropertyChanged)

- [ ] **Step 3:依審查回饋修正,每修一項 commit 一次**

---

## Self-Review

- ✅ Spec 三區塊皆有對應 task:最後 CHECKDB(T3, T6)、Suspect Pages(T4, T6)、Job 紀錄(T5, T6)
- ✅ Domain Entity 三個齊備(T1)+ DTO `LastCheckDbRow`(T2)
- ✅ Repository 介面與實作(T2-T5)
- ✅ Service Health 分類純函式可單測(T6)
- ✅ ViewModel 設計時建構 + Command + 進度回報(T7)
- ✅ View 三段 DataGrid + 空狀態提示(T8)
- ✅ 無 placeholder
- ✅ 型別命名一致:`IntegrityCheckStatus`、`SuspectPage`、`CheckDbJobHistory`、`LastCheckDbRow`、`IntegrityHealth`、`HasSuspectPages` 在所有 task 出現一致
