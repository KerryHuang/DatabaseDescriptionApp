# 維護計劃 — 檔案調校與完整性檢查 實作計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在「維護計劃」精靈 Step 2 新增三個步驟：`AdjustAutoGrowth`、`PreExpandDataFile`、`CreateCheckDbJob`。

**Architecture:** 沿用既有 Clean Architecture 分層 + 既有檢查/SQL 產生/執行三段模式。新增 Domain Entity `DatabaseFileInfo` 承載檔案 + 磁碟資訊；Repository 補一個查詢方法；Service 擴充 `CheckStepsAsync` 的 switch 與 `ExecutePlanAsync` 的群組；SqlGenerator 新增三個產生方法；ViewModel 與 View 各加三組欄位/CheckBox。

**Tech Stack:** .NET 8、CommunityToolkit.Mvvm、Avalonia 11、Dapper、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions

**Spec:** `docs/superpowers/specs/2026-05-15-maintenance-plan-file-tuning-design.md`

---

## File Structure

| 動作 | 檔案 | 責任 |
|------|------|------|
| Create | `src/Specurai.Domain/Entities/DatabaseFileInfo.cs` | 載入單一資料庫檔案 + 所在磁碟資訊的純資料載體 |
| Modify | `src/Specurai.Domain/Enums/MaintenancePlanStep.cs` | 新增三個 enum 值 |
| Modify | `src/Specurai.Domain/Entities/MaintenancePlanConfig.cs` | 新增 5 個欄位（autogrowth、預擴緩衝、CheckDB 排程） |
| Modify | `src/Specurai.Domain/Interfaces/IDatabaseInfoRepository.cs` | 新增 `GetDatabaseFilesAsync` |
| Modify | `src/Specurai.Infrastructure/Repositories/DatabaseInfoRepository.cs` | 實作 `GetDatabaseFilesAsync` |
| Modify | `src/Specurai.Application/Models/StepCheckResult.cs` | `StepName` switch 加三個分支 |
| Modify | `src/Specurai.Application/Services/MaintenancePlanService.cs` | `CheckStepsAsync` switch + `ExecutePlanAsync` 群組擴充 |
| Modify | `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs` | 三個新產生方法 + `GenerateStepSql` switch + `GenerateFullSql` 群組 |
| Modify | `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs` | 三組 IsXxxSelected / XxxStatus + `RunStep2ChecksAsync`、`GetSelectedSteps` 擴充 |
| Modify | `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml` | 三組 CheckBox + Status TextBlock |
| Create | `tests/Specurai.Domain.Tests/Entities/DatabaseFileInfoTests.cs` | Entity 屬性測試 |
| Modify | `tests/Specurai.Domain.Tests/Entities/MaintenancePlanConfigTests.cs` | 新欄位預設值測試 |
| Modify | `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs` | 三新步驟檢查邏輯測試 |
| Modify | `tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs` | 三新產生方法測試 |

---

## Task 1：Domain — DatabaseFileInfo Entity

**Files:**
- Create: `src/Specurai.Domain/Entities/DatabaseFileInfo.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/DatabaseFileInfoTests.cs`

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/Specurai.Domain.Tests/Entities/DatabaseFileInfoTests.cs
using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class DatabaseFileInfoTests
{
    [Fact]
    public void 建立_應正確設定所有屬性()
    {
        var info = new DatabaseFileInfo
        {
            LogicalName = "MyDb",
            PhysicalName = @"D:\Data\MyDb.mdf",
            FileType = DatabaseFileType.Data,
            SizeMB = 25600,
            FreeMB = 1280,
            IsPercentGrowth = false,
            GrowthMB = 256,
            VolumeMountPoint = @"D:\",
            VolumeFreeGB = 50
        };

        info.LogicalName.Should().Be("MyDb");
        info.FileType.Should().Be(DatabaseFileType.Data);
        info.FreePercent.Should().BeApproximately(5.0m, 0.01m);
    }

    [Fact]
    public void FreePercent_當SizeMB為零_應回傳零()
    {
        var info = new DatabaseFileInfo
        {
            LogicalName = "L",
            PhysicalName = "P",
            FileType = DatabaseFileType.Log,
            SizeMB = 0,
            FreeMB = 0,
            IsPercentGrowth = false,
            GrowthMB = 0,
            VolumeMountPoint = "X",
            VolumeFreeGB = null
        };

        info.FreePercent.Should().Be(0);
    }
}
```

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~DatabaseFileInfoTests"`
Expected: FAIL — 找不到 `DatabaseFileInfo` / `DatabaseFileType`

- [ ] **Step 3：建立 Entity**

```csharp
// src/Specurai.Domain/Entities/DatabaseFileInfo.cs
namespace Specurai.Domain.Entities;

/// <summary>資料庫檔案類型</summary>
public enum DatabaseFileType
{
    Data = 0,
    Log = 1
}

/// <summary>資料庫檔案資訊（含所在磁碟空間）</summary>
public class DatabaseFileInfo
{
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required DatabaseFileType FileType { get; init; }
    /// <summary>檔案目前大小（MB）</summary>
    public required int SizeMB { get; init; }
    /// <summary>檔案內可用空間（MB）= Size - SpaceUsed</summary>
    public required int FreeMB { get; init; }
    /// <summary>autogrowth 是否為百分比模式</summary>
    public required bool IsPercentGrowth { get; init; }
    /// <summary>autogrowth 數值；IsPercentGrowth=false 時單位為 MB，true 時為百分比</summary>
    public required int GrowthMB { get; init; }
    /// <summary>檔案所在磁碟掛載點（Windows 為 "D:\\"，Linux 為 "/"）</summary>
    public required string VolumeMountPoint { get; init; }
    /// <summary>檔案所在磁碟可用空間（GB）；查不到時為 null</summary>
    public int? VolumeFreeGB { get; init; }

    /// <summary>檔案內可用空間百分比</summary>
    public decimal FreePercent => SizeMB == 0 ? 0m : (decimal)FreeMB * 100m / SizeMB;
}
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~DatabaseFileInfoTests"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Domain/Entities/DatabaseFileInfo.cs tests/Specurai.Domain.Tests/Entities/DatabaseFileInfoTests.cs
git commit -m "feat(domain): 新增 DatabaseFileInfo entity 承載資料檔與磁碟資訊"
```

---

## Task 2：Domain — MaintenancePlanStep enum 新增 3 值

**Files:**
- Modify: `src/Specurai.Domain/Enums/MaintenancePlanStep.cs`

- [ ] **Step 1：替換整個檔案**

