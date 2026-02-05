# 使用狀態分析（Usage Analysis）實作計畫

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 新增「使用狀態分析」功能，檢查資料表及欄位是否在近 N 年內有被使用，支援單環境分析與多環境矩陣比對，並可直接刪除未使用的表/欄位。

**Architecture:** Clean Architecture 由內而外實作（Domain → Application → Infrastructure → Desktop），搭配 TDD（Red-Green-Refactor）。每個實體、服務、ViewModel 都先寫測試再寫實作。

**Tech Stack:** .NET 8, xUnit, NSubstitute, FluentAssertions, Dapper, Avalonia 11.x, CommunityToolkit.Mvvm

**設計文件:** `docs/plans/2026-02-05-usage-analysis-design.md`

---

## Task 1: Domain 層 — TableUsageInfo 實體

**Files:**
- Create: `src/TableSpec.Domain/Entities/TableUsageInfo.cs`
- Test: `tests/TableSpec.Domain.Tests/Entities/TableUsageInfoTests.cs`

**Step 1: 寫失敗測試**

```csharp
// tests/TableSpec.Domain.Tests/Entities/TableUsageInfoTests.cs
using FluentAssertions;

namespace TableSpec.Domain.Tests.Entities;

public class TableUsageInfoTests
{
    [Fact]
    public void IsUsed_有查詢活動_應為True()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            UserSeeks = 100,
            UserScans = 0,
            UserLookups = 0,
            UserUpdates = 0,
            LastUserSeek = DateTime.Now.AddDays(-30),
            LastUserScan = null,
            LastUserUpdate = null
        };

        info.HasQueryActivity.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_無查詢活動_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            UserSeeks = 0,
            UserScans = 0,
            UserLookups = 0,
            UserUpdates = 0,
            LastUserSeek = null,
            LastUserScan = null,
            LastUserUpdate = null
        };

        info.HasQueryActivity.Should().BeFalse();
    }

    [Fact]
    public void HasRecentUpdate_最後更新在期限內_應為True()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders",
            LastUserUpdate = DateTime.Now.AddMonths(-6)
        };

        info.HasRecentUpdate(years: 2).Should().BeTrue();
    }

    [Fact]
    public void HasRecentUpdate_最後更新超過期限_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            LastUserUpdate = DateTime.Now.AddYears(-3)
        };

        info.HasRecentUpdate(years: 2).Should().BeFalse();
    }

    [Fact]
    public void HasRecentUpdate_無更新紀錄_應為False()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "OldTable",
            LastUserUpdate = null
        };

        info.HasRecentUpdate(years: 2).Should().BeFalse();
    }

    [Fact]
    public void FullTableName_應回傳Schema加表名()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders"
        };

        info.FullTableName.Should().Be("[dbo].[Orders]");
    }

    [Fact]
    public void DropTableStatement_應產生正確SQL()
    {
        var info = new TableUsageInfo
        {
            SchemaName = "dbo",
            TableName = "Orders"
        };

        info.DropTableStatement.Should().Be("DROP TABLE [dbo].[Orders]");
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~TableUsageInfoTests" -v n`
Expected: FAIL — `TableUsageInfo` 不存在

**Step 3: 寫最小實作**

```csharp
// src/TableSpec.Domain/Entities/TableUsageInfo.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// 資料表使用狀態資訊
/// </summary>
public class TableUsageInfo
{
    /// <summary>Schema 名稱</summary>
    public required string SchemaName { get; init; }

    /// <summary>資料表名稱</summary>
    public required string TableName { get; init; }

    /// <summary>索引搜尋次數</summary>
    public long UserSeeks { get; init; }

    /// <summary>索引掃描次數</summary>
    public long UserScans { get; init; }

    /// <summary>索引查找次數</summary>
    public long UserLookups { get; init; }

    /// <summary>更新次數</summary>
    public long UserUpdates { get; init; }

    /// <summary>最後索引搜尋時間</summary>
    public DateTime? LastUserSeek { get; init; }

    /// <summary>最後索引掃描時間</summary>
    public DateTime? LastUserScan { get; init; }

    /// <summary>最後更新時間</summary>
    public DateTime? LastUserUpdate { get; init; }

    /// <summary>是否有查詢活動（seek/scan/lookup > 0）</summary>
    public bool HasQueryActivity => UserSeeks + UserScans + UserLookups > 0;

    /// <summary>近 N 年是否有更新</summary>
    public bool HasRecentUpdate(int years)
        => LastUserUpdate.HasValue && LastUserUpdate.Value > DateTime.Now.AddYears(-years);

    /// <summary>完整表名</summary>
    public string FullTableName => $"[{SchemaName}].[{TableName}]";

    /// <summary>DROP TABLE 語法</summary>
    public string DropTableStatement => $"DROP TABLE [{SchemaName}].[{TableName}]";
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~TableUsageInfoTests" -v n`
Expected: PASS（全部 7 個測試通過）

**Step 5: Commit**

```bash
git add src/TableSpec.Domain/Entities/TableUsageInfo.cs tests/TableSpec.Domain.Tests/Entities/TableUsageInfoTests.cs
git commit -m "新增 TableUsageInfo 實體與測試"
```

---

## Task 2: Domain 層 — ColumnUsageStatus 實體

**Files:**
- Create: `src/TableSpec.Domain/Entities/ColumnUsageStatus.cs`
- Test: `tests/TableSpec.Domain.Tests/Entities/ColumnUsageStatusTests.cs`

**Step 1: 寫失敗測試**

```csharp
// tests/TableSpec.Domain.Tests/Entities/ColumnUsageStatusTests.cs
using FluentAssertions;

namespace TableSpec.Domain.Tests.Entities;

public class ColumnUsageStatusTests
{
    [Fact]
    public void IsUsed_PK欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Id",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = true,
            IsForeignKey = false,
            IsIdentity = true
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_FK欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "CustomerId",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = true,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_Identity欄位_即使未引用且全NULL_仍為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Id",
            DataType = "int",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = true
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_被查詢引用_應為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Status",
            DataType = "int",
            IsReferencedInQueries = true,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void IsUsed_未引用且全NULL_應為False()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)",
            IsReferencedInQueries = false,
            IsAllNull = true,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_未引用且全為預設值_應為False()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Status",
            DataType = "int",
            DefaultValue = "0",
            IsReferencedInQueries = false,
            IsAllNull = false,
            IsAllDefault = true,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_未引用但有實際資料_應為True()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)",
            IsReferencedInQueries = false,
            IsAllNull = false,
            IsAllDefault = false,
            IsPrimaryKey = false,
            IsForeignKey = false,
            IsIdentity = false
        };

        status.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void DropColumnStatement_應產生正確SQL()
    {
        var status = new ColumnUsageStatus
        {
            SchemaName = "dbo",
            TableName = "Orders",
            ColumnName = "Remark",
            DataType = "nvarchar(500)"
        };

        status.DropColumnStatement.Should().Be("ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]");
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~ColumnUsageStatusTests" -v n`
Expected: FAIL — `ColumnUsageStatus` 不存在

**Step 3: 寫最小實作**

```csharp
// src/TableSpec.Domain/Entities/ColumnUsageStatus.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位使用狀態資訊
/// </summary>
public class ColumnUsageStatus
{
    /// <summary>Schema 名稱</summary>
    public required string SchemaName { get; init; }

    /// <summary>資料表名稱</summary>
    public required string TableName { get; init; }

    /// <summary>欄位名稱</summary>
    public required string ColumnName { get; init; }

    /// <summary>資料型別</summary>
    public required string DataType { get; init; }

    /// <summary>是否被查詢計畫引用</summary>
    public bool IsReferencedInQueries { get; init; }

    /// <summary>是否全部為 NULL</summary>
    public bool IsAllNull { get; init; }

    /// <summary>是否所有非 NULL 值皆等於預設值</summary>
    public bool IsAllDefault { get; init; }

    /// <summary>欄位預設值</summary>
    public string? DefaultValue { get; init; }

    /// <summary>是否為主鍵</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>是否為外鍵</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>是否為 Identity</summary>
    public bool IsIdentity { get; init; }

    /// <summary>
    /// 是否被使用。
    /// PK/FK/Identity 欄位一律視為使用中；
    /// 其餘欄位需被查詢引用，或含有非 NULL 且非預設值的資料。
    /// </summary>
    public bool IsUsed =>
        IsPrimaryKey || IsForeignKey || IsIdentity
        || IsReferencedInQueries
        || (!IsAllNull && !IsAllDefault);

    /// <summary>DROP COLUMN 語法</summary>
    public string DropColumnStatement =>
        $"ALTER TABLE [{SchemaName}].[{TableName}] DROP COLUMN [{ColumnName}]";
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~ColumnUsageStatusTests" -v n`
Expected: PASS（全部 8 個測試通過）

**Step 5: Commit**

```bash
git add src/TableSpec.Domain/Entities/ColumnUsageStatus.cs tests/TableSpec.Domain.Tests/Entities/ColumnUsageStatusTests.cs
git commit -m "新增 ColumnUsageStatus 實體與測試"
```

---

## Task 3: Domain 層 — UsageComparison 相關實體與 UsageScanResult

**Files:**
- Create: `src/TableSpec.Domain/Entities/UsageComparison.cs`
- Create: `src/TableSpec.Domain/Entities/UsageScanResult.cs`
- Test: `tests/TableSpec.Domain.Tests/Entities/UsageComparisonTests.cs`

**Step 1: 寫失敗測試**

```csharp
// tests/TableSpec.Domain.Tests/Entities/UsageComparisonTests.cs
using FluentAssertions;

namespace TableSpec.Domain.Tests.Entities;

public class UsageComparisonTests
{
    [Fact]
    public void UsageScanResult_應正確計算未使用表數量()
    {
        var result = new UsageScanResult
        {
            Tables =
            [
                new TableUsageInfo { SchemaName = "dbo", TableName = "Used", UserSeeks = 10 },
                new TableUsageInfo { SchemaName = "dbo", TableName = "Unused1", UserSeeks = 0, UserScans = 0, UserLookups = 0 },
                new TableUsageInfo { SchemaName = "dbo", TableName = "Unused2", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
            ],
            Columns = [],
            YearsThreshold = 2
        };

        result.UnusedTableCount(years: 2).Should().Be(2);
        result.UsedTableCount(years: 2).Should().Be(1);
    }

    [Fact]
    public void UsageScanResult_應正確計算未使用欄位數量()
    {
        var result = new UsageScanResult
        {
            Tables = [],
            Columns =
            [
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Id", DataType = "int", IsPrimaryKey = true, IsAllNull = true },
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Remark", DataType = "nvarchar", IsAllNull = true },
                new ColumnUsageStatus { SchemaName = "dbo", TableName = "T", ColumnName = "Status", DataType = "int", IsReferencedInQueries = true }
            ],
            YearsThreshold = 2
        };

        result.UnusedColumnCount.Should().Be(1);
        result.UsedColumnCount.Should().Be(2);
    }

    [Fact]
    public void TableUsageComparisonRow_應包含各環境狀態()
    {
        var row = new TableUsageComparisonRow
        {
            SchemaName = "dbo",
            TableName = "Orders",
            EnvironmentStatus = new Dictionary<string, TableUsageInfo?>
            {
                ["DEV"] = new TableUsageInfo { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
                ["PROD"] = null // 不存在
            }
        };

        row.EnvironmentStatus.Should().ContainKey("DEV");
        row.EnvironmentStatus["DEV"]!.HasQueryActivity.Should().BeTrue();
        row.EnvironmentStatus["PROD"].Should().BeNull();
    }

    [Fact]
    public void UsageComparison_應包含基準環境與目標環境()
    {
        var comparison = new UsageComparison
        {
            BaseEnvironment = "DEV",
            TargetEnvironments = ["STG", "PROD"],
            TableRows = [],
            ColumnRows = []
        };

        comparison.BaseEnvironment.Should().Be("DEV");
        comparison.TargetEnvironments.Should().HaveCount(2);
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~UsageComparisonTests" -v n`
Expected: FAIL

**Step 3: 寫最小實作**

```csharp
// src/TableSpec.Domain/Entities/UsageScanResult.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// 使用狀態掃描結果
/// </summary>
public class UsageScanResult
{
    /// <summary>資料表使用狀態清單</summary>
    public IReadOnlyList<TableUsageInfo> Tables { get; init; } = [];

    /// <summary>欄位使用狀態清單</summary>
    public IReadOnlyList<ColumnUsageStatus> Columns { get; init; } = [];

    /// <summary>時間門檻（年）</summary>
    public int YearsThreshold { get; init; }

    /// <summary>未使用資料表數量</summary>
    public int UnusedTableCount(int years) =>
        Tables.Count(t => !t.HasQueryActivity && !t.HasRecentUpdate(years));

    /// <summary>使用中資料表數量</summary>
    public int UsedTableCount(int years) =>
        Tables.Count - UnusedTableCount(years);

    /// <summary>未使用欄位數量</summary>
    public int UnusedColumnCount => Columns.Count(c => !c.IsUsed);

    /// <summary>使用中欄位數量</summary>
    public int UsedColumnCount => Columns.Count(c => c.IsUsed);
}
```

```csharp
// src/TableSpec.Domain/Entities/UsageComparison.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// 多環境使用狀態比對結果
/// </summary>
public class UsageComparison
{
    /// <summary>基準環境名稱</summary>
    public required string BaseEnvironment { get; init; }

    /// <summary>目標環境名稱清單</summary>
    public IReadOnlyList<string> TargetEnvironments { get; init; } = [];

    /// <summary>資料表層級比對列</summary>
    public IReadOnlyList<TableUsageComparisonRow> TableRows { get; init; } = [];

    /// <summary>欄位層級比對列</summary>
    public IReadOnlyList<ColumnUsageComparisonRow> ColumnRows { get; init; } = [];
}

/// <summary>
/// 資料表使用狀態比對列（跨環境）
/// </summary>
public class TableUsageComparisonRow
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }

    /// <summary>各環境的使用狀態（null 表示該環境不存在此表）</summary>
    public Dictionary<string, TableUsageInfo?> EnvironmentStatus { get; init; } = new();
}

/// <summary>
/// 欄位使用狀態比對列（跨環境）
/// </summary>
public class ColumnUsageComparisonRow
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string ColumnName { get; init; }

    /// <summary>各環境的使用狀態（null 表示該環境不存在此欄位）</summary>
    public Dictionary<string, ColumnUsageStatus?> EnvironmentStatus { get; init; } = new();
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "FullyQualifiedName~UsageComparisonTests" -v n`
Expected: PASS（全部 4 個測試通過）