```csharp
// src/Specurai.Domain/Enums/MaintenancePlanStep.cs
namespace Specurai.Domain.Enums;

/// <summary>維護計劃步驟（順序即 UI 顯示順序）</summary>
public enum MaintenancePlanStep
{
    /// <summary>更新資料庫相容性層級至當前 SQL Server 版本</summary>
    SetCompatibilityLevel,
    /// <summary>設定 Recovery Model 為 SIMPLE</summary>
    SetRecoveryModel,
    /// <summary>調整檔案自動成長設定（mdf 256MB / ldf 128MB）</summary>
    AdjustAutoGrowth,
    /// <summary>預擴資料檔（保留成長緩衝）</summary>
    PreExpandDataFile,
    /// <summary>重新命名邏輯檔名</summary>
    RenameLogicalFiles,
    /// <summary>建立登入帳號與使用者</summary>
    CreateLoginAndUser,
    /// <summary>將使用者加入 db_owner</summary>
    AddToDbOwner,
    /// <summary>建立每日全備份排程</summary>
    CreateBackupJob,
    /// <summary>建立每週完整性檢查排程（DBCC CHECKDB）</summary>
    CreateCheckDbJob,
    /// <summary>建立每日還原排程</summary>
    CreateRestoreJob
}
```

- [ ] **Step 2：建置確認沒破壞既有 switch**

Run: `dotnet build`
Expected: SUCCESS（既有 switch 都用具名 case，新值不會強制 break）

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Domain/Enums/MaintenancePlanStep.cs
git commit -m "feat(domain): MaintenancePlanStep 新增 AdjustAutoGrowth/PreExpandDataFile/CreateCheckDbJob"
```

---

## Task 3：Domain — MaintenancePlanConfig 新增 5 欄位

**Files:**
- Modify: `src/Specurai.Domain/Entities/MaintenancePlanConfig.cs`
- Modify: `tests/Specurai.Domain.Tests/Entities/MaintenancePlanConfigTests.cs`

- [ ] **Step 1：寫失敗測試（追加到既有測試類別）**

在 `MaintenancePlanConfigTests` 內追加：

```csharp
[Fact]
public void 預設值_AutoGrowthDataMB為256()
{
    var config = BuildConfig();
    config.AutoGrowthDataMB.Should().Be(256);
    config.AutoGrowthLogMB.Should().Be(128);
    config.PreExpandBufferGB.Should().Be(5);
    config.CheckDbTime.Should().Be(3);
    config.CheckDbDayOfWeek.Should().Be(DayOfWeek.Sunday);
}

private static MaintenancePlanConfig BuildConfig() => new()
{
    DatabaseName = "DB",
    BackupPath = @"D:\Backup\",
    RestorePath = @"D:\Restore\",
    TestDatabaseName = "DB-test",
    LoginName = "u",
    LoginPassword = "p",
    BackupTime = 2,
    RestoreTime = 3,
    SelectedSteps = []
};
```

> 若 `BuildConfig` 已存在於該檔案，請複用既有版本，只新增 `[Fact]` 即可。

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~MaintenancePlanConfigTests.預設值_AutoGrowthDataMB為256"`
Expected: FAIL — 屬性不存在

- [ ] **Step 3：在 MaintenancePlanConfig 加欄位**

在 `RecoveryModel` 屬性之後追加：

```csharp
/// <summary>資料檔 autogrowth 固定 MB</summary>
public int AutoGrowthDataMB { get; init; } = 256;
/// <summary>記錄檔 autogrowth 固定 MB</summary>
public int AutoGrowthLogMB { get; init; } = 128;
/// <summary>預擴資料檔的緩衝 GB（目前大小 + 此值，再湊整到 GB）</summary>
public int PreExpandBufferGB { get; init; } = 5;
/// <summary>CheckDB 排程小時（0-23）</summary>
public int CheckDbTime { get; init; } = 3;
/// <summary>CheckDB 排程星期</summary>
public DayOfWeek CheckDbDayOfWeek { get; init; } = DayOfWeek.Sunday;
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests --filter "FullyQualifiedName~MaintenancePlanConfigTests"`
Expected: 全部 PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Domain/Entities/MaintenancePlanConfig.cs tests/Specurai.Domain.Tests/Entities/MaintenancePlanConfigTests.cs
git commit -m "feat(domain): MaintenancePlanConfig 新增 autogrowth/預擴/CheckDB 排程欄位"
```

---

## Task 4：Application — StepCheckResult.StepName 三新分支

**Files:**
- Modify: `src/Specurai.Application/Models/StepCheckResult.cs`

- [ ] **Step 1：在 StepName switch 表達式中加入三個 case**

找到 `public string StepName => Step switch` 區塊，在 `MaintenancePlanStep.CreateRestoreJob => "建立還原排程",` 之前的適當位置加入：

```csharp
MaintenancePlanStep.AdjustAutoGrowth => "調整檔案成長設定",
MaintenancePlanStep.PreExpandDataFile => "預擴資料檔",
MaintenancePlanStep.CreateCheckDbJob => "建立完整性檢查排程",
```

完整 switch 應為：

```csharp
public string StepName => Step switch
{
    MaintenancePlanStep.SetCompatibilityLevel => "更新相容性層級",
    MaintenancePlanStep.SetRecoveryModel => "設定 Recovery Model",
    MaintenancePlanStep.AdjustAutoGrowth => "調整檔案成長設定",
    MaintenancePlanStep.PreExpandDataFile => "預擴資料檔",
    MaintenancePlanStep.RenameLogicalFiles => "重新命名邏輯檔名",
    MaintenancePlanStep.CreateLoginAndUser => "建立登入帳號與使用者",
    MaintenancePlanStep.AddToDbOwner => "加入 db_owner 角色",
    MaintenancePlanStep.CreateBackupJob => "建立備份排程",
    MaintenancePlanStep.CreateCheckDbJob => "建立完整性檢查排程",
    MaintenancePlanStep.CreateRestoreJob => "建立還原排程",
    _ => Step.ToString()
};
```

- [ ] **Step 2：建置**

Run: `dotnet build`
Expected: SUCCESS

- [ ] **Step 3：Commit**

```bash
git add src/Specurai.Application/Models/StepCheckResult.cs
git commit -m "feat(application): StepCheckResult 新增三步驟中文名稱"
```

---

## Task 5：Domain — IDatabaseInfoRepository 新增 GetDatabaseFilesAsync

**Files:**
- Modify: `src/Specurai.Domain/Interfaces/IDatabaseInfoRepository.cs`

- [ ] **Step 1：在介面尾端追加方法**

在 `ExecuteSqlAsync` 之後加入：

```csharp
/// <summary>
/// 取得指定資料庫的所有檔案資訊（含所在磁碟可用空間）
/// </summary>
Task<IReadOnlyList<DatabaseFileInfo>> GetDatabaseFilesAsync(string databaseName, CancellationToken ct = default);
```

並在檔案頂端加入 `using Specurai.Domain.Entities;`（若尚未存在）。

- [ ] **Step 2：建置（會在 Repository 實作出現編譯錯誤）**

Run: `dotnet build`
Expected: FAIL — `DatabaseInfoRepository` 未實作介面

- [ ] **Step 3：Commit（介面變更獨立 commit）**

```bash
git add src/Specurai.Domain/Interfaces/IDatabaseInfoRepository.cs
git commit -m "feat(domain): IDatabaseInfoRepository 新增 GetDatabaseFilesAsync"
```

---

## Task 6：Infrastructure — 實作 GetDatabaseFilesAsync

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/DatabaseInfoRepository.cs`