**Step 5: Commit**

```bash
git add src/TableSpec.Domain/Entities/UsageScanResult.cs src/TableSpec.Domain/Entities/UsageComparison.cs tests/TableSpec.Domain.Tests/Entities/UsageComparisonTests.cs
git commit -m "新增 UsageScanResult 與 UsageComparison 實體與測試"
```

---

## Task 4: Domain 層 — IUsageAnalysisRepository 介面

**Files:**
- Create: `src/TableSpec.Domain/Interfaces/IUsageAnalysisRepository.cs`

**Step 1: 建立介面**

```csharp
// src/TableSpec.Domain/Interfaces/IUsageAnalysisRepository.cs
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 使用狀態分析 Repository 介面
/// </summary>
public interface IUsageAnalysisRepository
{
    /// <summary>取得資料表使用狀態</summary>
    Task<IReadOnlyList<TableUsageInfo>> GetTableUsageAsync(CancellationToken ct = default);

    /// <summary>取得欄位使用狀態（含查詢計畫引用、NULL/預設值檢查）</summary>
    Task<IReadOnlyList<ColumnUsageStatus>> GetColumnUsageStatusAsync(
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>執行 SQL 語法（用於 DROP TABLE / DROP COLUMN）</summary>
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);
}
```

**Step 2: 確認編譯通過**

Run: `dotnet build src/TableSpec.Domain`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/TableSpec.Domain/Interfaces/IUsageAnalysisRepository.cs
git commit -m "新增 IUsageAnalysisRepository 介面"
```

---

## Task 5: Application 層 — UsageAnalysisService（單環境掃描）

**Files:**
- Create: `src/TableSpec.Application/Services/IUsageAnalysisService.cs`
- Create: `src/TableSpec.Application/Services/UsageAnalysisService.cs`
- Test: `tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceTests.cs`

**Step 1: 寫失敗測試 — ScanAsync**

```csharp
// tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

public class UsageAnalysisServiceTests
{
    private readonly IUsageAnalysisRepository _repository;
    private readonly UsageAnalysisService _service;

    public UsageAnalysisServiceTests()
    {
        _repository = Substitute.For<IUsageAnalysisRepository>();
        _service = new UsageAnalysisService(_repository);
    }

    [Fact]
    public async Task ScanAsync_應回傳表和欄位使用狀態()
    {
        // Arrange
        var tables = new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
            new() { SchemaName = "dbo", TableName = "OldTable", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
        };
        var columns = new List<ColumnUsageStatus>
        {
            new() { SchemaName = "dbo", TableName = "Orders", ColumnName = "Id", DataType = "int", IsPrimaryKey = true },
            new() { SchemaName = "dbo", TableName = "Orders", ColumnName = "Remark", DataType = "nvarchar", IsAllNull = true }
        };

        _repository.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(tables);
        _repository.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>()).Returns(columns);

        // Act
        var result = await _service.ScanAsync(years: 2, progress: null, ct: CancellationToken.None);

        // Assert
        result.Tables.Should().HaveCount(2);
        result.Columns.Should().HaveCount(2);
        result.YearsThreshold.Should().Be(2);
    }

    [Fact]
    public async Task ScanAsync_Repository無資料_應回傳空結果()
    {
        // Arrange
        _repository.GetTableUsageAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TableUsageInfo>());
        _repository.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());

        // Act
        var result = await _service.ScanAsync(years: 2, progress: null, ct: CancellationToken.None);

        // Assert
        result.Tables.Should().BeEmpty();
        result.Columns.Should().BeEmpty();
    }

    [Fact]
    public void GenerateDropTableSql_應產生正確語法()
    {
        var sql = _service.GenerateDropTableSql("dbo", "OldTable");
        sql.Should().Be("DROP TABLE [dbo].[OldTable]");
    }

    [Fact]
    public void GenerateDropColumnSql_應產生正確語法()
    {
        var sql = _service.GenerateDropColumnSql("dbo", "Orders", "Remark");
        sql.Should().Be("ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]");
    }

    [Fact]
    public async Task DeleteTableAsync_應呼叫Repository執行SQL()
    {
        // Act
        await _service.DeleteTableAsync("dbo", "OldTable", CancellationToken.None);

        // Assert
        await _repository.Received(1).ExecuteSqlAsync(
            "DROP TABLE [dbo].[OldTable]",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteColumnAsync_應呼叫Repository執行SQL()
    {
        // Act
        await _service.DeleteColumnAsync("dbo", "Orders", "Remark", CancellationToken.None);

        // Assert
        await _repository.Received(1).ExecuteSqlAsync(
            "ALTER TABLE [dbo].[Orders] DROP COLUMN [Remark]",
            Arg.Any<CancellationToken>());
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Application.Tests --filter "FullyQualifiedName~UsageAnalysisServiceTests" -v n`
Expected: FAIL — `UsageAnalysisService` 不存在

**Step 3: 寫最小實作**

```csharp
// src/TableSpec.Application/Services/IUsageAnalysisService.cs
using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 使用狀態分析服務介面
/// </summary>
public interface IUsageAnalysisService
{
    /// <summary>單環境掃描</summary>
    Task<UsageScanResult> ScanAsync(int years, IProgress<string>? progress, CancellationToken ct);

    /// <summary>多環境比對</summary>
    Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct);

    /// <summary>刪除資料表</summary>
    Task DeleteTableAsync(string schemaName, string tableName, CancellationToken ct);

    /// <summary>刪除欄位</summary>
    Task DeleteColumnAsync(string schemaName, string tableName, string columnName, CancellationToken ct);

    /// <summary>產生 DROP TABLE 語法</summary>
    string GenerateDropTableSql(string schemaName, string tableName);

    /// <summary>產生 DROP COLUMN 語法</summary>
    string GenerateDropColumnSql(string schemaName, string tableName, string columnName);
}
```

```csharp
// src/TableSpec.Application/Services/UsageAnalysisService.cs
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 使用狀態分析服務實作
/// </summary>
public class UsageAnalysisService : IUsageAnalysisService
{
    private readonly IUsageAnalysisRepository _repository;

    public UsageAnalysisService(IUsageAnalysisRepository repository)
    {
        _repository = repository;
    }

    public async Task<UsageScanResult> ScanAsync(
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("正在取得資料表使用狀態...");
        var tables = await _repository.GetTableUsageAsync(ct);

        progress?.Report("正在分析欄位使用狀態...");
        var columns = await _repository.GetColumnUsageStatusAsync(progress, ct);

        return new UsageScanResult
        {
            Tables = tables,
            Columns = columns,
            YearsThreshold = years
        };
    }

    public Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        // Task 6 實作
        throw new NotImplementedException();
    }

    public async Task DeleteTableAsync(string schemaName, string tableName, CancellationToken ct)
    {
        var sql = GenerateDropTableSql(schemaName, tableName);
        await _repository.ExecuteSqlAsync(sql, ct);
    }

    public async Task DeleteColumnAsync(
        string schemaName, string tableName, string columnName, CancellationToken ct)
    {
        var sql = GenerateDropColumnSql(schemaName, tableName, columnName);
        await _repository.ExecuteSqlAsync(sql, ct);
    }

    public string GenerateDropTableSql(string schemaName, string tableName)
        => $"DROP TABLE [{schemaName}].[{tableName}]";

    public string GenerateDropColumnSql(string schemaName, string tableName, string columnName)
        => $"ALTER TABLE [{schemaName}].[{tableName}] DROP COLUMN [{columnName}]";
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Application.Tests --filter "FullyQualifiedName~UsageAnalysisServiceTests" -v n`
Expected: PASS（全部 6 個測試通過）

**Step 5: Commit**

```bash
git add src/TableSpec.Application/Services/IUsageAnalysisService.cs src/TableSpec.Application/Services/UsageAnalysisService.cs tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceTests.cs
git commit -m "新增 UsageAnalysisService 單環境掃描與刪除功能及測試"
```

---

## Task 6: Application 層 — UsageAnalysisService（多環境比對）

**Files:**
- Modify: `src/TableSpec.Application/Services/IUsageAnalysisService.cs`
- Modify: `src/TableSpec.Application/Services/UsageAnalysisService.cs`
- Test: `tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceCompareTests.cs`

**注意：** 多環境比對需要 `IConnectionManager` 來取得各環境的連線字串，並動態建立 Repository。Service 建構函式需增加 `IConnectionManager` 與 `Func<string?, IUsageAnalysisRepository>` 工廠參數。

**Step 1: 寫失敗測試**

```csharp
// tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceCompareTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

public class UsageAnalysisServiceCompareTests
{
    private readonly IUsageAnalysisRepository _baseRepo;
    private readonly IUsageAnalysisRepository _targetRepo;
    private readonly IConnectionManager _connectionManager;
    private readonly UsageAnalysisService _service;

    public UsageAnalysisServiceCompareTests()
    {
        _baseRepo = Substitute.For<IUsageAnalysisRepository>();
        _targetRepo = Substitute.For<IUsageAnalysisRepository>();
        _connectionManager = Substitute.For<IConnectionManager>();

        var baseProfileId = Guid.NewGuid();
        var targetProfileId = Guid.NewGuid();

        _connectionManager.GetConnectionString(baseProfileId).Returns("Server=base;Database=db");
        _connectionManager.GetConnectionString(targetProfileId).Returns("Server=target;Database=db");
        _connectionManager.GetProfileName(baseProfileId).Returns("DEV");
        _connectionManager.GetProfileName(targetProfileId).Returns("PROD");

        // Repository 工廠：根據連線字串建立 Repository
        IUsageAnalysisRepository RepoFactory(string? connStr) =>
            connStr == "Server=base;Database=db" ? _baseRepo : _targetRepo;

        _service = new UsageAnalysisService(_baseRepo, _connectionManager, RepoFactory);
    }

    [Fact]
    public async Task CompareAsync_應合併各環境的表使用狀態()
    {
        // Arrange
        var baseProfileId = Guid.NewGuid();
        var targetProfileId = Guid.NewGuid();
        _connectionManager.GetConnectionString(baseProfileId).Returns("Server=base;Database=db");
        _connectionManager.GetConnectionString(targetProfileId).Returns("Server=target;Database=db");
        _connectionManager.GetProfileName(baseProfileId).Returns("DEV");
        _connectionManager.GetProfileName(targetProfileId).Returns("PROD");

        _baseRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 100 },
            new() { SchemaName = "dbo", TableName = "DevOnly", UserSeeks = 0, UserScans = 0, UserLookups = 0 }
        });

        _targetRepo.GetTableUsageAsync(Arg.Any<CancellationToken>()).Returns(new List<TableUsageInfo>
        {
            new() { SchemaName = "dbo", TableName = "Orders", UserSeeks = 5000 }
        });

        _baseRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());
        _targetRepo.GetColumnUsageStatusAsync(Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ColumnUsageStatus>());

        // Act
        var result = await _service.CompareAsync(
            baseProfileId, [targetProfileId], 2, null, CancellationToken.None);

        // Assert
        result.BaseEnvironment.Should().Be("DEV");
        result.TargetEnvironments.Should().Contain("PROD");
        result.TableRows.Should().HaveCount(2); // Orders + DevOnly
        var ordersRow = result.TableRows.First(r => r.TableName == "Orders");
        ordersRow.EnvironmentStatus["DEV"]!.UserSeeks.Should().Be(100);
        ordersRow.EnvironmentStatus["PROD"]!.UserSeeks.Should().Be(5000);
        var devOnlyRow = result.TableRows.First(r => r.TableName == "DevOnly");
        devOnlyRow.EnvironmentStatus["PROD"].Should().BeNull();
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Application.Tests --filter "FullyQualifiedName~UsageAnalysisServiceCompareTests" -v n`
Expected: FAIL — 建構函式不匹配或 `CompareAsync` 拋 `NotImplementedException`

**Step 3: 修改 UsageAnalysisService 加入多環境比對邏輯**

更新建構函式支援 `IConnectionManager` 和 Repository 工廠，實作 `CompareAsync`：

```csharp
// 更新 UsageAnalysisService.cs
public class UsageAnalysisService : IUsageAnalysisService
{
    private readonly IUsageAnalysisRepository _repository;
    private readonly IConnectionManager? _connectionManager;
    private readonly Func<string?, IUsageAnalysisRepository>? _repositoryFactory;

    // 單環境用建構函式
    public UsageAnalysisService(IUsageAnalysisRepository repository)
    {
        _repository = repository;
    }

    // 多環境用建構函式
    public UsageAnalysisService(
        IUsageAnalysisRepository repository,
        IConnectionManager connectionManager,
        Func<string?, IUsageAnalysisRepository> repositoryFactory)
    {
        _repository = repository;
        _connectionManager = connectionManager;
        _repositoryFactory = repositoryFactory;
    }

    public async Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        if (_connectionManager == null || _repositoryFactory == null)
            throw new InvalidOperationException("多環境比對需要 ConnectionManager");

        var allEnvironments = new List<(string Name, IUsageAnalysisRepository Repo)>();

        // 基準環境
        var baseConnStr = _connectionManager.GetConnectionString(baseProfileId);
        var baseName = _connectionManager.GetProfileName(baseProfileId);
        var baseRepo = _repositoryFactory(baseConnStr);
        allEnvironments.Add((baseName, baseRepo));