- [ ] **Step 1：先 Read 現有檔案找到適合插入點**

Run（Read 工具）: 開啟 `src/Specurai.Infrastructure/Repositories/DatabaseInfoRepository.cs`，定位到任一既有方法之後（例如 `ExecuteSqlAsync` 結尾）。

- [ ] **Step 2：在類別尾端加入實作**

```csharp
public async Task<IReadOnlyList<DatabaseFileInfo>> GetDatabaseFilesAsync(string databaseName, CancellationToken ct = default)
{
    var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync(ct);

    // 切換到目標 DB 後查 sys.database_files + sys.dm_os_volume_stats
    // 注意：FILEPROPERTY 必須在目標 DB 的 context 才能解析；此處用 USE 切換
    var sql = $@"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
USE [{databaseName.Replace("]", "]]")}];
SELECT
    f.name                                              AS LogicalName,
    f.physical_name                                     AS PhysicalName,
    f.type                                              AS FileTypeRaw,
    CAST(f.size * 8 / 1024 AS INT)                      AS SizeMB,
    CAST((f.size - FILEPROPERTY(f.name, 'SpaceUsed')) * 8 / 1024 AS INT) AS FreeMB,
    f.is_percent_growth                                 AS IsPercentGrowth,
    CASE WHEN f.is_percent_growth = 1
         THEN f.growth
         ELSE CAST(f.growth * 8 / 1024 AS INT) END     AS GrowthMB,
    vs.volume_mount_point                               AS VolumeMountPoint,
    CAST(vs.available_bytes / 1073741824 AS INT)        AS VolumeFreeGB
FROM sys.database_files f
OUTER APPLY sys.dm_os_volume_stats(DB_ID(), f.file_id) vs;
";

    var rows = await conn.QueryAsync<DatabaseFileRow>(new CommandDefinition(sql, cancellationToken: ct));
    return rows.Select(r => new DatabaseFileInfo
    {
        LogicalName = r.LogicalName,
        PhysicalName = r.PhysicalName,
        FileType = r.FileTypeRaw == 1 ? DatabaseFileType.Log : DatabaseFileType.Data,
        SizeMB = r.SizeMB,
        FreeMB = r.FreeMB,
        IsPercentGrowth = r.IsPercentGrowth,
        GrowthMB = r.GrowthMB,
        VolumeMountPoint = r.VolumeMountPoint ?? string.Empty,
        VolumeFreeGB = r.VolumeFreeGB
    }).ToList();
}

private sealed class DatabaseFileRow
{
    public string LogicalName { get; set; } = string.Empty;
    public string PhysicalName { get; set; } = string.Empty;
    public byte FileTypeRaw { get; set; }
    public int SizeMB { get; set; }
    public int FreeMB { get; set; }
    public bool IsPercentGrowth { get; set; }
    public int GrowthMB { get; set; }
    public string? VolumeMountPoint { get; set; }
    public int? VolumeFreeGB { get; set; }
}
```

> 若檔案頂端尚未 `using Specurai.Domain.Entities;` 與 `using Dapper;`、`using Microsoft.Data.SqlClient;`，請補上（依既有慣例）。

- [ ] **Step 3：建置**

Run: `dotnet build`
Expected: SUCCESS

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/DatabaseInfoRepository.cs
git commit -m "feat(infra): 實作 GetDatabaseFilesAsync 查詢檔案與磁碟空間"
```

---

## Task 7：Application — Service 檢查 AutoGrowth

**Files:**
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs`

- [ ] **Step 1：寫失敗測試**

在 `MaintenancePlanServiceTests` 追加：

```csharp
[Fact]
public async Task CheckSteps_AutoGrowth_當mdf小於64MB或ldf為百分比_應標記需調整()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    var jobRepo = Substitute.For<IAgentJobRepository>();
    var sqlGen = Substitute.For<IMaintenancePlanSqlGenerator>();

    dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>())
        .Returns(new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25600, FreeMB = 1280, IsPercentGrowth = false, GrowthMB = 1,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 },
            new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log,
                    SizeMB = 1024, FreeMB = 512, IsPercentGrowth = true, GrowthMB = 10,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
        });

    var svc = new MaintenancePlanService(dbRepo, jobRepo, sqlGen);
    var config = MakeConfig(MaintenancePlanStep.AdjustAutoGrowth);
    var results = await svc.CheckStepsAsync(config);

    var r = results.Single();
    r.Step.Should().Be(MaintenancePlanStep.AdjustAutoGrowth);
    r.AlreadyExists.Should().BeFalse();
    r.CurrentStatus.Should().Contain("需調整");
}

[Fact]
public async Task CheckSteps_AutoGrowth_當所有檔案皆為固定且至少64MB_應標記已最佳化()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    var jobRepo = Substitute.For<IAgentJobRepository>();
    var sqlGen = Substitute.For<IMaintenancePlanSqlGenerator>();

    dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>())
        .Returns(new List<DatabaseFileInfo>
        {
            new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                    SizeMB = 25600, FreeMB = 1280, IsPercentGrowth = false, GrowthMB = 256,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 },
            new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log,
                    SizeMB = 1024, FreeMB = 512, IsPercentGrowth = false, GrowthMB = 128,
                    VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
        });

    var svc = new MaintenancePlanService(dbRepo, jobRepo, sqlGen);
    var config = MakeConfig(MaintenancePlanStep.AdjustAutoGrowth);
    var results = await svc.CheckStepsAsync(config);

    results.Single().AlreadyExists.Should().BeTrue();
    results.Single().CurrentStatus.Should().Contain("已最佳化");
}

private static MaintenancePlanConfig MakeConfig(params MaintenancePlanStep[] steps) => new()
{
    DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
    TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
    BackupTime = 2, RestoreTime = 3, SelectedSteps = steps
};
```

> 若已有同名 `MakeConfig`/`BuildConfig` 私有方法，複用既有版本即可。

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_AutoGrowth"`
Expected: FAIL — switch 沒有 AdjustAutoGrowth case

- [ ] **Step 3：在 MaintenancePlanService.CheckStepsAsync 的 switch 加入新 case**

在 `MaintenancePlanService.CheckStepsAsync` 的 `switch (step)` 中新增 case，並在類別尾端加入私有方法：

```csharp
// switch 內新增
MaintenancePlanStep.AdjustAutoGrowth => await CheckAutoGrowthAsync(config, ct),
```