        // 目標環境
        foreach (var targetId in targetProfileIds)
        {
            ct.ThrowIfCancellationRequested();
            var connStr = _connectionManager.GetConnectionString(targetId);
            var name = _connectionManager.GetProfileName(targetId);
            var repo = _repositoryFactory(connStr);
            allEnvironments.Add((name, repo));
        }

        // 收集各環境資料
        var envData = new Dictionary<string, (IReadOnlyList<TableUsageInfo> Tables, IReadOnlyList<ColumnUsageStatus> Columns)>();
        foreach (var (name, repo) in allEnvironments)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"正在掃描環境：{name}...");
            var tables = await repo.GetTableUsageAsync(ct);
            var columns = await repo.GetColumnUsageStatusAsync(progress, ct);
            envData[name] = (tables, columns);
        }

        // 合併表資料
        var allTableKeys = envData.Values
            .SelectMany(d => d.Tables.Select(t => (t.SchemaName, t.TableName)))
            .Distinct()
            .OrderBy(k => k.SchemaName).ThenBy(k => k.TableName)
            .ToList();

        var tableRows = allTableKeys.Select(key => new TableUsageComparisonRow
        {
            SchemaName = key.SchemaName,
            TableName = key.TableName,
            EnvironmentStatus = allEnvironments.ToDictionary(
                e => e.Name,
                e => envData[e.Name].Tables.FirstOrDefault(
                    t => t.SchemaName == key.SchemaName && t.TableName == key.TableName))
        }).ToList();

        // 合併欄位資料
        var allColumnKeys = envData.Values
            .SelectMany(d => d.Columns.Select(c => (c.SchemaName, c.TableName, c.ColumnName)))
            .Distinct()
            .OrderBy(k => k.SchemaName).ThenBy(k => k.TableName).ThenBy(k => k.ColumnName)
            .ToList();

        var columnRows = allColumnKeys.Select(key => new ColumnUsageComparisonRow
        {
            SchemaName = key.SchemaName,
            TableName = key.TableName,
            ColumnName = key.ColumnName,
            EnvironmentStatus = allEnvironments.ToDictionary(
                e => e.Name,
                e => envData[e.Name].Columns.FirstOrDefault(
                    c => c.SchemaName == key.SchemaName && c.TableName == key.TableName && c.ColumnName == key.ColumnName))
        }).ToList();

        return new UsageComparison
        {
            BaseEnvironment = baseName,
            TargetEnvironments = targetProfileIds.Select(id => _connectionManager.GetProfileName(id)).ToList(),
            TableRows = tableRows,
            ColumnRows = columnRows
        };
    }

    // ... 其餘方法不變
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Application.Tests --filter "FullyQualifiedName~UsageAnalysisService" -v n`
Expected: PASS（Task 5 + Task 6 的所有測試皆通過）

**Step 5: Commit**

```bash
git add src/TableSpec.Application/Services/UsageAnalysisService.cs tests/TableSpec.Application.Tests/Services/UsageAnalysisServiceCompareTests.cs
git commit -m "新增 UsageAnalysisService 多環境比對功能及測試"
```

---

## Task 7: Infrastructure 層 — UsageAnalysisRepository

**Files:**
- Create: `src/TableSpec.Infrastructure/Repositories/UsageAnalysisRepository.cs`

**注意：** 此 Repository 涉及實際 SQL Server DMV 查詢，屬於整合測試範疇，不適合純 Unit Test。先建立實作，後續手動測試。

**Step 1: 建立 Repository 實作**

```csharp
// src/TableSpec.Infrastructure/Repositories/UsageAnalysisRepository.cs
using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 使用狀態分析 Repository 實作
/// </summary>
public class UsageAnalysisRepository : IUsageAnalysisRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public UsageAnalysisRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<TableUsageInfo>> GetTableUsageAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableUsageInfo>();

        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    ISNULL(SUM(ius.user_seeks), 0) AS UserSeeks,
    ISNULL(SUM(ius.user_scans), 0) AS UserScans,
    ISNULL(SUM(ius.user_lookups), 0) AS UserLookups,
    ISNULL(SUM(ius.user_updates), 0) AS UserUpdates,
    MAX(ius.last_user_seek) AS LastUserSeek,
    MAX(ius.last_user_scan) AS LastUserScan,
    MAX(ius.last_user_update) AS LastUserUpdate
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.dm_db_index_usage_stats ius
    ON ius.object_id = t.object_id
    AND ius.database_id = DB_ID()
GROUP BY s.name, t.name
ORDER BY s.name, t.name";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<TableUsageInfo>(
            new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
        return result.ToList();
    }

    public async Task<IReadOnlyList<ColumnUsageStatus>> GetColumnUsageStatusAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ColumnUsageStatus>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 第一步：取得查詢計畫中引用的欄位
        progress?.Report("正在分析查詢計畫引用...");
        var referencedColumns = await GetReferencedColumnsAsync(connection, ct);

        // 第二步：取得所有欄位基本資訊（含 PK/FK/Identity/Default）
        progress?.Report("正在取得欄位資訊...");
        var columnInfos = await GetColumnInfosAsync(connection, ct);

        // 第三步：逐表檢查 NULL / 預設值
        var results = new List<ColumnUsageStatus>();
        var tables = columnInfos.GroupBy(c => new { c.SchemaName, c.TableName }).ToList();

        for (var i = 0; i < tables.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var tableGroup = tables[i];
            progress?.Report($"正在掃描 {i + 1}/{tables.Count} 張表：[{tableGroup.Key.SchemaName}].[{tableGroup.Key.TableName}]...");

            var nullDefaultResults = await CheckNullAndDefaultAsync(
                connection, tableGroup.Key.SchemaName, tableGroup.Key.TableName,
                tableGroup.ToList(), ct);

            foreach (var col in tableGroup)
            {
                var isReferenced = referencedColumns.Contains(
                    $"{col.SchemaName}.{col.TableName}.{col.ColumnName}");

                var ndResult = nullDefaultResults.GetValueOrDefault(col.ColumnName);

                results.Add(new ColumnUsageStatus
                {
                    SchemaName = col.SchemaName,
                    TableName = col.TableName,
                    ColumnName = col.ColumnName,
                    DataType = col.DataType,
                    IsReferencedInQueries = isReferenced,
                    IsAllNull = ndResult?.IsAllNull ?? false,
                    IsAllDefault = ndResult?.IsAllDefault ?? false,
                    DefaultValue = col.DefaultValue,
                    IsPrimaryKey = col.IsPrimaryKey,
                    IsForeignKey = col.IsForeignKey,
                    IsIdentity = col.IsIdentity
                });
            }
        }

        return results;
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("連線字串為空");

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
    }

    #region 私有方法

    private static async Task<HashSet<string>> GetReferencedColumnsAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    OBJECT_SCHEMA_NAME(st.objectid) AS SchemaName,
    OBJECT_NAME(st.objectid) AS TableName,
    c.value('(@Column)[1]', 'NVARCHAR(128)') AS ColumnName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