```csharp
// 類別尾端新增私有方法
private async Task<StepCheckResult> CheckAutoGrowthAsync(MaintenancePlanConfig config, CancellationToken ct)
{
    var files = await _dbInfoRepo.GetDatabaseFilesAsync(config.DatabaseName, ct);
    var problems = files.Where(f =>
        f.IsPercentGrowth ||
        f.GrowthMB < 64).ToList();
    var optimal = problems.Count == 0 && files.Count > 0;

    var dataFile = files.FirstOrDefault(f => f.FileType == DatabaseFileType.Data);
    var logFile = files.FirstOrDefault(f => f.FileType == DatabaseFileType.Log);

    return new StepCheckResult
    {
        Step = MaintenancePlanStep.AdjustAutoGrowth,
        AlreadyExists = optimal,
        CurrentStatus = optimal
            ? $"自動成長設定已最佳化（資料檔 {dataFile?.GrowthMB} MB / 記錄檔 {logFile?.GrowthMB} MB）"
            : $"自動成長設定需調整（{string.Join(", ", problems.Select(p => $"{p.LogicalName}: {(p.IsPercentGrowth ? p.GrowthMB + "%" : p.GrowthMB + " MB")}"))}）",
        AvailableActions = optimal ? ["跳過"] : ["執行", "跳過"]
    };
}
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_AutoGrowth"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Application/Services/MaintenancePlanService.cs tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "feat(application): 維護計劃新增 AdjustAutoGrowth 檢查邏輯"
```

---

## Task 8：Application — Service 檢查 PreExpand

**Files:**
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs`

- [ ] **Step 1：寫失敗測試（追加）**

```csharp
[Fact]
public async Task CheckSteps_PreExpand_當資料檔可用率小於20pct_應建議預擴()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                SizeMB = 25600, FreeMB = 1024, // 4%
                IsPercentGrowth = false, GrowthMB = 256,
                VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
    });
    var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

    var r = (await svc.CheckStepsAsync(MakeConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

    r.AlreadyExists.Should().BeFalse();
    r.CurrentStatus.Should().Contain("建議預擴");
}

[Fact]
public async Task CheckSteps_PreExpand_當磁碟空間不足_應標記跳過且不可執行()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    // 需擴 5 GB，但磁碟僅剩 4 GB（< 5 * 1.5 = 7.5 GB）
    dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                SizeMB = 25600, FreeMB = 100,
                IsPercentGrowth = false, GrowthMB = 256,
                VolumeMountPoint = @"D:\", VolumeFreeGB = 4 }
    });
    var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

    var r = (await svc.CheckStepsAsync(MakeConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

    r.AlreadyExists.Should().BeTrue();
    r.CurrentStatus.Should().Contain("磁碟空間不足");
    r.AvailableActions.Should().BeEquivalentTo(["跳過"]);
}

[Fact]
public async Task CheckSteps_PreExpand_當可用率大於等於20pct_應標記空間充足()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    dbRepo.GetDatabaseFilesAsync("DB", Arg.Any<CancellationToken>()).Returns(new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                SizeMB = 10240, FreeMB = 5120, // 50%
                IsPercentGrowth = false, GrowthMB = 256,
                VolumeMountPoint = @"D:\", VolumeFreeGB = 100 }
    });
    var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

    var r = (await svc.CheckStepsAsync(MakeConfig(MaintenancePlanStep.PreExpandDataFile))).Single();

    r.AlreadyExists.Should().BeTrue();
    r.CurrentStatus.Should().Contain("空間充足");
}
```

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_PreExpand"`
Expected: FAIL

- [ ] **Step 3：實作 case + 私有方法**

switch 加入：

```csharp
MaintenancePlanStep.PreExpandDataFile => await CheckPreExpandAsync(config, ct),
```

新增私有方法：

```csharp
private async Task<StepCheckResult> CheckPreExpandAsync(MaintenancePlanConfig config, CancellationToken ct)
{
    var files = await _dbInfoRepo.GetDatabaseFilesAsync(config.DatabaseName, ct);
    var dataFiles = files.Where(f => f.FileType == DatabaseFileType.Data).ToList();

    if (dataFiles.Count == 0)
    {
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.PreExpandDataFile,
            AlreadyExists = true,
            CurrentStatus = "找不到資料檔",
            AvailableActions = ["跳過"]
        };
    }

    // 磁碟空間檢查：擴增量 = bufferGB；護欄 = 1.5x
    var bufferGB = config.PreExpandBufferGB;
    var insufficient = dataFiles.FirstOrDefault(f =>
        f.VolumeFreeGB.HasValue && f.VolumeFreeGB.Value < bufferGB * 1.5);
    if (insufficient is not null)
    {
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.PreExpandDataFile,
            AlreadyExists = true,
            CurrentStatus = $"磁碟空間不足，跳過（{insufficient.VolumeMountPoint} free {insufficient.VolumeFreeGB} GB < 需要 {bufferGB * 1.5} GB）",
            AvailableActions = ["跳過"]
        };
    }

    // 可用率 < 20% → 建議
    var lowSpace = dataFiles.FirstOrDefault(f => f.FreePercent < 20m);
    if (lowSpace is not null)
    {
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.PreExpandDataFile,
            AlreadyExists = false,
            CurrentStatus = $"建議預擴（{lowSpace.LogicalName} 可用 {lowSpace.FreePercent:0.0}%）",
            AvailableActions = ["執行", "跳過"]
        };
    }

    var minPct = dataFiles.Min(f => f.FreePercent);
    return new StepCheckResult
    {
        Step = MaintenancePlanStep.PreExpandDataFile,
        AlreadyExists = true,
        CurrentStatus = $"資料檔可用空間充足（{minPct:0.0}%）",
        AvailableActions = ["跳過"]
    };
}
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_PreExpand"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Application/Services/MaintenancePlanService.cs tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "feat(application): 維護計劃新增 PreExpandDataFile 檢查邏輯"
```

---

## Task 9：Application — Service 檢查 CreateCheckDbJob

**Files:**
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs`

- [ ] **Step 1：寫失敗測試**

```csharp
[Fact]
public async Task CheckSteps_CreateCheckDbJob_當Job存在_應標記已存在()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    dbRepo.AgentJobExistsAsync("DB_CheckDb", Arg.Any<CancellationToken>()).Returns(true);
    // 上游 GetRecoveryModelAsync 也會被呼叫；給個值避免 null
    dbRepo.GetRecoveryModelAsync("DB", Arg.Any<CancellationToken>()).Returns("SIMPLE");

    var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

    var r = (await svc.CheckStepsAsync(MakeConfig(MaintenancePlanStep.CreateCheckDbJob))).Single();

    r.AlreadyExists.Should().BeTrue();
    r.CurrentStatus.Should().Contain("DB_CheckDb").And.Contain("已存在");
}

[Fact]
public async Task CheckSteps_CreateCheckDbJob_當Job不存在_應允許建立()
{
    var dbRepo = Substitute.For<IDatabaseInfoRepository>();
    dbRepo.AgentJobExistsAsync("DB_CheckDb", Arg.Any<CancellationToken>()).Returns(false);
    dbRepo.GetRecoveryModelAsync("DB", Arg.Any<CancellationToken>()).Returns("SIMPLE");

    var svc = new MaintenancePlanService(dbRepo, Substitute.For<IAgentJobRepository>(), Substitute.For<IMaintenancePlanSqlGenerator>());

    var r = (await svc.CheckStepsAsync(MakeConfig(MaintenancePlanStep.CreateCheckDbJob))).Single();

    r.AlreadyExists.Should().BeFalse();
    r.AvailableActions.Should().Contain("建立");
}
```

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_CreateCheckDbJob"`
Expected: FAIL

- [ ] **Step 3：在 switch 加 case，複用既有 `CheckJobAsync`**

```csharp
MaintenancePlanStep.CreateCheckDbJob => await CheckJobAsync(config, MaintenancePlanStep.CreateCheckDbJob, $"{config.DatabaseName}_CheckDb", ct),
```

> `CheckJobAsync` 已存在且通用，無需新增方法。

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests --filter "FullyQualifiedName~CheckSteps_CreateCheckDbJob"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Application/Services/MaintenancePlanService.cs tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "feat(application): 維護計劃新增 CreateCheckDbJob 檢查邏輯"
```

---

## Task 10：Infrastructure — SqlGenerator AdjustAutoGrowth

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`
- Modify: `tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs`

> ⚠️ AdjustAutoGrowth 與 PreExpandDataFile 需要 `DatabaseFileInfo` 清單才能產 SQL，但既有 `GenerateStepSql(step, config, action)` 簽章不含 file info。決定：在 `MaintenancePlanService.ExecutePlanAsync` 與 `GenerateFullSql` 內，當步驟為這兩者時，**不透過 `GenerateStepSql` 路徑**，改直接呼叫專屬方法（service 端先查檔案再呼叫產生器）。`GenerateStepSql` 對這兩個 step 回傳空字串以維持介面相容。

- [ ] **Step 1：在介面 `IMaintenancePlanSqlGenerator` 新增專屬方法**

修改 `src/Specurai.Application/Services/IMaintenancePlanSqlGenerator.cs`，在介面尾端追加：

```csharp
/// <summary>產生 AdjustAutoGrowth 的 SQL（對所有檔案套用 MODIFY FILE FILEGROWTH）</summary>
string GenerateAdjustAutoGrowthSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> files);

/// <summary>產生 PreExpandDataFile 的 SQL（對每個資料檔擴到「目前大小+bufferGB」湊整 GB）</summary>
string GeneratePreExpandDataFileSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> dataFiles);

/// <summary>產生 CreateCheckDbJob 的 SQL</summary>
string GenerateCreateCheckDbJobSql(MaintenancePlanConfig config, string? action = null);
```

並補 `using Specurai.Domain.Entities;`。

- [ ] **Step 2：寫失敗測試**

在 `MaintenancePlanSqlGeneratorTests` 追加：

```csharp
[Fact]
public void GenerateAdjustAutoGrowthSql_應產出每檔的MODIFY_FILE_語句()
{
    var gen = new MaintenancePlanSqlGenerator();
    var config = new MaintenancePlanConfig
    {
        DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = []
    };
    var files = new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data, SizeMB = 1, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 1, VolumeMountPoint = "D", VolumeFreeGB = null },
        new() { LogicalName = "DB_log", PhysicalName = "x", FileType = DatabaseFileType.Log, SizeMB = 1, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 1, VolumeMountPoint = "D", VolumeFreeGB = null }
    };

    var sql = gen.GenerateAdjustAutoGrowthSql(config, files);

    sql.Should().Contain("ALTER DATABASE [DB]");
    sql.Should().Contain("NAME = N'DB'").And.Contain("FILEGROWTH = 256MB");
    sql.Should().Contain("NAME = N'DB_log'").And.Contain("FILEGROWTH = 128MB");
}
```

- [ ] **Step 3：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GenerateAdjustAutoGrowthSql"`
Expected: FAIL — 方法未實作

- [ ] **Step 4：實作（在 `MaintenancePlanSqlGenerator` 類別尾端，靠近 `private static string GenerateXxx` 方法群）**

```csharp
public string GenerateAdjustAutoGrowthSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> files)
{
    var sb = new StringBuilder();
    var db = QuoteName(config.DatabaseName);
    sb.AppendLine($"-- 調整 {db} 的檔案 autogrowth");
    foreach (var f in files)
    {
        var growMB = f.FileType == DatabaseFileType.Data ? config.AutoGrowthDataMB : config.AutoGrowthLogMB;
        var name = EscapeSingleQuote(f.LogicalName);
        sb.AppendLine($"ALTER DATABASE {db} MODIFY FILE (NAME = N'{name}', FILEGROWTH = {growMB}MB);");
    }
    return sb.ToString();
}
```

- [ ] **Step 5：執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GenerateAdjustAutoGrowthSql"`
Expected: PASS

- [ ] **Step 6：Commit**

```bash
git add src/Specurai.Application/Services/IMaintenancePlanSqlGenerator.cs src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
git commit -m "feat(infra): SqlGenerator 新增 GenerateAdjustAutoGrowthSql"
```

---

## Task 11：Infrastructure — SqlGenerator PreExpandDataFile

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`
- Modify: `tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs`

- [ ] **Step 1：寫失敗測試**

```csharp
[Fact]
public void GeneratePreExpandDataFileSql_應湊整GB且只擴資料檔()
{
    var gen = new MaintenancePlanSqlGenerator();
    var config = new MaintenancePlanConfig
    {
        DatabaseName = "DB", BackupPath = @"D:\B\", RestorePath = @"D:\R\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
        PreExpandBufferGB = 5
    };
    // 25600 MB (25 GB) + 5 GB = 30 GB = 30720 MB
    var dataFiles = new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                SizeMB = 25600, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 256,
                VolumeMountPoint = "D", VolumeFreeGB = 100 }
    };

    var sql = gen.GeneratePreExpandDataFileSql(config, dataFiles);

    sql.Should().Contain("ALTER DATABASE [DB]");
    sql.Should().Contain("NAME = N'DB'").And.Contain("SIZE = 30720MB");
    sql.Should().NotContain("_log"); // 不可動 log
}

[Fact]
public void GeneratePreExpandDataFileSql_當目前大小非整GB_應向上湊整再加緩衝()
{
    var gen = new MaintenancePlanSqlGenerator();
    var config = new MaintenancePlanConfig
    {
        DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
        PreExpandBufferGB = 5
    };
    // 25700 MB ≈ 25.1 GB → 湊整 26 GB → +5 GB = 31 GB = 31744 MB
    var files = new List<DatabaseFileInfo>
    {
        new() { LogicalName = "DB", PhysicalName = "x", FileType = DatabaseFileType.Data,
                SizeMB = 25700, FreeMB = 0, IsPercentGrowth = false, GrowthMB = 256,
                VolumeMountPoint = "D", VolumeFreeGB = 100 }
    };

    var sql = gen.GeneratePreExpandDataFileSql(config, files);
    sql.Should().Contain("SIZE = 31744MB");
}
```

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GeneratePreExpandDataFileSql"`
Expected: FAIL