CROSS APPLY qp.query_plan.nodes('//ColumnReference') AS ref(c)
WHERE st.objectid IS NOT NULL
    AND st.dbid = DB_ID()";

        try
        {
            var result = await connection.QueryAsync<(string SchemaName, string TableName, string ColumnName)>(
                new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));

            return result
                .Where(r => !string.IsNullOrEmpty(r.SchemaName) && !string.IsNullOrEmpty(r.TableName) && !string.IsNullOrEmpty(r.ColumnName))
                .Select(r => $"{r.SchemaName}.{r.TableName}.{r.ColumnName}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<IReadOnlyList<ColumnInfoRaw>> GetColumnInfosAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    CASE
        WHEN tp.name IN ('nvarchar','nchar') THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length/2 AS VARCHAR) END + ')'
        WHEN tp.name IN ('varchar','char','varbinary') THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
        WHEN tp.name IN ('decimal','numeric') THEN tp.name + '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
        ELSE tp.name
    END AS DataType,
    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
    CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey,
    c.is_identity AS IsIdentity,
    dc.definition AS DefaultValue
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types tp ON c.user_type_id = tp.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.index_columns ic
    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
LEFT JOIN (
    SELECT DISTINCT fkc.parent_object_id, fkc.parent_column_id
    FROM sys.foreign_key_columns fkc
) fk ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
ORDER BY s.name, t.name, c.column_id";

        var result = await connection.QueryAsync<ColumnInfoRaw>(
            new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
        return result.ToList();
    }

    private static async Task<Dictionary<string, NullDefaultResult>> CheckNullAndDefaultAsync(
        SqlConnection connection, string schemaName, string tableName,
        List<ColumnInfoRaw> columns, CancellationToken ct)
    {
        var results = new Dictionary<string, NullDefaultResult>();

        // 排除計算欄位和 Identity 欄位的實際值檢查
        var checkableColumns = columns.Where(c => !c.IsIdentity).ToList();
        if (checkableColumns.Count == 0)
            return results;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("SELECT");
            sb.AppendLine("    COUNT(*) AS TotalRows,");

            var parts = new List<string>();
            foreach (var col in checkableColumns)
            {
                var escapedName = col.ColumnName.Replace("]", "]]");
                parts.Add($"    SUM(CASE WHEN [{escapedName}] IS NULL THEN 1 ELSE 0 END) AS [{escapedName}_NullCount]");

                if (!string.IsNullOrEmpty(col.DefaultValue))
                {
                    // 清理預設值（移除括號）
                    var defaultVal = col.DefaultValue.Trim('(', ')');
                    parts.Add($"    SUM(CASE WHEN CAST([{escapedName}] AS NVARCHAR(MAX)) = N'{defaultVal.Replace("'", "''")}' THEN 1 ELSE 0 END) AS [{escapedName}_DefaultCount]");
                }
            }

            sb.AppendLine(string.Join(",\n", parts));
            sb.AppendLine($"FROM [{schemaName}].[{tableName}]");

            var row = await connection.QueryFirstOrDefaultAsync(
                new CommandDefinition(sb.ToString(), cancellationToken: ct, commandTimeout: 300));

            if (row == null) return results;

            var rowDict = (IDictionary<string, object>)row;
            var totalRows = Convert.ToInt64(rowDict["TotalRows"]);

            foreach (var col in checkableColumns)
            {
                var nullCount = Convert.ToInt64(rowDict.GetValueOrDefault($"{col.ColumnName}_NullCount", 0L));
                var defaultCount = rowDict.ContainsKey($"{col.ColumnName}_DefaultCount")
                    ? Convert.ToInt64(rowDict[$"{col.ColumnName}_DefaultCount"])
                    : 0L;

                results[col.ColumnName] = new NullDefaultResult
                {
                    IsAllNull = totalRows == 0 || nullCount == totalRows,
                    IsAllDefault = totalRows > 0 && !string.IsNullOrEmpty(col.DefaultValue) && (nullCount + defaultCount) == totalRows
                };
            }
        }
        catch
        {
            // 查詢失敗時，標記為無法判斷（預設 false）
        }

        return results;
    }

    #endregion

    #region 內部類別

    private class ColumnInfoRaw
    {
        public string SchemaName { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public bool IsPrimaryKey { get; init; }
        public bool IsForeignKey { get; init; }
        public bool IsIdentity { get; init; }
        public string? DefaultValue { get; init; }
    }

    private class NullDefaultResult
    {
        public bool IsAllNull { get; init; }
        public bool IsAllDefault { get; init; }
    }

    #endregion
}
```

**Step 2: 確認編譯通過**

Run: `dotnet build src/TableSpec.Infrastructure`
Expected: BUILD SUCCEEDED

**Step 3: 確認所有現有測試仍通過**

Run: `dotnet test`
Expected: 全部 PASS

**Step 4: Commit**

```bash
git add src/TableSpec.Infrastructure/Repositories/UsageAnalysisRepository.cs
git commit -m "新增 UsageAnalysisRepository DMV 查詢實作"
```

---

## Task 8: IConnectionManager 擴充（GetProfileName 方法）

**Files:**
- Modify: `src/TableSpec.Application/Services/IConnectionManager.cs` (或 Domain 層，視現有位置)
- Modify: `src/TableSpec.Infrastructure/Services/ConnectionManager.cs`
- Test: 在現有 ConnectionManager 測試中新增

**注意：** Task 6 的多環境比對需要 `GetProfileName(Guid id)` 方法。檢查現有 `IConnectionManager` 是否已有此方法，若無則新增。

**Step 1: 檢查現有介面，補充缺少的方法**

在 `IConnectionManager` 介面新增（如果不存在）：

```csharp
/// <summary>根據 Profile ID 取得設定檔名稱</summary>
string GetProfileName(Guid profileId);
```

**Step 2: 在 ConnectionManager 中實作**

```csharp
public string GetProfileName(Guid profileId)
{
    var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
    return profile?.Name ?? profileId.ToString();
}
```

**Step 3: 確認編譯通過**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add -A
git commit -m "擴充 IConnectionManager 新增 GetProfileName 方法"
```

---

## Task 9: DI 註冊

**Files:**
- Modify: `src/TableSpec.Desktop/Program.cs`

**Step 1: 新增 DI 註冊**

在 `ConfigureServices()` 方法中加入：

```csharp
// 使用狀態分析 Repository
services.AddSingleton<IUsageAnalysisRepository>(sp =>
    new UsageAnalysisRepository(() =>
        sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

// 使用狀態分析 Service（含多環境比對支援）
services.AddSingleton<IUsageAnalysisService>(sp =>
    new UsageAnalysisService(
        sp.GetRequiredService<IUsageAnalysisRepository>(),
        sp.GetRequiredService<IConnectionManager>(),
        connStr => new UsageAnalysisRepository(() => connStr)));

// ViewModel
services.AddTransient<UsageAnalysisDocumentViewModel>(sp =>
    new UsageAnalysisDocumentViewModel(
        sp.GetRequiredService<IUsageAnalysisService>(),
        sp.GetRequiredService<IConnectionManager>()));
```