- [ ] **Step 3：實作**

```csharp
public string GeneratePreExpandDataFileSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> dataFiles)
{
    var sb = new StringBuilder();
    var db = QuoteName(config.DatabaseName);
    sb.AppendLine($"-- 預擴 {db} 的資料檔");
    foreach (var f in dataFiles.Where(x => x.FileType == DatabaseFileType.Data))
    {
        // 目前大小向上湊整到 GB，再加緩衝 GB
        var currentGB = (int)Math.Ceiling(f.SizeMB / 1024.0);
        var targetMB = (currentGB + config.PreExpandBufferGB) * 1024;
        var name = EscapeSingleQuote(f.LogicalName);
        sb.AppendLine($"ALTER DATABASE {db} MODIFY FILE (NAME = N'{name}', SIZE = {targetMB}MB);");
    }
    return sb.ToString();
}
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GeneratePreExpandDataFileSql"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
git commit -m "feat(infra): SqlGenerator 新增 GeneratePreExpandDataFileSql"
```

---

## Task 12：Infrastructure — SqlGenerator CreateCheckDbJob

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`
- Modify: `tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs`

- [ ] **Step 1：寫失敗測試**

```csharp
[Fact]
public void GenerateCreateCheckDbJobSql_應包含DBCC_CHECKDB與PHYSICAL_ONLY且每週執行()
{
    var gen = new MaintenancePlanSqlGenerator();
    var config = new MaintenancePlanConfig
    {
        DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = [],
        CheckDbTime = 3, CheckDbDayOfWeek = DayOfWeek.Sunday
    };

    var sql = gen.GenerateCreateCheckDbJobSql(config);

    sql.Should().Contain("DB_CheckDb");
    sql.Should().Contain("DBCC CHECKDB");
    sql.Should().Contain("PHYSICAL_ONLY");
    sql.Should().Contain("@freq_type         = 8");        // weekly
    sql.Should().Contain("@freq_interval     = 1");        // Sunday bitmask
    sql.Should().Contain("@active_start_time = 30000");    // 03:00:00
    sql.Should().Contain("sp_add_job");
    sql.Should().Contain("sp_add_jobschedule");
}

[Fact]
public void GenerateCreateCheckDbJobSql_當action為刪除重建_應先刪除再建立()
{
    var gen = new MaintenancePlanSqlGenerator();
    var config = new MaintenancePlanConfig
    {
        DatabaseName = "DB", BackupPath = @"D:\", RestorePath = @"D:\",
        TestDatabaseName = "DB-test", LoginName = "u", LoginPassword = "p",
        BackupTime = 2, RestoreTime = 3, SelectedSteps = []
    };

    var sql = gen.GenerateCreateCheckDbJobSql(config, "刪除重建");

    sql.Should().Contain("sp_delete_job");
    sql.Should().Contain("sp_add_job");
}
```

- [ ] **Step 2：執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GenerateCreateCheckDbJobSql"`
Expected: FAIL

- [ ] **Step 3：實作**

```csharp
public string GenerateCreateCheckDbJobSql(MaintenancePlanConfig config, string? action = null)
{
    var sb = new StringBuilder();
    var dbName = EscapeSingleQuote(config.DatabaseName);
    var jobName = $"{config.DatabaseName}_CheckDb";
    var escapedJobName = EscapeSingleQuote(jobName);

    // SQL Agent weekly bitmask: Sun=1 Mon=2 Tue=4 Wed=8 Thu=16 Fri=32 Sat=64
    var dayBitmask = config.CheckDbDayOfWeek switch
    {
        DayOfWeek.Sunday => 1,
        DayOfWeek.Monday => 2,
        DayOfWeek.Tuesday => 4,
        DayOfWeek.Wednesday => 8,
        DayOfWeek.Thursday => 16,
        DayOfWeek.Friday => 32,
        DayOfWeek.Saturday => 64,
        _ => 1
    };
    var startTime = config.CheckDbTime * 10000; // HHMMSS

    sb.AppendLine("USE [msdb];");
    sb.AppendLine();

    if (action == "刪除重建")
    {
        sb.AppendLine($"-- 刪除現有的 Job: [{jobName}]");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'{escapedJobName}')");
        sb.AppendLine($"    EXEC dbo.sp_delete_job");
        sb.AppendLine($"        @job_name = N'{escapedJobName}',");
        sb.AppendLine($"        @delete_unused_schedule = 1;");
        sb.AppendLine();
    }

    sb.AppendLine($"-- 建立 Job: [{jobName}]");
    sb.AppendLine($"EXEC dbo.sp_add_job");
    sb.AppendLine($"    @job_name    = N'{escapedJobName}',");
    sb.AppendLine($"    @enabled     = 1,");
    sb.AppendLine($"    @description = N'[Specurai] 每週對 {dbName} 執行 DBCC CHECKDB（PHYSICAL_ONLY）';");
    sb.AppendLine();

    sb.AppendLine($"-- 新增 Step: CheckDB {dbName}");
    sb.AppendLine($"EXEC dbo.sp_add_jobstep");
    sb.AppendLine($"    @job_name       = N'{escapedJobName}',");
    sb.AppendLine($"    @step_name      = N'CheckDB {dbName}',");
    sb.AppendLine($"    @subsystem      = N'TSQL',");
    sb.AppendLine($"    @on_success_action = 1,");
    sb.AppendLine($"    @on_fail_action    = 2,");
    sb.AppendLine($"    @command = N'");
    sb.AppendLine($"BEGIN TRY");
    sb.AppendLine($"    PRINT N''開始：DBCC CHECKDB {dbName} WITH PHYSICAL_ONLY...'';");
    sb.AppendLine($"    DBCC CHECKDB(N''{dbName}'') WITH PHYSICAL_ONLY, NO_INFOMSGS, ALL_ERRORMSGS;");
    sb.AppendLine($"    PRINT N''CheckDB 完成'';");
    sb.AppendLine($"END TRY");
    sb.AppendLine($"BEGIN CATCH");
    sb.AppendLine($"    PRINT N''錯誤: '' + ERROR_MESSAGE();");
    sb.AppendLine($"    THROW;");
    sb.AppendLine($"END CATCH");
    sb.AppendLine($"';");
    sb.AppendLine();

    sb.AppendLine($"-- 建立排程: 每週 {config.CheckDbDayOfWeek} {config.CheckDbTime:D2}:00 執行");
    sb.AppendLine($"EXEC dbo.sp_add_jobschedule");
    sb.AppendLine($"    @job_name          = N'{escapedJobName}',");
    sb.AppendLine($"    @name              = N'{escapedJobName}_Schedule',");
    sb.AppendLine($"    @freq_type         = 8,");
    sb.AppendLine($"    @freq_interval     = {dayBitmask},");
    sb.AppendLine($"    @freq_recurrence_factor = 1,");
    sb.AppendLine($"    @active_start_time = {startTime};");
    sb.AppendLine();

    sb.AppendLine($"-- 指定 Job 在本機伺服器執行");
    sb.AppendLine($"EXEC dbo.sp_add_jobserver");
    sb.AppendLine($"    @job_name = N'{escapedJobName}';");

    return sb.ToString();
}
```

- [ ] **Step 4：執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests --filter "FullyQualifiedName~GenerateCreateCheckDbJobSql"`
Expected: PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
git commit -m "feat(infra): SqlGenerator 新增 GenerateCreateCheckDbJobSql"
```

---

## Task 13：Infrastructure — GenerateStepSql 與 GenerateFullSql 整合

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`

- [ ] **Step 1：在 `GenerateStepSql` 的 switch 加入三個 case**

```csharp
MaintenancePlanStep.AdjustAutoGrowth => string.Empty,    // 需要 file 清單，由 service 直接呼叫
MaintenancePlanStep.PreExpandDataFile => string.Empty,   // 同上
MaintenancePlanStep.CreateCheckDbJob => GenerateCreateCheckDbJobSql(config, action),
```

- [ ] **Step 2：在 `GenerateFullSql` 的 backupStep 區段之後追加 CheckDb 區段**

```csharp
// 步驟：CheckDB 排程
var checkDbStep = activeResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateCheckDbJob);
if (checkDbStep is not null)
{
    sb.AppendLine($"PRINT N'===== 建立完整性檢查排程 (開始) =====';");
    sb.AppendLine("BEGIN TRY");
    sb.AppendLine(GenerateStepSql(checkDbStep.Step, config, checkDbStep.SelectedAction));
    sb.AppendLine($"    PRINT N'===== 建立完整性檢查排程 (完成) =====';");
    sb.AppendLine("END TRY");
    sb.AppendLine("BEGIN CATCH");
    sb.AppendLine("    PRINT N'##### 建立完整性檢查排程發生錯誤 #####';");
    sb.AppendLine("    PRINT ERROR_MESSAGE();");
    sb.AppendLine("END CATCH;");
    sb.AppendLine("GO");
    sb.AppendLine();
}
```

> 注意：`GenerateFullSql` 中 `AdjustAutoGrowth` 與 `PreExpandDataFile` 的預覽 SQL 由 `MaintenancePlanService.GeneratePreviewSqlAsync` 補齊（Task 14）；本層先回空字串避免破壞既有架構。

- [ ] **Step 3：建置**

Run: `dotnet build`
Expected: SUCCESS

- [ ] **Step 4：Commit**

```bash
git add src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs
git commit -m "feat(infra): GenerateStepSql/GenerateFullSql 接通 CheckDB 步驟"
```

---

## Task 14：Application — ExecutePlanAsync 整合三步驟 + 預覽

**Files:**
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`

> 因為 AutoGrowth 與 PreExpand 的 SQL 產生需要 `DatabaseFileInfo` 清單，service 端要自行查檔案、呼叫專屬產生器、執行。CheckDb 直接走既有 Job 模式。

- [ ] **Step 1：在 `ExecutePlanAsync` 的「交易群組」之前加入新區段**

於既有 `// 獨立步驟：更新相容性層級` 區段之後、`// 交易群組：資料庫設定步驟` 之前，插入：

```csharp
// 檔案調校：autogrowth + 預擴（兩者都用 ALTER DATABASE，不能放在交易內）
var autoGrowthStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.AdjustAutoGrowth && r.SelectedAction != "跳過");
var preExpandStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.PreExpandDataFile && r.SelectedAction != "跳過");

if (autoGrowthStep is not null || preExpandStep is not null)
{
    var files = await _dbInfoRepo.GetDatabaseFilesAsync(config.DatabaseName, ct);

    if (autoGrowthStep is not null)
    {
        progress?.Report("正在調整檔案自動成長設定...");
        var sql = _sqlGenerator.GenerateAdjustAutoGrowthSql(config, files);
        await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
        progress?.Report("自動成長設定調整完成。");
    }

    ct.ThrowIfCancellationRequested();

    if (preExpandStep is not null)
    {
        progress?.Report("正在預擴資料檔（可能需數分鐘）...");
        var dataFiles = files.Where(f => f.FileType == DatabaseFileType.Data).ToList();
        var sql = _sqlGenerator.GeneratePreExpandDataFileSql(config, dataFiles);
        await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
        progress?.Report("預擴資料檔完成。");
    }

    ct.ThrowIfCancellationRequested();
}
```

- [ ] **Step 2：在 `// 交易群組 2：備份 Job` 之後追加 CheckDb 區段**

```csharp
ct.ThrowIfCancellationRequested();

// 交易群組：CheckDB Job
var checkDbStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateCheckDbJob && r.SelectedAction != "跳過");
if (checkDbStep != null)
{
    progress?.Report("正在建立完整性檢查排程...");
    var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateCheckDbJob, config, checkDbStep.SelectedAction);
    await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
    progress?.Report("完整性檢查排程建立完成。");
}
```

- [ ] **Step 3：擴充 `GeneratePreviewSqlAsync` 補齊 AutoGrowth/PreExpand 區段**

將 `GeneratePreviewSqlAsync` 改為 async 並合併產生器輸出：

```csharp
public async Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults)
{
    var baseSql = _sqlGenerator.GenerateFullSql(config, checkResults);

    var autoGrowthActive = checkResults.Any(r => r.Step == MaintenancePlanStep.AdjustAutoGrowth && r.SelectedAction != "跳過");
    var preExpandActive = checkResults.Any(r => r.Step == MaintenancePlanStep.PreExpandDataFile && r.SelectedAction != "跳過");

    if (!autoGrowthActive && !preExpandActive)
        return baseSql;

    var files = await _dbInfoRepo.GetDatabaseFilesAsync(config.DatabaseName);
    var sb = new System.Text.StringBuilder();

    if (autoGrowthActive)
    {
        sb.AppendLine("PRINT N'===== 調整 autogrowth (開始) =====';");
        sb.AppendLine("BEGIN TRY");
        sb.AppendLine(_sqlGenerator.GenerateAdjustAutoGrowthSql(config, files));
        sb.AppendLine("PRINT N'===== 調整 autogrowth (完成) =====';");
        sb.AppendLine("END TRY BEGIN CATCH PRINT ERROR_MESSAGE(); END CATCH;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    if (preExpandActive)
    {
        var dataFiles = files.Where(f => f.FileType == DatabaseFileType.Data).ToList();
        sb.AppendLine("PRINT N'===== 預擴資料檔 (開始) =====';");
        sb.AppendLine("BEGIN TRY");
        sb.AppendLine(_sqlGenerator.GeneratePreExpandDataFileSql(config, dataFiles));
        sb.AppendLine("PRINT N'===== 預擴資料檔 (完成) =====';");
        sb.AppendLine("END TRY BEGIN CATCH PRINT ERROR_MESSAGE(); END CATCH;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    return sb.ToString() + baseSql;
}
```