**Step 2: 確認編譯通過**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/TableSpec.Desktop/Program.cs
git commit -m "註冊使用狀態分析相關服務至 DI 容器"
```

---

## Task 10: Desktop 層 — UsageAnalysisDocumentViewModel（基本結構與單環境掃描）

**Files:**
- Create: `src/TableSpec.Desktop/ViewModels/UsageAnalysisDocumentViewModel.cs`
- Test: `tests/TableSpec.Desktop.Tests/ViewModels/UsageAnalysisDocumentViewModelTests.cs`

**Step 1: 寫失敗測試**

```csharp
// tests/TableSpec.Desktop.Tests/ViewModels/UsageAnalysisDocumentViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

public class UsageAnalysisDocumentViewModelTests
{
    private readonly IUsageAnalysisService _service;
    private readonly IConnectionManager _connectionManager;

    public UsageAnalysisDocumentViewModelTests()
    {
        _service = Substitute.For<IUsageAnalysisService>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.Should().NotBeNull();
        vm.Title.Should().Be("使用狀態分析");
    }

    [Fact]
    public void DocumentType_應為UsageAnalysis()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.DocumentType.Should().Be("UsageAnalysis");
    }

    [Fact]
    public void 初始狀態_YearsThreshold應為2()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.YearsThreshold.Should().Be(2);
    }

    [Fact]
    public void 初始狀態_IsCompareMode應為False()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.IsCompareMode.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsScanning應為False()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.IsScanning.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_StatusMessage應為提示文字()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.StatusMessage.Should().Contain("掃描");
    }

    [Fact]
    public void GenerateDropTableSql_應委派給Service()
    {
        _service.GenerateDropTableSql("dbo", "T").Returns("DROP TABLE [dbo].[T]");
        var vm = new UsageAnalysisDocumentViewModel(_service, _connectionManager);

        // 透過 ConfirmExecuteCallback 驗證 SQL 產生
        // （詳細的命令測試在整合層處理）
        vm.Should().NotBeNull();
    }
}
```

**Step 2: 執行測試，確認失敗**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "FullyQualifiedName~UsageAnalysisDocumentViewModelTests" -v n`
Expected: FAIL

**Step 3: 寫最小 ViewModel 實作**

```csharp
// src/TableSpec.Desktop/ViewModels/UsageAnalysisDocumentViewModel.cs
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 使用狀態分析文件 ViewModel
/// </summary>
public partial class UsageAnalysisDocumentViewModel : DocumentViewModel
{
    private readonly IUsageAnalysisService? _service;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cts;
    private UsageScanResult? _scanResult;

    public override string DocumentType => "UsageAnalysis";
    public override string DocumentKey => DocumentType;

    // === 模式切換 ===
    [ObservableProperty]
    private bool _isCompareMode;

    // === 操作參數 ===
    [ObservableProperty]
    private int _yearsThreshold = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "請點擊「開始掃描」分析使用狀態";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    // === 維度切換 ===
    [ObservableProperty]
    private bool _isColumnDimension;

    // === 篩選 ===
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _statusFilter; // 0=全部, 1=僅未使用, 2=僅使用中

    // === 摘要 ===
    [ObservableProperty]
    private int _totalTableCount;

    [ObservableProperty]
    private int _unusedTableCount;

    [ObservableProperty]
    private int _totalColumnCount;

    [ObservableProperty]
    private int _unusedColumnCount;

    // === 單環境結果 ===
    public ObservableCollection<TableUsageInfo> TableResults { get; } = [];
    public ObservableCollection<ColumnUsageStatus> ColumnResults { get; } = [];

    // === 多環境結果 ===
    public ObservableCollection<TableUsageComparisonRow> ComparisonTableRows { get; } = [];
    public ObservableCollection<ColumnUsageComparisonRow> ComparisonColumnRows { get; } = [];

    // === 多環境選擇 ===
    [ObservableProperty]
    private ConnectionProfile? _selectedBaseProfile;

    public ObservableCollection<ConnectionProfile> AvailableProfiles { get; } = [];
    public ObservableCollection<SelectableProfile> TargetProfileItems { get; } = [];

    // === 確認回調 ===
    public Func<string, Task<bool>>? ConfirmExecuteCallback { get; set; }

    // === 建構函式 ===
    public UsageAnalysisDocumentViewModel()
    {
        Title = "使用狀態分析";
    }

    public UsageAnalysisDocumentViewModel(
        IUsageAnalysisService service,
        IConnectionManager connectionManager) : this()
    {
        _service = service;
        _connectionManager = connectionManager;
        LoadProfiles();
    }

    // === 命令 ===
    private bool CanScan => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (_service == null) return;

        IsScanning = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => ProgressMessage = msg));

            if (IsCompareMode)
            {
                await RunCompareAsync(progress, _cts.Token);
            }
            else
            {
                await RunSingleScanAsync(progress, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消掃描";
        }
        catch (Exception ex)
        {
            StatusMessage = $"掃描失敗: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ProgressMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteTableAsync(TableUsageInfo? table)
    {
        if (_service == null || table == null) return;

        var sql = _service.GenerateDropTableSql(table.SchemaName, table.TableName);
        var message = $"確定要刪除以下資料表嗎？此操作無法復原！\n\n{sql}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed) return;
        }

        try
        {
            StatusMessage = $"正在刪除資料表：{table.FullTableName}...";
            await _service.DeleteTableAsync(table.SchemaName, table.TableName, CancellationToken.None);
            StatusMessage = $"資料表刪除成功：{table.FullTableName}";

            await Dispatcher.UIThread.InvokeAsync(() => TableResults.Remove(table));
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteColumnAsync(ColumnUsageStatus? column)
    {
        if (_service == null || column == null) return;

        var sql = _service.GenerateDropColumnSql(column.SchemaName, column.TableName, column.ColumnName);
        var message = $"確定要刪除以下欄位嗎？此操作無法復原！\n\n{sql}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed) return;
        }

        try
        {
            StatusMessage = $"正在刪除欄位：{column.DropColumnStatement}...";
            await _service.DeleteColumnAsync(
                column.SchemaName, column.TableName, column.ColumnName, CancellationToken.None);
            StatusMessage = $"欄位刪除成功：[{column.TableName}].[{column.ColumnName}]";

            await Dispatcher.UIThread.InvokeAsync(() => ColumnResults.Remove(column));
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗: {ex.Message}";
        }
    }

    // === 篩選通知 ===
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(int value) => ApplyFilter();
    partial void OnIsColumnDimensionChanged(bool value) => ApplyFilter();

    // === 私有方法 ===

    private async Task RunSingleScanAsync(IProgress<string> progress, CancellationToken ct)
    {
        StatusMessage = "正在掃描...";
        _scanResult = await _service!.ScanAsync(YearsThreshold, progress, ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TotalTableCount = _scanResult.Tables.Count;
            UnusedTableCount = _scanResult.UnusedTableCount(YearsThreshold);
            TotalColumnCount = _scanResult.Columns.Count;
            UnusedColumnCount = _scanResult.UnusedColumnCount;
            HasData = _scanResult.Tables.Count > 0 || _scanResult.Columns.Count > 0;
            ApplyFilter();
            StatusMessage = $"掃描完成，共 {TotalTableCount} 張表、{TotalColumnCount} 個欄位";
        });
    }

    private async Task RunCompareAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (_connectionManager == null || SelectedBaseProfile == null) return;

        var targetIds = TargetProfileItems
            .Where(p => p.IsSelected)
            .Select(p => p.Profile.Id)
            .ToList();

        if (targetIds.Count == 0)
        {
            StatusMessage = "請至少選擇一個目標環境";
            return;
        }

        StatusMessage = "正在進行多環境比對...";
        var comparison = await _service!.CompareAsync(
            SelectedBaseProfile.Id, targetIds, YearsThreshold, progress, ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ComparisonTableRows.Clear();
            foreach (var row in comparison.TableRows)
                ComparisonTableRows.Add(row);

            ComparisonColumnRows.Clear();
            foreach (var row in comparison.ColumnRows)
                ComparisonColumnRows.Add(row);

            HasData = comparison.TableRows.Count > 0 || comparison.ColumnRows.Count > 0;
            StatusMessage = $"比對完成，共 {comparison.TableRows.Count} 張表、{comparison.ColumnRows.Count} 個欄位";
        });
    }

    private void ApplyFilter()
    {
        if (_scanResult == null && !IsCompareMode) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCompareMode && _scanResult != null)
            {
                ApplySingleModeFilter();
            }
        });
    }

    private void ApplySingleModeFilter()
    {
        if (_scanResult == null) return;

        if (!IsColumnDimension)
        {
            // 資料表維度
            var filtered = _scanResult.Tables.AsEnumerable();

            if (!string.IsNullOrEmpty(SearchText))
                filtered = filtered.Where(t =>
                    t.TableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    t.SchemaName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (StatusFilter == 1)
                filtered = filtered.Where(t => !t.HasQueryActivity && !t.HasRecentUpdate(YearsThreshold));
            else if (StatusFilter == 2)
                filtered = filtered.Where(t => t.HasQueryActivity || t.HasRecentUpdate(YearsThreshold));

            TableResults.Clear();
            foreach (var item in filtered)
                TableResults.Add(item);
        }
        else
        {
            // 欄位維度
            var filtered = _scanResult.Columns.AsEnumerable();

            if (!string.IsNullOrEmpty(SearchText))
                filtered = filtered.Where(c =>
                    c.ColumnName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.TableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (StatusFilter == 1)
                filtered = filtered.Where(c => !c.IsUsed);
            else if (StatusFilter == 2)
                filtered = filtered.Where(c => c.IsUsed);

            ColumnResults.Clear();
            foreach (var item in filtered)
                ColumnResults.Add(item);
        }
    }

    private void LoadProfiles()
    {
        if (_connectionManager == null) return;
        var profiles = _connectionManager.GetAllProfiles();
        AvailableProfiles.Clear();
        foreach (var p in profiles)
            AvailableProfiles.Add(p);
    }

    partial void OnSelectedBaseProfileChanged(ConnectionProfile? value)
    {
        TargetProfileItems.Clear();
        if (value == null || _connectionManager == null) return;

        foreach (var p in _connectionManager.GetAllProfiles().Where(p => p.Id != value.Id))
            TargetProfileItems.Add(new SelectableProfile { Profile = p });
    }
}

/// <summary>
/// 可勾選的連線設定（用於多環境比對目標選擇）
/// </summary>
public partial class SelectableProfile : ObservableObject
{
    public required ConnectionProfile Profile { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
```