> 介面 `IMaintenancePlanService.GeneratePreviewSqlAsync` 已是 `Task<string>`，無需改簽章。

- [ ] **Step 4：執行 Application 全部測試確認回歸沒破壞**

Run: `dotnet test tests/Specurai.Application.Tests`
Expected: 全部 PASS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Application/Services/MaintenancePlanService.cs
git commit -m "feat(application): ExecutePlanAsync 整合 autogrowth/預擴/CheckDB 三新步驟"
```

---

## Task 15：Desktop — ViewModel 三組欄位與選取邏輯

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs`

- [ ] **Step 1：在 #region 精靈 - 步驟2 選擇步驟 內補欄位**

於 `_isCreateRestoreJobSelected` 之前/之後加入：

```csharp
[ObservableProperty]
private bool _isAdjustAutoGrowthSelected = true;

[ObservableProperty]
private bool _isPreExpandDataFileSelected;

[ObservableProperty]
private bool _isCreateCheckDbJobSelected = true;
```

於既有 status 欄位區塊（`_backupJobStatus` 等）之後加入：

```csharp
[ObservableProperty]
private string _adjustAutoGrowthStatus = string.Empty;

[ObservableProperty]
private string _preExpandDataFileStatus = string.Empty;

[ObservableProperty]
private string _checkDbJobStatus = string.Empty;
```

- [ ] **Step 2：在 `RunStep2ChecksAsync` 的 switch 中加入三個 case**

```csharp
case MaintenancePlanStep.AdjustAutoGrowth:
    AdjustAutoGrowthStatus = r.CurrentStatus;
    IsAdjustAutoGrowthSelected = !r.AlreadyExists;
    break;
case MaintenancePlanStep.PreExpandDataFile:
    PreExpandDataFileStatus = r.CurrentStatus;
    IsPreExpandDataFileSelected = !r.AlreadyExists;
    break;
case MaintenancePlanStep.CreateCheckDbJob:
    CheckDbJobStatus = r.CurrentStatus;
    IsCreateCheckDbJobSelected = !r.AlreadyExists;
    break;
```

- [ ] **Step 3：在 `GetSelectedSteps` 中加入三個 if**

```csharp
if (IsAdjustAutoGrowthSelected) steps.Add(MaintenancePlanStep.AdjustAutoGrowth);
if (IsPreExpandDataFileSelected) steps.Add(MaintenancePlanStep.PreExpandDataFile);
// CreateBackupJob 之後
if (IsCreateCheckDbJobSelected) steps.Add(MaintenancePlanStep.CreateCheckDbJob);
```

> 順序需與 enum 一致以利 UI 顯示順序：SetCompatibilityLevel, SetRecoveryModel, **AdjustAutoGrowth, PreExpandDataFile**, RenameLogicalFiles, CreateLoginAndUser, AddToDbOwner, CreateBackupJob, **CreateCheckDbJob**, CreateRestoreJob。

- [ ] **Step 4：建置**

Run: `dotnet build`
Expected: SUCCESS

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs
git commit -m "feat(desktop): ViewModel 加入三新步驟的選取欄位與檢查整合"
```

---

## Task 16：Desktop — View XAML 三組 CheckBox

**Files:**
- Modify: `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml`

- [ ] **Step 1：找到既有的步驟 CheckBox 群組**

用 Grep 定位：`IsSetRecoveryModelSelected`、`IsCreateBackupJobSelected`，照樣板複製三組。

- [ ] **Step 2：在「設定 Recovery Model」之後、「重新命名邏輯檔名」之前插入兩組**

```xml
<Grid ColumnDefinitions="*,*">
    <CheckBox Grid.Column="0" IsChecked="{Binding IsAdjustAutoGrowthSelected}"
              Content="調整檔案自動成長設定"/>
    <TextBlock Grid.Column="1" Text="{Binding AdjustAutoGrowthStatus}"
               VerticalAlignment="Center" Opacity="0.6" FontSize="12"/>
</Grid>
<Grid ColumnDefinitions="*,*">
    <CheckBox Grid.Column="0" IsChecked="{Binding IsPreExpandDataFileSelected}"
              Content="預擴資料檔（保留成長緩衝）"/>
    <TextBlock Grid.Column="1" Text="{Binding PreExpandDataFileStatus}"
               VerticalAlignment="Center" Opacity="0.6" FontSize="12"/>
</Grid>
```

- [ ] **Step 3：在「建立每日全備份排程」之後、「建立每日還原排程」之前插入一組**

```xml
<Grid ColumnDefinitions="*,*">
    <CheckBox Grid.Column="0" IsChecked="{Binding IsCreateCheckDbJobSelected}"
              Content="建立每週完整性檢查排程"/>
    <TextBlock Grid.Column="1" Text="{Binding CheckDbJobStatus}"
               VerticalAlignment="Center" Opacity="0.6" FontSize="12"/>
</Grid>
```

- [ ] **Step 4：建置 + 啟動桌面 App 手動 smoke test**

Run: `dotnet build`
Expected: SUCCESS

Run: `dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: 開啟維護計劃 → 精靈 Step 2 看到三個新項目，每項右側顯示檢查狀態。

- [ ] **Step 5：Commit**

```bash
git add src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml
git commit -m "feat(desktop): MaintenancePlan View 新增三步驟 CheckBox"
```

---

## Task 17：全測試 + 程式碼審查

- [ ] **Step 1：執行所有測試**

Run: `dotnet test`
Expected: 全部 PASS（既有 604+ 測試 + 本次新增 ~10 筆）

- [ ] **Step 2：透過 superpowers:requesting-code-review 進行審查**

依 CLAUDE.md 法規:`<law>程式碼審查：每次完成功能實作、Bug 修復或重構後，必須使用 superpowers:requesting-code-review 技能進行程式碼審查，再回報完成。</law>`

針對本批變更請求審查，重點：
- Clean Architecture 分層相依性是否正確
- TDD 測試完整度
- SQL 注入風險（檔名來自系統，但仍應走 `EscapeSingleQuote` / `QuoteName`）

- [ ] **Step 3：針對審查回饋修正**

依審查意見調整。每修一項就 commit 一次。

---

## Self-Review 檢查

- ✅ Spec 三個步驟皆有對應 Task：AutoGrowth (T7/T10)、PreExpand (T8/T11)、CheckDb (T9/T12)
- ✅ Domain Entity / enum / config / interface 變更齊備
- ✅ Service 與 SqlGenerator 的整合在 T13/T14 接通
- ✅ UI 在 T15/T16 完成
- ✅ 無 placeholder（"TODO"/"待補"/"類似 Task N"）
- ✅ 型別一致性：`DatabaseFileInfo`、`DatabaseFileType.Data/Log`、`GrowthMB`、`FreePercent` 在所有 task 命名一致
- ✅ 測試命名遵循專案慣例：`[Method]_[Condition]_[Expected]` 繁體中文