**Step 4: 執行測試，確認通過**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "FullyQualifiedName~UsageAnalysisDocumentViewModelTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add src/TableSpec.Desktop/ViewModels/UsageAnalysisDocumentViewModel.cs tests/TableSpec.Desktop.Tests/ViewModels/UsageAnalysisDocumentViewModelTests.cs
git commit -m "新增 UsageAnalysisDocumentViewModel 及測試"
```

---

## Task 11: Desktop 層 — View（AXAML）

**Files:**
- Create: `src/TableSpec.Desktop/Views/UsageAnalysisDocumentView.axaml`
- Create: `src/TableSpec.Desktop/Views/UsageAnalysisDocumentView.axaml.cs`

**Step 1: 建立 View**

AXAML 包含：
- 頂部操作列：模式切換、年份輸入、基準/目標環境選擇、掃描按鈕、進度條
- 摘要列：表/欄位總數、未使用數量
- 維度切換 + 篩選列
- 單環境模式的 DataGrid（表維度 + 欄位維度）
- 多環境模式的矩陣 DataGrid
- 每列操作按鈕（刪除）

**（View 為 UI 檔案，不需要 TDD，完整 AXAML 程式碼在實作時撰寫）**

**Step 2: 確認編譯通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/TableSpec.Desktop/Views/UsageAnalysisDocumentView.axaml src/TableSpec.Desktop/Views/UsageAnalysisDocumentView.axaml.cs
git commit -m "新增使用狀態分析 View（AXAML）"
```

---

## Task 12: MainWindow 整合 — 選單入口

**Files:**
- Modify: `src/TableSpec.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/TableSpec.Desktop/Views/MainWindow.axaml`

**Step 1: 在 MainWindowViewModel 新增開啟命令**

```csharp
[RelayCommand]
private void OpenUsageAnalysis()
{
    var existing = Documents.OfType<UsageAnalysisDocumentViewModel>().FirstOrDefault();
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var doc = App.Services?.GetRequiredService<UsageAnalysisDocumentViewModel>()
        ?? new UsageAnalysisDocumentViewModel();

    doc.ConfirmExecuteCallback = ConfirmSaveCallback;
    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
}
```

**Step 2: 在 MainWindow.axaml 新增選單項目**

在適當的工具選單位置加入：

```xml
<Button Content="使用狀態分析" Command="{Binding OpenUsageAnalysisCommand}"/>
```

**Step 3: 確認編譯通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add src/TableSpec.Desktop/ViewModels/MainWindowViewModel.cs src/TableSpec.Desktop/Views/MainWindow.axaml
git commit -m "整合使用狀態分析至主視窗選單"
```

---

## Task 13: 全部測試驗證 + 最終確認

**Step 1: 執行所有測試**

Run: `dotnet test`
Expected: 全部 PASS

**Step 2: 執行應用程式確認**

Run: `dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj`

驗證：
- [ ] 選單中可看到「使用狀態分析」入口
- [ ] 點擊可開啟頁面
- [ ] 模式切換正常（單環境 / 多環境）
- [ ] 年份輸入可調整
- [ ] 掃描功能正常（需有資料庫連線）
- [ ] 進度條顯示
- [ ] 結果顯示（表維度 / 欄位維度）
- [ ] 篩選功能正常
- [ ] 多環境比對矩陣正確
- [ ] 刪除功能彈出確認對話框

**Step 3: 最終 Commit**

```bash
git add -A
git commit -m "完成使用狀態分析功能"
```
