# 資料庫維護計劃功能實作計劃

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 TableSpec 中新增資料庫維護計劃功能，包含建立精靈和 SQL Agent Job 管理面板。

**Architecture:** Domain 層定義實體和 Repository 介面，Application 層提供服務介面和實作，Infrastructure 層實作 SQL 產生器和 Repository（查詢 msdb），Desktop 層實作精靈和管理視窗。

**Tech Stack:** .NET 8, Avalonia 11.x, CommunityToolkit.Mvvm, Dapper, xUnit, NSubstitute, FluentAssertions

**Spec:** `docs/superpowers/specs/2026-03-18-maintenance-plan-design.md`

---

### Task 1: Domain 層 — 列舉與實體

**Files:**
- Create: `src/TableSpec.Domain/Enums/MaintenancePlanStep.cs`
- Create: `src/TableSpec.Domain/Entities/AgentJobInfo.cs`
- Create: `src/TableSpec.Domain/Entities/AgentJobHistory.cs`
- Create: `src/TableSpec.Domain/Entities/MaintenancePlanConfig.cs`
- Test: `tests/TableSpec.Domain.Tests/Entities/AgentJobInfoTests.cs`
- Test: `tests/TableSpec.Domain.Tests/Entities/MaintenancePlanConfigTests.cs`

> **注意**：`StepCheckResult` 含可變屬性 `SelectedAction`，放在 Application 層（Task 3）。

- [ ] **Step 1: 撰寫 AgentJobInfo 測試（TDD：先寫測試）**

```csharp
// tests/TableSpec.Domain.Tests/Entities/AgentJobInfoTests.cs
// （測試內容同後方 Step 7，移至此處先寫）
```

- [ ] **Step 2: 撰寫 MaintenancePlanConfig 測試（TDD：先寫測試）**

```csharp
// tests/TableSpec.Domain.Tests/Entities/MaintenancePlanConfigTests.cs
// （測試內容同後方 Step 8，移至此處先寫）
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "AgentJobInfoTests|MaintenancePlanConfigTests" -v minimal`
Expected: FAIL (classes not found)

- [ ] **Step 4: 建立 MaintenancePlanStep 列舉**

```csharp
// src/TableSpec.Domain/Enums/MaintenancePlanStep.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 維護計劃步驟
/// </summary>
public enum MaintenancePlanStep
{
    /// <summary>設定 Recovery Model 為 SIMPLE</summary>
    SetRecoveryModel,
    /// <summary>重新命名邏輯檔名</summary>
    RenameLogicalFiles,
    /// <summary>建立登入帳號與使用者</summary>
    CreateLoginAndUser,
    /// <summary>將使用者加入 db_owner</summary>
    AddToDbOwner,
    /// <summary>建立每日全備份排程</summary>
    CreateBackupJob,
    /// <summary>建立每日還原排程</summary>
    CreateRestoreJob
}
```

- [ ] **Step 5b: （StepCheckResult 移至 Task 3 Application 層，此處不建立）**

- [ ] **Step 3: 建立 MaintenancePlanConfig 實體**

```csharp
// src/TableSpec.Domain/Entities/MaintenancePlanConfig.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// 維護計劃設定參數
/// </summary>
public class MaintenancePlanConfig
{
    /// <summary>資料庫名稱</summary>
    public required string DatabaseName { get; init; }

    /// <summary>備份路徑</summary>
    public required string BackupPath { get; init; }

    /// <summary>還原路徑</summary>
    public required string RestorePath { get; init; }

    /// <summary>測試資料庫名稱</summary>
    public required string TestDatabaseName { get; init; }

    /// <summary>登入帳號名稱</summary>
    public required string LoginName { get; init; }

    /// <summary>登入密碼</summary>
    public required string LoginPassword { get; init; }

    /// <summary>備份排程時間（HHMMSS 格式）</summary>
    public required int BackupTime { get; init; }

    /// <summary>還原排程時間（HHMMSS 格式）</summary>
    public required int RestoreTime { get; init; }

    /// <summary>選擇的步驟</summary>
    public required IReadOnlyList<MaintenancePlanStep> SelectedSteps { get; init; }

    /// <summary>備份保留天數</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>備份路徑是否以路徑分隔符結尾</summary>
    public bool IsBackupPathValid => !string.IsNullOrWhiteSpace(BackupPath) && (BackupPath.EndsWith('/') || BackupPath.EndsWith('\\'));

    /// <summary>還原路徑是否以路徑分隔符結尾</summary>
    public bool IsRestorePathValid => !string.IsNullOrWhiteSpace(RestorePath) && (RestorePath.EndsWith('/') || RestorePath.EndsWith('\\'));
}
```

- [ ] **Step 4: 建立 AgentJobInfo 實體**

```csharp
// src/TableSpec.Domain/Entities/AgentJobInfo.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// SQL Agent Job 資訊
/// </summary>
public class AgentJobInfo
{
    /// <summary>Job 唯一識別碼</summary>
    public required Guid JobId { get; init; }

    /// <summary>Job 名稱</summary>
    public required string Name { get; init; }

    /// <summary>Job 說明</summary>
    public string? Description { get; init; }

    /// <summary>是否啟用</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>上次執行時間</summary>
    public DateTime? LastRunDate { get; init; }

    /// <summary>上次執行結果（0=失敗, 1=成功, 3=取消, 5=未知）</summary>
    public int? LastRunOutcome { get; init; }

    /// <summary>下次排程時間</summary>
    public DateTime? NextRunDate { get; init; }

    /// <summary>排程時間（HHMMSS 格式）</summary>
    public int? ScheduleTime { get; init; }

    /// <summary>排程頻率類型</summary>
    public int? ScheduleFreqType { get; init; }

    /// <summary>是否由 TableSpec 建立</summary>
    public bool IsTableSpecJob => Description?.Contains("[TableSpec]") == true;

    /// <summary>上次執行結果文字</summary>
    public string LastRunOutcomeText => LastRunOutcome switch
    {
        0 => "失敗",
        1 => "成功",
        3 => "取消",
        5 => "未知",
        _ => "無記錄"
    };

    /// <summary>狀態文字</summary>
    public string StatusText => IsEnabled ? "啟用" : "停用";
}
```

- [ ] **Step 5: 建立 AgentJobHistory 實體**

```csharp
// src/TableSpec.Domain/Entities/AgentJobHistory.cs
namespace TableSpec.Domain.Entities;

/// <summary>
/// SQL Agent Job 執行歷史
/// </summary>
public class AgentJobHistory
{
    /// <summary>Job 識別碼</summary>
    public required Guid JobId { get; init; }

    /// <summary>步驟名稱</summary>
    public required string StepName { get; init; }

    /// <summary>執行時間</summary>
    public required DateTime RunDate { get; init; }

    /// <summary>執行結果（0=失敗, 1=成功）</summary>
    public required int RunStatus { get; init; }

    /// <summary>執行時長（秒）</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>訊息</summary>
    public string? Message { get; init; }

    /// <summary>執行結果文字</summary>
    public string RunStatusText => RunStatus switch
    {
        0 => "失敗",
        1 => "成功",
        _ => "未知"
    };
}
```

- [ ] **Step 6: 撰寫 AgentJobInfo 測試**

```csharp
// tests/TableSpec.Domain.Tests/Entities/AgentJobInfoTests.cs
using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

public class AgentJobInfoTests
{
    [Fact]
    public void IsTableSpecJob_Description包含TableSpec標記_應回傳True()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            Description = "[TableSpec] 每日全備份",
            IsEnabled = true
        };
        job.IsTableSpecJob.Should().BeTrue();
    }

    [Fact]
    public void IsTableSpecJob_Description不包含標記_應回傳False()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            Description = "一般的 Job",
            IsEnabled = true
        };
        job.IsTableSpecJob.Should().BeFalse();
    }

    [Fact]
    public void IsTableSpecJob_Description為Null_應回傳False()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            Description = null,
            IsEnabled = true
        };
        job.IsTableSpecJob.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "失敗")]
    [InlineData(1, "成功")]
    [InlineData(3, "取消")]
    [InlineData(5, "未知")]
    [InlineData(null, "無記錄")]
    public void LastRunOutcomeText_應回傳正確文字(int? outcome, string expected)
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            IsEnabled = true,
            LastRunOutcome = outcome
        };
        job.LastRunOutcomeText.Should().Be(expected);
    }

    [Fact]
    public void StatusText_啟用時_應回傳啟用()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            IsEnabled = true
        };
        job.StatusText.Should().Be("啟用");
    }

    [Fact]
    public void StatusText_停用時_應回傳停用()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "TestJob",
            IsEnabled = false
        };
        job.StatusText.Should().Be("停用");
    }
}
```

- [ ] **Step 7: 撰寫 MaintenancePlanConfig 測試**

```csharp
// tests/TableSpec.Domain.Tests/Entities/MaintenancePlanConfigTests.cs
using FluentAssertions;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities;

public class MaintenancePlanConfigTests
{
    [Theory]
    [InlineData("D:\\SQLBackup\\", true)]
    [InlineData("/var/opt/mssql/dbbackup/", true)]
    [InlineData("D:\\SQLBackup", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void IsBackupPathValid_應根據路徑結尾判斷(string path, bool expected)
    {
        var config = CreateConfig(backupPath: path);
        config.IsBackupPathValid.Should().Be(expected);
    }

    [Theory]
    [InlineData("D:\\sql_data\\", true)]
    [InlineData("/var/opt/mssql/data/", true)]
    [InlineData("D:\\sql_data", false)]
    public void IsRestorePathValid_應根據路徑結尾判斷(string path, bool expected)
    {
        var config = CreateConfig(restorePath: path);
        config.IsRestorePathValid.Should().Be(expected);
    }

    [Fact]
    public void RetentionDays_預設值應為7()
    {
        var config = CreateConfig();
        config.RetentionDays.Should().Be(7);
    }

    private static MaintenancePlanConfig CreateConfig(
        string backupPath = "D:\\Backup\\",
        string restorePath = "D:\\Data\\")
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = "TestDB",
            BackupPath = backupPath,
            RestorePath = restorePath,
            TestDatabaseName = "TestDB-test",
            LoginName = "mis",
            LoginPassword = "pass",
            BackupTime = 020000,
            RestoreTime = 030000,
            SelectedSteps = [MaintenancePlanStep.SetRecoveryModel]
        };
    }
}
```

- [ ] **Step 8: 執行測試確認通過**

Run: `dotnet test tests/TableSpec.Domain.Tests --filter "AgentJobInfoTests|MaintenancePlanConfigTests" -v minimal`
Expected: All tests PASS

- [ ] **Step 9: Commit**

```bash
git add src/TableSpec.Domain/Enums/MaintenancePlanStep.cs src/TableSpec.Domain/Entities/AgentJobInfo.cs src/TableSpec.Domain/Entities/AgentJobHistory.cs src/TableSpec.Domain/Entities/MaintenancePlanConfig.cs src/TableSpec.Domain/Entities/StepCheckResult.cs tests/TableSpec.Domain.Tests/Entities/AgentJobInfoTests.cs tests/TableSpec.Domain.Tests/Entities/MaintenancePlanConfigTests.cs
git commit -m "新增維護計劃 Domain 層實體與列舉"
```

---

### Task 2: Domain 層 — Repository 介面

**Files:**
- Create: `src/TableSpec.Domain/Interfaces/IAgentJobRepository.cs`
- Create: `src/TableSpec.Domain/Interfaces/IDatabaseInfoRepository.cs`

- [ ] **Step 1: 建立 IAgentJobRepository**

```csharp
// src/TableSpec.Domain/Interfaces/IAgentJobRepository.cs
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// SQL Agent Job 資料存取介面
/// </summary>
public interface IAgentJobRepository
{
    /// <summary>取得所有由 TableSpec 建立的 Job</summary>
    Task<IReadOnlyList<AgentJobInfo>> GetTableSpecJobsAsync(CancellationToken ct = default);

    /// <summary>取得指定 Job 的執行歷史</summary>
    Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default);

    /// <summary>啟用或停用 Job</summary>
    Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default);

    /// <summary>立即執行 Job</summary>
    Task StartJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>刪除 Job</summary>
    Task DeleteJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>修改 Job 排程時間</summary>
    Task UpdateJobScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default);

    /// <summary>檢查 SQL Agent 服務是否執行中</summary>
    Task<bool> IsAgentRunningAsync(CancellationToken ct = default);

    /// <summary>檢查是否有 msdb 操作權限</summary>
    Task<bool> HasAgentPermissionAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: 建立 IDatabaseInfoRepository**

```csharp
// src/TableSpec.Domain/Interfaces/IDatabaseInfoRepository.cs
namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 資料庫資訊查詢介面（用於維護計劃前置檢查）
/// </summary>
public interface IDatabaseInfoRepository
{
    /// <summary>取得伺服器上所有資料庫名稱</summary>
    Task<IReadOnlyList<string>> GetDatabaseNamesAsync(CancellationToken ct = default);

    /// <summary>取得資料庫的 Recovery Model</summary>
    Task<string> GetRecoveryModelAsync(string databaseName, CancellationToken ct = default);

    /// <summary>取得資料庫邏輯檔名清單</summary>
    Task<IReadOnlyList<(string LogicalName, string PhysicalName)>> GetLogicalFileNamesAsync(string databaseName, CancellationToken ct = default);

    /// <summary>檢查登入帳號是否存在</summary>
    Task<bool> LoginExistsAsync(string loginName, CancellationToken ct = default);

    /// <summary>檢查資料庫使用者是否存在</summary>
    Task<bool> DatabaseUserExistsAsync(string databaseName, string userName, CancellationToken ct = default);

    /// <summary>檢查使用者是否為 db_owner 成員</summary>
    Task<bool> IsDbOwnerMemberAsync(string databaseName, string userName, CancellationToken ct = default);

    /// <summary>檢查指定名稱的 Job 是否存在</summary>
    Task<bool> AgentJobExistsAsync(string jobName, CancellationToken ct = default);

    /// <summary>檢查是否為 Azure SQL Database</summary>
    Task<bool> IsAzureSqlDatabaseAsync(CancellationToken ct = default);

    /// <summary>在交易中執行 SQL</summary>
    Task ExecuteSqlWithTransactionAsync(string sql, CancellationToken ct = default);

    /// <summary>執行 SQL（不含交易）</summary>
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);
}
```

- [ ] **Step 3: Commit**

```bash
git add src/TableSpec.Domain/Interfaces/IAgentJobRepository.cs src/TableSpec.Domain/Interfaces/IDatabaseInfoRepository.cs
git commit -m "新增維護計劃 Repository 介面"
```

---

### Task 3: Application 層 — 服務介面與實作

**Files:**
- Create: `src/TableSpec.Application/Services/IMaintenancePlanService.cs`
- Create: `src/TableSpec.Application/Services/MaintenancePlanService.cs`
- Create: `src/TableSpec.Application/Services/IAgentJobService.cs`
- Create: `src/TableSpec.Application/Services/AgentJobService.cs`
- Test: `tests/TableSpec.Application.Tests/Services/MaintenancePlanServiceTests.cs`
- Test: `tests/TableSpec.Application.Tests/Services/AgentJobServiceTests.cs`

- [ ] **Step 0: 建立 StepCheckResult（Application 層 DTO）**

```csharp
// src/TableSpec.Application/Models/StepCheckResult.cs
using TableSpec.Domain.Enums;

namespace TableSpec.Application.Models;

/// <summary>
/// 步驟前置檢查結果（含可變的使用者選擇）
/// </summary>
public class StepCheckResult
{
    /// <summary>步驟</summary>
    public required MaintenancePlanStep Step { get; init; }

    /// <summary>是否已存在/已設定</summary>
    public required bool AlreadyExists { get; init; }

    /// <summary>目前狀態描述</summary>
    public required string CurrentStatus { get; init; }

    /// <summary>可用的處理選項</summary>
    public required IReadOnlyList<string> AvailableActions { get; init; }

    /// <summary>使用者選擇的處理方式（null 表示尚未選擇）</summary>
    public string? SelectedAction { get; set; }
}
```

- [ ] **Step 1: 建立 IMaintenancePlanService 介面**

```csharp
// src/TableSpec.Application/Services/IMaintenancePlanService.cs
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Application.Services;

/// <summary>
/// 維護計劃服務介面
/// </summary>
public interface IMaintenancePlanService
{
    /// <summary>執行前置檢查</summary>
    Task<IReadOnlyList<StepCheckResult>> CheckStepsAsync(MaintenancePlanConfig config, CancellationToken ct = default);

    /// <summary>執行維護計劃</summary>
    Task ExecutePlanAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>產生預覽 SQL</summary>
    Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, CancellationToken ct = default);

    /// <summary>檢查前置條件（SQL Agent 狀態、權限）</summary>
    Task<(bool IsReady, string? ErrorMessage)> CheckPrerequisitesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: 建立 IMaintenancePlanSqlGenerator 介面**

```csharp
// src/TableSpec.Application/Services/IMaintenancePlanSqlGenerator.cs
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Application.Services;

/// <summary>
/// 維護計劃 SQL 產生器介面
/// </summary>
public interface IMaintenancePlanSqlGenerator
{
    /// <summary>產生指定步驟的 SQL</summary>
    string GenerateStepSql(MaintenancePlanStep step, MaintenancePlanConfig config, string? action = null);

    /// <summary>產生完整維護計劃 SQL（含交易）</summary>
    string GenerateFullSql(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults);
}
```

- [ ] **Step 3: 建立 IAgentJobService 介面**

```csharp
// src/TableSpec.Application/Services/IAgentJobService.cs
using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// SQL Agent Job 管理服務介面
/// </summary>
public interface IAgentJobService
{
    /// <summary>取得所有由 TableSpec 建立的 Job</summary>
    Task<IReadOnlyList<AgentJobInfo>> GetJobsAsync(CancellationToken ct = default);

    /// <summary>啟用或停用 Job</summary>
    Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default);

    /// <summary>立即執行 Job</summary>
    Task StartJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>刪除 Job</summary>
    Task DeleteJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>修改排程</summary>
    Task UpdateScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default);

    /// <summary>取得 Job 執行歷史</summary>
    Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, CancellationToken ct = default);
}
```

- [ ] **Step 4: 實作 AgentJobService**

```csharp
// src/TableSpec.Application/Services/AgentJobService.cs
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// SQL Agent Job 管理服務實作
/// </summary>
public class AgentJobService : IAgentJobService
{
    private readonly IAgentJobRepository _repository;

    public AgentJobService(IAgentJobRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<AgentJobInfo>> GetJobsAsync(CancellationToken ct = default)
        => _repository.GetTableSpecJobsAsync(ct);

    public Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default)
        => _repository.SetJobEnabledAsync(jobId, enabled, ct);

    public Task StartJobAsync(Guid jobId, CancellationToken ct = default)
        => _repository.StartJobAsync(jobId, ct);

    public Task DeleteJobAsync(Guid jobId, CancellationToken ct = default)
        => _repository.DeleteJobAsync(jobId, ct);

    public Task UpdateScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default)
        => _repository.UpdateJobScheduleAsync(jobId, freqType, freqInterval, activeStartTime, ct);

    public Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, CancellationToken ct = default)
        => _repository.GetJobHistoryAsync(jobId, ct: ct);
}
```

- [ ] **Step 5: 實作 MaintenancePlanService**

```csharp
// src/TableSpec.Application/Services/MaintenancePlanService.cs
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 維護計劃服務實作
/// </summary>
public class MaintenancePlanService : IMaintenancePlanService
{
    private readonly IDatabaseInfoRepository _dbInfoRepository;
    private readonly IAgentJobRepository _agentJobRepository;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;

    public MaintenancePlanService(
        IDatabaseInfoRepository dbInfoRepository,
        IAgentJobRepository agentJobRepository,
        IMaintenancePlanSqlGenerator sqlGenerator)
    {
        _dbInfoRepository = dbInfoRepository;
        _agentJobRepository = agentJobRepository;
        _sqlGenerator = sqlGenerator;
    }

    // 注意：StepCheckResult 從 TableSpec.Application.Models 引用

    public async Task<(bool IsReady, string? ErrorMessage)> CheckPrerequisitesAsync(CancellationToken ct = default)
    {
        // 檢查是否為 Azure SQL Database（EngineEdition = 5）
        var isAzure = await _dbInfoRepository.IsAzureSqlDatabaseAsync(ct);
        if (isAzure)
            return (false, "Azure SQL Database 不支援 SQL Server Agent，無法使用維護計劃功能。");

        var isRunning = await _agentJobRepository.IsAgentRunningAsync(ct);
        if (!isRunning)
            return (false, "SQL Server Agent 服務未啟動，無法管理維護計劃。");

        var hasPermission = await _agentJobRepository.HasAgentPermissionAsync(ct);
        if (!hasPermission)
            return (false, "目前連線帳號沒有 msdb 的操作權限，無法管理 SQL Agent Job。");

        return (true, null);
    }

    public async Task<IReadOnlyList<StepCheckResult>> CheckStepsAsync(MaintenancePlanConfig config, CancellationToken ct = default)
    {
        var results = new List<StepCheckResult>();

        foreach (var step in config.SelectedSteps)
        {
            var result = step switch
            {
                MaintenancePlanStep.SetRecoveryModel => await CheckRecoveryModelAsync(config, ct),
                MaintenancePlanStep.RenameLogicalFiles => await CheckLogicalFilesAsync(config, ct),
                MaintenancePlanStep.CreateLoginAndUser => await CheckLoginAsync(config, ct),
                MaintenancePlanStep.AddToDbOwner => await CheckDbOwnerAsync(config, ct),
                MaintenancePlanStep.CreateBackupJob => await CheckJobAsync(config, $"{config.DatabaseName}_FullBackup", MaintenancePlanStep.CreateBackupJob, ct),
                MaintenancePlanStep.CreateRestoreJob => await CheckJobAsync(config, $"{config.DatabaseName}_FullRestore", MaintenancePlanStep.CreateRestoreJob, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(step))
            };
            results.Add(result);
        }

        return results;
    }

    public Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, CancellationToken ct = default)
    {
        var sql = _sqlGenerator.GenerateFullSql(config, checkResults);
        return Task.FromResult(sql);
    }

    public async Task ExecutePlanAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // 交易一：設定類步驟 (1-4)
        var configSteps = checkResults.Where(r =>
            r.Step is MaintenancePlanStep.SetRecoveryModel or
                MaintenancePlanStep.RenameLogicalFiles or
                MaintenancePlanStep.CreateLoginAndUser or
                MaintenancePlanStep.AddToDbOwner &&
            r.SelectedAction != "跳過").ToList();

        if (configSteps.Count > 0)
        {
            progress?.Report("交易一：基本設定");
            var sql = string.Join("\n", configSteps.Select(r => _sqlGenerator.GenerateStepSql(r.Step, config, r.SelectedAction)));
            await _dbInfoRepository.ExecuteSqlWithTransactionAsync(sql, ct);
            foreach (var step in configSteps)
                progress?.Report($"  [完成] {step.Step}: {step.CurrentStatus}");
        }

        // 交易二：備份 Job
        var backupStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateBackupJob && r.SelectedAction != "跳過");
        if (backupStep != null)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("交易二：備份排程");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, backupStep.SelectedAction);
            await _dbInfoRepository.ExecuteSqlAsync(sql, ct);
            progress?.Report($"  [完成] 備份 Job 已建立");
        }

        // 交易三：還原 Job
        var restoreStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob && r.SelectedAction != "跳過");
        if (restoreStep != null)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("交易三：還原排程");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, config, restoreStep.SelectedAction);
            await _dbInfoRepository.ExecuteSqlAsync(sql, ct);
            progress?.Report($"  [完成] 還原 Job 已建立");
        }
    }

    private async Task<StepCheckResult> CheckRecoveryModelAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var model = await _dbInfoRepository.GetRecoveryModelAsync(config.DatabaseName, ct);
        var isSimple = model.Equals("SIMPLE", StringComparison.OrdinalIgnoreCase);
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.SetRecoveryModel,
            AlreadyExists = isSimple,
            CurrentStatus = isSimple ? "已設定為 SIMPLE" : $"目前為 {model}",
            AvailableActions = isSimple ? ["跳過"] : ["執行"]
        };
    }

    private async Task<StepCheckResult> CheckLogicalFilesAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var files = await _dbInfoRepository.GetLogicalFileNamesAsync(config.DatabaseName, ct);
        var hasOldNames = files.Any(f => f.LogicalName.StartsWith("shltw_", StringComparison.OrdinalIgnoreCase));
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.RenameLogicalFiles,
            AlreadyExists = !hasOldNames,
            CurrentStatus = hasOldNames ? "發現舊邏輯檔名需重新命名" : "邏輯檔名已正確",
            AvailableActions = hasOldNames ? ["執行"] : ["跳過"]
        };
    }

    private async Task<StepCheckResult> CheckLoginAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var loginExists = await _dbInfoRepository.LoginExistsAsync(config.LoginName, ct);
        var userExists = await _dbInfoRepository.DatabaseUserExistsAsync(config.DatabaseName, config.LoginName, ct);
        var bothExist = loginExists && userExists;
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.CreateLoginAndUser,
            AlreadyExists = bothExist,
            CurrentStatus = bothExist
                ? $"登入帳號和使用者 [{config.LoginName}] 皆已存在"
                : loginExists ? $"登入帳號已存在，但資料庫使用者不存在"
                : $"登入帳號 [{config.LoginName}] 不存在",
            AvailableActions = bothExist ? ["跳過", "刪除重建"] : ["執行"]
        };
    }

    private async Task<StepCheckResult> CheckDbOwnerAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var isMember = await _dbInfoRepository.IsDbOwnerMemberAsync(config.DatabaseName, config.LoginName, ct);
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.AddToDbOwner,
            AlreadyExists = isMember,
            CurrentStatus = isMember ? "已是 db_owner 成員" : "尚未加入 db_owner",
            AvailableActions = isMember ? ["跳過"] : ["執行"]
        };
    }

    private async Task<StepCheckResult> CheckJobAsync(MaintenancePlanConfig config, string jobName, MaintenancePlanStep step, CancellationToken ct)
    {
        var exists = await _dbInfoRepository.AgentJobExistsAsync(jobName, ct);
        return new StepCheckResult
        {
            Step = step,
            AlreadyExists = exists,
            CurrentStatus = exists ? $"Job [{jobName}] 已存在" : $"Job [{jobName}] 不存在",
            AvailableActions = exists ? ["跳過", "刪除重建"] : ["執行"]
        };
    }
}
```

- [ ] **Step 6: 撰寫 AgentJobService 測試**

```csharp
// tests/TableSpec.Application.Tests/Services/AgentJobServiceTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

public class AgentJobServiceTests
{
    private readonly IAgentJobRepository _repository;
    private readonly AgentJobService _service;

    public AgentJobServiceTests()
    {
        _repository = Substitute.For<IAgentJobRepository>();
        _service = new AgentJobService(_repository);
    }

    [Fact]
    public async Task GetJobsAsync_應委派給Repository()
    {
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = Guid.NewGuid(), Name = "TestJob", IsEnabled = true, Description = "[TableSpec]" }
        };
        _repository.GetTableSpecJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs);

        var result = await _service.GetJobsAsync();

        result.Should().BeEquivalentTo(jobs);
        await _repository.Received(1).GetTableSpecJobsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetJobEnabledAsync_應委派給Repository()
    {
        var jobId = Guid.NewGuid();

        await _service.SetJobEnabledAsync(jobId, false);

        await _repository.Received(1).SetJobEnabledAsync(jobId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJobAsync_應委派給Repository()
    {
        var jobId = Guid.NewGuid();

        await _service.DeleteJobAsync(jobId);

        await _repository.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartJobAsync_應委派給Repository()
    {
        var jobId = Guid.NewGuid();

        await _service.StartJobAsync(jobId);

        await _repository.Received(1).StartJobAsync(jobId, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 7: 撰寫 MaintenancePlanService 測試**

```csharp
// tests/TableSpec.Application.Tests/Services/MaintenancePlanServiceTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

public class MaintenancePlanServiceTests
{
    private readonly IDatabaseInfoRepository _dbInfoRepo;
    private readonly IAgentJobRepository _agentJobRepo;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;
    private readonly MaintenancePlanService _service;

    public MaintenancePlanServiceTests()
    {
        _dbInfoRepo = Substitute.For<IDatabaseInfoRepository>();
        _agentJobRepo = Substitute.For<IAgentJobRepository>();
        _sqlGenerator = Substitute.For<IMaintenancePlanSqlGenerator>();
        _service = new MaintenancePlanService(_dbInfoRepo, _agentJobRepo, _sqlGenerator);

        // 預設非 Azure SQL
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
    }

    #region CheckPrerequisitesAsync 測試

    [Fact]
    public async Task CheckPrerequisitesAsync_AzureSQL_應回傳錯誤()
    {
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(true);

        var (isReady, error) = await _service.CheckPrerequisitesAsync();

        isReady.Should().BeFalse();
        error.Should().Contain("Azure SQL Database");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_Agent未啟動_應回傳錯誤()
    {
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (isReady, error) = await _service.CheckPrerequisitesAsync();

        isReady.Should().BeFalse();
        error.Should().Contain("SQL Server Agent");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_無權限_應回傳錯誤()
    {
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(true);
        _agentJobRepo.HasAgentPermissionAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (isReady, error) = await _service.CheckPrerequisitesAsync();

        isReady.Should().BeFalse();
        error.Should().Contain("權限");
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_一切正常_應回傳Ready()
    {
        _dbInfoRepo.IsAzureSqlDatabaseAsync(Arg.Any<CancellationToken>()).Returns(false);
        _agentJobRepo.IsAgentRunningAsync(Arg.Any<CancellationToken>()).Returns(true);
        _agentJobRepo.HasAgentPermissionAsync(Arg.Any<CancellationToken>()).Returns(true);

        var (isReady, error) = await _service.CheckPrerequisitesAsync();

        isReady.Should().BeTrue();
        error.Should().BeNull();
    }

    #endregion

    #region CheckStepsAsync 測試

    [Fact]
    public async Task CheckStepsAsync_RecoveryModel已是SIMPLE_應顯示已設定()
    {
        _dbInfoRepo.GetRecoveryModelAsync("TestDB", Arg.Any<CancellationToken>()).Returns("SIMPLE");
        var config = CreateConfig(steps: [MaintenancePlanStep.SetRecoveryModel]);

        var results = await _service.CheckStepsAsync(config);

        results.Should().HaveCount(1);
        results[0].AlreadyExists.Should().BeTrue();
        results[0].CurrentStatus.Should().Contain("SIMPLE");
    }

    [Fact]
    public async Task CheckStepsAsync_RecoveryModel為FULL_應顯示需要執行()
    {
        _dbInfoRepo.GetRecoveryModelAsync("TestDB", Arg.Any<CancellationToken>()).Returns("FULL");
        var config = CreateConfig(steps: [MaintenancePlanStep.SetRecoveryModel]);

        var results = await _service.CheckStepsAsync(config);

        results[0].AlreadyExists.Should().BeFalse();
        results[0].AvailableActions.Should().Contain("執行");
    }

    [Fact]
    public async Task CheckStepsAsync_登入帳號已存在_應提供跳過和刪除重建()
    {
        _dbInfoRepo.LoginExistsAsync("mis", Arg.Any<CancellationToken>()).Returns(true);
        var config = CreateConfig(steps: [MaintenancePlanStep.CreateLoginAndUser]);

        var results = await _service.CheckStepsAsync(config);

        results[0].AlreadyExists.Should().BeTrue();
        results[0].AvailableActions.Should().Contain("跳過");
        results[0].AvailableActions.Should().Contain("刪除重建");
    }

    [Fact]
    public async Task CheckStepsAsync_Job已存在_應提供跳過和刪除重建()
    {
        _dbInfoRepo.AgentJobExistsAsync("TestDB_FullBackup", Arg.Any<CancellationToken>()).Returns(true);
        var config = CreateConfig(steps: [MaintenancePlanStep.CreateBackupJob]);

        var results = await _service.CheckStepsAsync(config);

        results[0].AlreadyExists.Should().BeTrue();
        results[0].AvailableActions.Should().Contain("刪除重建");
    }

    #endregion

    private static MaintenancePlanConfig CreateConfig(IReadOnlyList<MaintenancePlanStep>? steps = null)
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = "TestDB",
            BackupPath = "D:\\Backup\\",
            RestorePath = "D:\\Data\\",
            TestDatabaseName = "TestDB-test",
            LoginName = "mis",
            LoginPassword = "pass",
            BackupTime = 020000,
            RestoreTime = 030000,
            SelectedSteps = steps ?? [MaintenancePlanStep.SetRecoveryModel]
        };
    }
}
```

- [ ] **Step 8: 執行測試確認通過**

Run: `dotnet test tests/TableSpec.Application.Tests --filter "AgentJobServiceTests|MaintenancePlanServiceTests" -v minimal`
Expected: All tests PASS

- [ ] **Step 9: Commit**

```bash
git add src/TableSpec.Application/Services/IMaintenancePlanService.cs src/TableSpec.Application/Services/MaintenancePlanService.cs src/TableSpec.Application/Services/IMaintenancePlanSqlGenerator.cs src/TableSpec.Application/Services/IAgentJobService.cs src/TableSpec.Application/Services/AgentJobService.cs tests/TableSpec.Application.Tests/Services/AgentJobServiceTests.cs tests/TableSpec.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "新增維護計劃 Application 層服務"
```

---

### Task 4: Infrastructure 層 — SQL 產生器

**Files:**
- Create: `src/TableSpec.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`
- Test: `tests/TableSpec.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs`

- [ ] **Step 1: 撰寫 SQL 產生器測試**

```csharp
// tests/TableSpec.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
using FluentAssertions;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Infrastructure.Services;

namespace TableSpec.Infrastructure.Tests.Services;

public class MaintenancePlanSqlGeneratorTests
{
    private readonly MaintenancePlanSqlGenerator _generator = new();

    #region GenerateStepSql 測試

    [Fact]
    public void GenerateStepSql_SetRecoveryModel_應包含ALTER_DATABASE()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.SetRecoveryModel, config);

        sql.Should().Contain("ALTER DATABASE");
        sql.Should().Contain("[TestDB]");
        sql.Should().Contain("SIMPLE");
    }

    [Fact]
    public void GenerateStepSql_SetRecoveryModel_應同時設定測試資料庫()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.SetRecoveryModel, config);

        sql.Should().Contain("[TestDB-test]");
    }

    [Fact]
    public void GenerateStepSql_CreateLoginAndUser_應使用QUOTENAME防注入()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateLoginAndUser, config);

        sql.Should().Contain("CREATE LOGIN");
        sql.Should().Contain("[mis]");
    }

    [Fact]
    public void GenerateStepSql_CreateLoginAndUser_密碼應轉義單引號()
    {
        var config = new MaintenancePlanConfig
        {
            DatabaseName = "TestDB",
            BackupPath = "D:\\Backup\\",
            RestorePath = "D:\\Data\\",
            TestDatabaseName = "TestDB-test",
            LoginName = "mis",
            LoginPassword = "pass'word",
            BackupTime = 020000,
            RestoreTime = 030000,
            SelectedSteps = [MaintenancePlanStep.CreateLoginAndUser]
        };
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateLoginAndUser, config);

        sql.Should().Contain("pass''word");
        sql.Should().NotContain("pass'word");
    }

    [Fact]
    public void GenerateStepSql_CreateBackupJob_應包含Job名稱和TableSpec標記()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config);

        sql.Should().Contain("TestDB_FullBackup");
        sql.Should().Contain("[TableSpec]");
        sql.Should().Contain("sp_add_job");
    }

    [Fact]
    public void GenerateStepSql_CreateBackupJob_應使用設定的排程時間()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config);

        sql.Should().Contain("020000");
    }

    [Fact]
    public void GenerateStepSql_CreateBackupJob_刪除重建_應先刪除Job()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, "刪除重建");

        sql.Should().Contain("sp_delete_job");
        sql.Should().Contain("sp_add_job");
    }

    [Fact]
    public void GenerateStepSql_CreateRestoreJob_應包含RESTORE_DATABASE()
    {
        var config = CreateConfig();
        var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, config);

        sql.Should().Contain("RESTORE DATABASE");
        sql.Should().Contain("[TestDB-test]");
    }

    #endregion

    #region GenerateFullSql 測試

    [Fact]
    public void GenerateFullSql_應包含BEGIN_TRANSACTION()
    {
        var config = CreateConfig(steps: [MaintenancePlanStep.SetRecoveryModel, MaintenancePlanStep.CreateBackupJob]);
        var checkResults = new List<StepCheckResult>
        {
            new() { Step = MaintenancePlanStep.SetRecoveryModel, AlreadyExists = false, CurrentStatus = "FULL", AvailableActions = ["執行"], SelectedAction = "執行" },
            new() { Step = MaintenancePlanStep.CreateBackupJob, AlreadyExists = false, CurrentStatus = "不存在", AvailableActions = ["執行"], SelectedAction = "執行" }
        };

        var sql = _generator.GenerateFullSql(config, checkResults);

        sql.Should().Contain("BEGIN TRANSACTION");
        sql.Should().Contain("COMMIT");
    }

    [Fact]
    public void GenerateFullSql_跳過的步驟_不應產生SQL()
    {
        var config = CreateConfig(steps: [MaintenancePlanStep.SetRecoveryModel]);
        var checkResults = new List<StepCheckResult>
        {
            new() { Step = MaintenancePlanStep.SetRecoveryModel, AlreadyExists = true, CurrentStatus = "SIMPLE", AvailableActions = ["跳過"], SelectedAction = "跳過" }
        };

        var sql = _generator.GenerateFullSql(config, checkResults);

        sql.Should().NotContain("ALTER DATABASE");
    }

    #endregion

    private static MaintenancePlanConfig CreateConfig(IReadOnlyList<MaintenancePlanStep>? steps = null)
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = "TestDB",
            BackupPath = "D:\\Backup\\",
            RestorePath = "D:\\Data\\",
            TestDatabaseName = "TestDB-test",
            LoginName = "mis",
            LoginPassword = "pass",
            BackupTime = 020000,
            RestoreTime = 030000,
            SelectedSteps = steps ?? [MaintenancePlanStep.SetRecoveryModel]
        };
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/TableSpec.Infrastructure.Tests --filter "MaintenancePlanSqlGeneratorTests" -v minimal`
Expected: FAIL (class not found)

- [ ] **Step 3: 實作 MaintenancePlanSqlGenerator**

建立 `src/TableSpec.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`，實作 `IMaintenancePlanSqlGenerator` 介面。

各步驟的 SQL 基於參考範本（`資料庫檢查到備份計劃SIMPLE-範本.sql`）產生：

- `SetRecoveryModel`: `ALTER DATABASE [{db}] SET RECOVERY SIMPLE WITH NO_WAIT`
- `RenameLogicalFiles`: `ALTER DATABASE [{db}] MODIFY FILE (NAME=N'shltw_Data', NEWNAME=N'{db}_Data')`
- `CreateLoginAndUser`: `CREATE LOGIN [{login}]` + `CREATE USER [{login}]` + `ALTER USER [{login}] WITH LOGIN`
- `AddToDbOwner`: `ALTER ROLE [db_owner] ADD MEMBER [{login}]`
- `CreateBackupJob`: `sp_add_job` + `sp_add_jobstep`（BACKUP DATABASE + xp_delete_file）+ `sp_add_jobschedule` + `sp_add_jobserver`
- `CreateRestoreJob`: `sp_add_job` + `sp_add_jobstep`（SINGLE_USER + RESTORE DATABASE + MULTI_USER）+ `sp_add_jobschedule` + `sp_add_jobserver`

安全性：
- 資料庫名稱用 `[{name}]` 括號包裹（QUOTENAME 效果）
- 密碼用 `EscapeSingleQuote()` 方法轉義
- 路徑用 `EscapeSingleQuote()` 方法轉義
- Job description 加入 `[TableSpec]` 標記

`GenerateFullSql` 方法將設定類步驟（1-4）包在 `BEGIN TRANSACTION...COMMIT`，備份和還原 Job 各自獨立交易。跳過的步驟不產生 SQL。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/TableSpec.Infrastructure.Tests --filter "MaintenancePlanSqlGeneratorTests" -v minimal`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/TableSpec.Infrastructure/Services/MaintenancePlanSqlGenerator.cs tests/TableSpec.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
git commit -m "新增維護計劃 SQL 產生器"
```

---

### Task 5: Infrastructure 層 — Repository 實作

**Files:**
- Create: `src/TableSpec.Infrastructure/Repositories/AgentJobRepository.cs`
- Create: `src/TableSpec.Infrastructure/Repositories/DatabaseInfoRepository.cs`

- [ ] **Step 1: 實作 DatabaseInfoRepository**

```csharp
// src/TableSpec.Infrastructure/Repositories/DatabaseInfoRepository.cs
using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 資料庫資訊查詢 Repository 實作
/// </summary>
public class DatabaseInfoRepository : IDatabaseInfoRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public DatabaseInfoRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<string>> GetDatabaseNamesAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return [];

        const string sql = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";
        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<string>(sql, cancellationToken: ct);
        return result.ToList();
    }

    public async Task<string> GetRecoveryModelAsync(string databaseName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return "UNKNOWN";

        const string sql = "SELECT recovery_model_desc FROM sys.databases WHERE name = @DatabaseName";
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleOrDefaultAsync<string>(sql, new { DatabaseName = databaseName }, cancellationToken: ct) ?? "UNKNOWN";
    }

    public async Task<IReadOnlyList<(string LogicalName, string PhysicalName)>> GetLogicalFileNamesAsync(string databaseName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return [];

        const string sql = "SELECT name AS LogicalName, physical_name AS PhysicalName FROM sys.master_files WHERE database_id = DB_ID(@DatabaseName)";
        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<(string LogicalName, string PhysicalName)>(sql, new { DatabaseName = databaseName }, cancellationToken: ct);
        return result.ToList();
    }

    public async Task<bool> LoginExistsAsync(string loginName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        const string sql = "SELECT COUNT(1) FROM sys.server_principals WHERE name = @LoginName";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { LoginName = loginName }, cancellationToken: ct) > 0;
    }

    public async Task<bool> DatabaseUserExistsAsync(string databaseName, string userName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        // 使用 QUOTENAME 防止 SQL 注入
        var safeName = QuoteName(databaseName);
        var sql = $"SELECT COUNT(1) FROM {safeName}.sys.database_principals WHERE name = @UserName";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { UserName = userName }, cancellationToken: ct) > 0;
    }

    public async Task<bool> IsDbOwnerMemberAsync(string databaseName, string userName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        var safeName = QuoteName(databaseName);
        var sql = $@"
SELECT COUNT(1)
FROM {safeName}.sys.database_role_members rm
JOIN {safeName}.sys.database_principals r ON rm.role_principal_id = r.principal_id
JOIN {safeName}.sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE r.name = 'db_owner' AND m.name = @UserName";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { UserName = userName }, cancellationToken: ct) > 0;
    }

    public async Task<bool> IsAzureSqlDatabaseAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        const string sql = "SELECT CAST(SERVERPROPERTY('EngineEdition') AS INT)";
        await using var connection = new SqlConnection(connectionString);
        var edition = await connection.ExecuteScalarAsync<int>(sql, cancellationToken: ct);
        return edition == 5; // 5 = Azure SQL Database
    }

    public async Task ExecuteSqlWithTransactionAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(sql, transaction: transaction, cancellationToken: ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql, cancellationToken: ct);
    }

    /// <summary>安全的資料庫名稱括號包裹（等同 SQL 的 QUOTENAME）</summary>
    private static string QuoteName(string name) => $"[{name.Replace("]", "]]")}]";

    public async Task<bool> AgentJobExistsAsync(string jobName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        const string sql = "SELECT COUNT(1) FROM msdb.dbo.sysjobs WHERE name = @JobName";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, new { JobName = jobName }, cancellationToken: ct) > 0;
    }
}
```

- [ ] **Step 2: 實作 AgentJobRepository**

```csharp
// src/TableSpec.Infrastructure/Repositories/AgentJobRepository.cs
using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// SQL Agent Job 資料存取實作
/// </summary>
public class AgentJobRepository : IAgentJobRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public AgentJobRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<AgentJobInfo>> GetTableSpecJobsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return [];

        const string sql = @"
SELECT
    j.job_id AS JobId,
    j.name AS Name,
    j.description AS Description,
    CAST(j.enabled AS BIT) AS IsEnabled,
    CASE WHEN jh.run_date IS NOT NULL AND jh.run_date > 0
        THEN CAST(CAST(jh.run_date AS VARCHAR(8)) AS DATETIME) + CAST(STUFF(STUFF(RIGHT('000000' + CAST(jh.run_time AS VARCHAR(6)), 6), 3, 0, ':'), 6, 0, ':') AS DATETIME)
        ELSE NULL END AS LastRunDate,
    jh.run_status AS LastRunOutcome,
    CASE WHEN js.next_run_date IS NOT NULL AND js.next_run_date > 0
        THEN CAST(CAST(js.next_run_date AS VARCHAR(8)) AS DATETIME) + CAST(STUFF(STUFF(RIGHT('000000' + CAST(js.next_run_time AS VARCHAR(6)), 6), 3, 0, ':'), 6, 0, ':') AS DATETIME)
        ELSE NULL END AS NextRunDate,
    sch.active_start_time AS ScheduleTime,
    sch.freq_type AS ScheduleFreqType
FROM msdb.dbo.sysjobs j
LEFT JOIN (
    SELECT job_id, run_date, run_time, run_status,
           ROW_NUMBER() OVER (PARTITION BY job_id ORDER BY run_date DESC, run_time DESC) AS rn
    FROM msdb.dbo.sysjobhistory WHERE step_id = 0
) jh ON j.job_id = jh.job_id AND jh.rn = 1
LEFT JOIN msdb.dbo.sysjobschedules js ON j.job_id = js.job_id
LEFT JOIN msdb.dbo.sysschedules sch ON js.schedule_id = sch.schedule_id
WHERE j.description LIKE '%[[]TableSpec]%'
ORDER BY j.name";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<AgentJobInfo>(sql, cancellationToken: ct);
        return result.ToList();
    }

    public async Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return [];

        const string sql = @"
SELECT TOP(@MaxRecords)
    job_id AS JobId,
    step_name AS StepName,
    CAST(CAST(run_date AS VARCHAR(8)) AS DATETIME) + CAST(STUFF(STUFF(RIGHT('000000' + CAST(run_time AS VARCHAR(6)), 6), 3, 0, ':'), 6, 0, ':') AS DATETIME) AS RunDate,
    run_status AS RunStatus,
    run_duration AS DurationSeconds,
    message AS Message
FROM msdb.dbo.sysjobhistory
WHERE job_id = @JobId
ORDER BY run_date DESC, run_time DESC";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<AgentJobHistory>(sql, new { JobId = jobId, MaxRecords = maxRecords }, cancellationToken: ct);
        return result.ToList();
    }

    public async Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        const string sql = "EXEC msdb.dbo.sp_update_job @job_id = @JobId, @enabled = @Enabled";
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql, new { JobId = jobId, Enabled = enabled ? 1 : 0 }, cancellationToken: ct);
    }

    public async Task StartJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        const string sql = "EXEC msdb.dbo.sp_start_job @job_id = @JobId";
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql, new { JobId = jobId }, cancellationToken: ct);
    }

    public async Task DeleteJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        const string sql = "EXEC msdb.dbo.sp_delete_job @job_id = @JobId, @delete_unused_schedule = 1";
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql, new { JobId = jobId }, cancellationToken: ct);
    }

    public async Task UpdateJobScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return;

        const string sql = @"
DECLARE @scheduleId INT;
SELECT @scheduleId = sch.schedule_id
FROM msdb.dbo.sysjobschedules js
JOIN msdb.dbo.sysschedules sch ON js.schedule_id = sch.schedule_id
WHERE js.job_id = @JobId;

IF @scheduleId IS NOT NULL
    EXEC msdb.dbo.sp_update_schedule @schedule_id = @scheduleId, @freq_type = @FreqType, @freq_interval = @FreqInterval, @active_start_time = @ActiveStartTime";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql, new { JobId = jobId, FreqType = freqType, FreqInterval = freqInterval, ActiveStartTime = activeStartTime }, cancellationToken: ct);
    }

    public async Task<bool> IsAgentRunningAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        const string sql = @"
SELECT COUNT(1) FROM sys.dm_server_services
WHERE servicename LIKE 'SQL Server Agent%' AND status = 4";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, cancellationToken: ct) > 0;
    }

    public async Task<bool> HasAgentPermissionAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString)) return false;

        const string sql = @"
SELECT COUNT(1) FROM msdb.sys.database_role_members rm
JOIN msdb.sys.database_principals r ON rm.role_principal_id = r.principal_id
JOIN msdb.sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE r.name IN ('SQLAgentOperatorRole', 'SQLAgentReaderRole', 'SQLAgentUserRole', 'db_owner', 'sysadmin')
AND m.name = SUSER_SNAME()";
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, cancellationToken: ct) > 0;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/TableSpec.Infrastructure/Repositories/AgentJobRepository.cs src/TableSpec.Infrastructure/Repositories/DatabaseInfoRepository.cs
git commit -m "新增維護計劃 Infrastructure 層 Repository 實作"
```

---

### Task 6: Desktop 層 — 管理面板 ViewModel

**Files:**
- Create: `src/TableSpec.Desktop/ViewModels/MaintenancePlanManagerViewModel.cs`
- Test: `tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanManagerViewModelTests.cs`

- [ ] **Step 1: 撰寫 ViewModel 測試**

```csharp
// tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanManagerViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

public class MaintenancePlanManagerViewModelTests
{
    private readonly IAgentJobService _jobService;
    private readonly IMaintenancePlanService _planService;

    public MaintenancePlanManagerViewModelTests()
    {
        _jobService = Substitute.For<IAgentJobService>();
        _planService = Substitute.For<IMaintenancePlanService>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new MaintenancePlanManagerViewModel();
        vm.Should().NotBeNull();
        vm.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadJobsAsync_應載入Job清單()
    {
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = Guid.NewGuid(), Name = "DB_FullBackup", IsEnabled = true, Description = "[TableSpec]" }
        };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs);

        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService);
        await vm.LoadJobsCommand.ExecuteAsync(null);

        vm.Jobs.Should().HaveCount(1);
        vm.Jobs[0].Name.Should().Be("DB_FullBackup");
    }

    [Fact]
    public async Task DeleteJobAsync_應呼叫Service並重新載入()
    {
        var jobId = Guid.NewGuid();
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = jobId, Name = "DB_FullBackup", IsEnabled = true, Description = "[TableSpec]" }
        };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs, new List<AgentJobInfo>());

        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService);
        vm.SelectedJob = jobs[0];
        vm.ConfirmDeleteCallback = () => Task.FromResult(true);

        await vm.DeleteJobCommand.ExecuteAsync(null);

        await _jobService.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleJobAsync_啟用變停用_應呼叫Service()
    {
        var jobId = Guid.NewGuid();
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = jobId, Name = "DB_FullBackup", IsEnabled = true, Description = "[TableSpec]" }
        };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs);

        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService);
        vm.SelectedJob = jobs[0];

        await vm.ToggleJobCommand.ExecuteAsync(null);

        await _jobService.Received(1).SetJobEnabledAsync(jobId, false, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "MaintenancePlanManagerViewModelTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 實作 MaintenancePlanManagerViewModel**

```csharp
// src/TableSpec.Desktop/ViewModels/MaintenancePlanManagerViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 維護計劃管理面板 ViewModel
/// </summary>
public partial class MaintenancePlanManagerViewModel : ViewModelBase
{
    private readonly IAgentJobService? _jobService;
    private readonly IMaintenancePlanService? _planService;

    [ObservableProperty]
    private AgentJobInfo? _selectedJob;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<AgentJobInfo> Jobs { get; } = [];

    /// <summary>刪除確認回呼</summary>
    public Func<Task<bool>>? ConfirmDeleteCallback { get; set; }

    /// <summary>開啟精靈回呼</summary>
    public Func<Task>? OpenWizardCallback { get; set; }

    /// <summary>開啟排程編輯回呼</summary>
    public Func<AgentJobInfo, Task>? EditScheduleCallback { get; set; }

    public MaintenancePlanManagerViewModel() { }

    public MaintenancePlanManagerViewModel(IAgentJobService jobService, IMaintenancePlanService planService)
    {
        _jobService = jobService;
        _planService = planService;
    }

    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        if (_jobService == null) return;
        IsLoading = true;
        try
        {
            var jobs = await _jobService.GetJobsAsync();
            Jobs.Clear();
            foreach (var job in jobs) Jobs.Add(job);
            StatusMessage = $"已載入 {jobs.Count} 個維護計劃";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入失敗：{ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ToggleJobAsync()
    {
        if (_jobService == null || SelectedJob == null) return;
        try
        {
            await _jobService.SetJobEnabledAsync(SelectedJob.JobId, !SelectedJob.IsEnabled);
            await LoadJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"操作失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartJobAsync()
    {
        if (_jobService == null || SelectedJob == null) return;
        try
        {
            await _jobService.StartJobAsync(SelectedJob.JobId);
            StatusMessage = $"已觸發執行 Job [{SelectedJob.Name}]";
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteJobAsync()
    {
        if (_jobService == null || SelectedJob == null) return;
        if (ConfirmDeleteCallback != null)
        {
            var confirmed = await ConfirmDeleteCallback();
            if (!confirmed) return;
        }
        try
        {
            await _jobService.DeleteJobAsync(SelectedJob.JobId);
            StatusMessage = $"已刪除 Job [{SelectedJob.Name}]";
            await LoadJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EditScheduleAsync()
    {
        if (SelectedJob == null || EditScheduleCallback == null) return;
        await EditScheduleCallback(SelectedJob);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task OpenWizardAsync()
    {
        if (OpenWizardCallback == null) return;
        await OpenWizardCallback();
        await LoadJobsAsync();
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "MaintenancePlanManagerViewModelTests" -v minimal`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/TableSpec.Desktop/ViewModels/MaintenancePlanManagerViewModel.cs tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanManagerViewModelTests.cs
git commit -m "新增維護計劃管理面板 ViewModel"
```

---

### Task 7: Desktop 層 — 精靈 ViewModel

**Files:**
- Create: `src/TableSpec.Desktop/ViewModels/MaintenancePlanWizardViewModel.cs`
- Test: `tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanWizardViewModelTests.cs`

- [ ] **Step 1: 撰寫精靈 ViewModel 測試**

```csharp
// tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanWizardViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Desktop.Tests.ViewModels;

public class MaintenancePlanWizardViewModelTests
{
    private readonly IMaintenancePlanService _planService;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;

    public MaintenancePlanWizardViewModelTests()
    {
        _planService = Substitute.For<IMaintenancePlanService>();
        _sqlGenerator = Substitute.For<IMaintenancePlanSqlGenerator>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new MaintenancePlanWizardViewModel();
        vm.Should().NotBeNull();
        vm.CurrentStep.Should().Be(1);
    }

    [Fact]
    public void 初始狀態_預設值應正確()
    {
        var vm = new MaintenancePlanWizardViewModel();

        vm.LoginName.Should().Be("mis");
        vm.BackupTime.Hours.Should().Be(2);
        vm.RestoreTime.Hours.Should().Be(3);
        vm.IsStep1Visible.Should().BeTrue();
        vm.IsStep2Visible.Should().BeFalse();
        vm.IsStep3Visible.Should().BeFalse();
    }

    [Fact]
    public void NextStep_從步驟1到步驟2_應切換顯示()
    {
        var vm = new MaintenancePlanWizardViewModel();
        vm.DatabaseName = "TestDB";
        vm.BackupPath = "D:\\Backup\\";
        vm.RestorePath = "D:\\Data\\";
        vm.LoginPassword = "pass";

        vm.NextStepCommand.Execute(null);

        vm.CurrentStep.Should().Be(2);
        vm.IsStep1Visible.Should().BeFalse();
        vm.IsStep2Visible.Should().BeTrue();
    }

    [Fact]
    public void PreviousStep_從步驟2到步驟1_應切換顯示()
    {
        var vm = new MaintenancePlanWizardViewModel();
        vm.DatabaseName = "TestDB";
        vm.BackupPath = "D:\\Backup\\";
        vm.RestorePath = "D:\\Data\\";
        vm.LoginPassword = "pass";
        vm.NextStepCommand.Execute(null);

        vm.PreviousStepCommand.Execute(null);

        vm.CurrentStep.Should().Be(1);
        vm.IsStep1Visible.Should().BeTrue();
    }

    [Fact]
    public void CanNextStep_步驟1欄位未填_應為False()
    {
        var vm = new MaintenancePlanWizardViewModel();
        vm.NextStepCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void TestDatabaseName_應自動帶入()
    {
        var vm = new MaintenancePlanWizardViewModel();
        vm.DatabaseName = "WayDoSoft01";

        vm.TestDatabaseName.Should().Be("WayDoSoft01-test");
    }

    [Fact]
    public void SelectedSteps_CreateRestoreJob_預設不勾選()
    {
        var vm = new MaintenancePlanWizardViewModel();

        vm.IsCreateRestoreJobSelected.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "MaintenancePlanWizardViewModelTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 實作 MaintenancePlanWizardViewModel**

```csharp
// src/TableSpec.Desktop/ViewModels/MaintenancePlanWizardViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 維護計劃建立精靈 ViewModel
/// </summary>
public partial class MaintenancePlanWizardViewModel : ViewModelBase
{
    private readonly IMaintenancePlanService? _planService;
    private readonly IMaintenancePlanSqlGenerator? _sqlGenerator;

    // 步驟一：基本設定
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _restorePath = string.Empty;

    [ObservableProperty]
    private string _testDatabaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _loginName = "mis";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private TimeSpan _backupTime = new(2, 0, 0);

    [ObservableProperty]
    private TimeSpan _restoreTime = new(3, 0, 0);

    // 步驟二：選擇步驟
    [ObservableProperty]
    private bool _isSetRecoveryModelSelected = true;

    [ObservableProperty]
    private bool _isRenameLogicalFilesSelected = true;

    [ObservableProperty]
    private bool _isCreateLoginAndUserSelected = true;

    [ObservableProperty]
    private bool _isAddToDbOwnerSelected = true;

    [ObservableProperty]
    private bool _isCreateBackupJobSelected = true;

    [ObservableProperty]
    private bool _isCreateRestoreJobSelected; // 預設不勾選

    // 精靈狀態
    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _previewSql = string.Empty;

    public ObservableCollection<string> Databases { get; } = [];
    public ObservableCollection<StepCheckResult> CheckResults { get; } = [];
    public ObservableCollection<string> ExecutionLog { get; } = [];

    public bool IsStep1Visible => CurrentStep == 1;
    public bool IsStep2Visible => CurrentStep == 2;
    public bool IsStep3Visible => CurrentStep == 3;

    public MaintenancePlanWizardViewModel() { }

    public MaintenancePlanWizardViewModel(
        IMaintenancePlanService planService,
        IMaintenancePlanSqlGenerator sqlGenerator)
    {
        _planService = planService;
        _sqlGenerator = sqlGenerator;
    }

    partial void OnDatabaseNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            TestDatabaseName = $"{value}-test";
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
    }

    private bool CanNextStep()
    {
        return CurrentStep switch
        {
            1 => !string.IsNullOrWhiteSpace(DatabaseName) &&
                 !string.IsNullOrWhiteSpace(BackupPath) &&
                 !string.IsNullOrWhiteSpace(RestorePath) &&
                 !string.IsNullOrWhiteSpace(LoginName) &&
                 !string.IsNullOrWhiteSpace(LoginPassword),
            2 => true,
            _ => false
        };
    }

    [RelayCommand(CanExecute = nameof(CanNextStep))]
    private async Task NextStepAsync()
    {
        if (CurrentStep == 2)
        {
            // 進入步驟三前執行前置檢查
            await RunPreChecksAsync();
        }
        CurrentStep++;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    private async Task ExecuteAsync(CancellationToken ct)
    {
        if (_planService == null) return;
        IsExecuting = true;
        ExecutionLog.Clear();
        try
        {
            var config = BuildConfig();
            var progress = new Progress<string>(msg => ExecutionLog.Add(msg));
            await _planService.ExecutePlanAsync(config, CheckResults.ToList(), progress, ct);
            StatusMessage = "維護計劃執行完成";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消執行";
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗：{ex.Message}";
        }
        finally { IsExecuting = false; }
    }

    [RelayCommand]
    private async Task GeneratePreviewAsync()
    {
        if (_planService == null) return;
        var config = BuildConfig();
        PreviewSql = await _planService.GeneratePreviewSqlAsync(config, CheckResults.ToList());
    }

    private async Task RunPreChecksAsync()
    {
        if (_planService == null) return;
        CheckResults.Clear();
        var config = BuildConfig();
        var results = await _planService.CheckStepsAsync(config);
        foreach (var r in results) CheckResults.Add(r);
    }

    private IReadOnlyList<MaintenancePlanStep> GetSelectedSteps()
    {
        var steps = new List<MaintenancePlanStep>();
        if (IsSetRecoveryModelSelected) steps.Add(MaintenancePlanStep.SetRecoveryModel);
        if (IsRenameLogicalFilesSelected) steps.Add(MaintenancePlanStep.RenameLogicalFiles);
        if (IsCreateLoginAndUserSelected) steps.Add(MaintenancePlanStep.CreateLoginAndUser);
        if (IsAddToDbOwnerSelected) steps.Add(MaintenancePlanStep.AddToDbOwner);
        if (IsCreateBackupJobSelected) steps.Add(MaintenancePlanStep.CreateBackupJob);
        if (IsCreateRestoreJobSelected) steps.Add(MaintenancePlanStep.CreateRestoreJob);
        return steps;
    }

    private MaintenancePlanConfig BuildConfig()
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = DatabaseName,
            BackupPath = BackupPath,
            RestorePath = RestorePath,
            TestDatabaseName = TestDatabaseName,
            LoginName = LoginName,
            LoginPassword = LoginPassword,
            BackupTime = BackupTime.Hours * 10000 + BackupTime.Minutes * 100,
            RestoreTime = RestoreTime.Hours * 10000 + RestoreTime.Minutes * 100,
            SelectedSteps = GetSelectedSteps()
        };
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/TableSpec.Desktop.Tests --filter "MaintenancePlanWizardViewModelTests" -v minimal`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/TableSpec.Desktop/ViewModels/MaintenancePlanWizardViewModel.cs tests/TableSpec.Desktop.Tests/ViewModels/MaintenancePlanWizardViewModelTests.cs
git commit -m "新增維護計劃精靈 ViewModel"
```

---

### Task 8: Desktop 層 — 管理視窗 View

**Files:**
- Create: `src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml`
- Create: `src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml.cs`

- [ ] **Step 1: 建立管理視窗 AXAML**

```xml
<!-- src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:TableSpec.Desktop.ViewModels"
        x:Class="TableSpec.Desktop.Views.MaintenancePlanManagerWindow"
        x:DataType="vm:MaintenancePlanManagerViewModel"
        Icon="/Assets/TableSpec.png"
        Title="資料庫維護計劃管理"
        Width="900" Height="600"
        WindowStartupLocation="CenterOwner">

    <Design.DataContext>
        <vm:MaintenancePlanManagerViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto" Margin="15">
        <!-- 工具列 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" Margin="0,0,0,10">
            <Button Content="新增計劃" Command="{Binding OpenWizardCommand}" ToolTip.Tip="開啟建立精靈"/>
            <Button Content="重新整理" Command="{Binding LoadJobsCommand}"/>
            <Separator/>
            <Button Content="啟用/停用" Command="{Binding ToggleJobCommand}" IsEnabled="{Binding SelectedJob, Converter={x:Static ObjectConverters.IsNotNull}}"/>
            <Button Content="立即執行" Command="{Binding StartJobCommand}" IsEnabled="{Binding SelectedJob, Converter={x:Static ObjectConverters.IsNotNull}}"/>
            <Button Content="修改排程" Command="{Binding EditScheduleCommand}" IsEnabled="{Binding SelectedJob, Converter={x:Static ObjectConverters.IsNotNull}}"/>
            <Button Content="刪除" Command="{Binding DeleteJobCommand}" IsEnabled="{Binding SelectedJob, Converter={x:Static ObjectConverters.IsNotNull}}"/>
        </StackPanel>

        <!-- Job 清單 -->
        <DataGrid Grid.Row="1"
                  ItemsSource="{Binding Jobs}"
                  SelectedItem="{Binding SelectedJob}"
                  IsReadOnly="True"
                  AutoGenerateColumns="False"
                  GridLinesVisibility="Horizontal">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Job 名稱" Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="狀態" Binding="{Binding StatusText}" Width="80"/>
                <DataGridTextColumn Header="上次執行時間" Binding="{Binding LastRunDate, StringFormat='{}{0:yyyy-MM-dd HH:mm}'}" Width="150"/>
                <DataGridTextColumn Header="上次執行結果" Binding="{Binding LastRunOutcomeText}" Width="100"/>
                <DataGridTextColumn Header="下次排程時間" Binding="{Binding NextRunDate, StringFormat='{}{0:yyyy-MM-dd HH:mm}'}" Width="150"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- 狀態列 -->
        <TextBlock Grid.Row="2" Text="{Binding StatusMessage}" Margin="0,10,0,0"/>
    </Grid>
</Window>
```

- [ ] **Step 2: 建立管理視窗 Code-Behind**

```csharp
// src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml.cs
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class MaintenancePlanManagerWindow : Window
{
    public MaintenancePlanManagerWindow()
    {
        InitializeComponent();
    }

    public MaintenancePlanManagerWindow(MaintenancePlanManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ConfirmDeleteCallback = async () =>
        {
            // 使用 MessageBox 確認刪除
            // Avalonia 的確認對話框實作
            return true; // 暫時回傳 true，後續實作確認對話框
        };

        viewModel.OpenWizardCallback = async () =>
        {
            var wizardVm = App.Services?.GetRequiredService<MaintenancePlanWizardViewModel>()
                ?? new MaintenancePlanWizardViewModel();
            var wizard = new MaintenancePlanWizardWindow(wizardVm);
            await wizard.ShowDialog(this);
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MaintenancePlanManagerViewModel vm)
        {
            await vm.LoadJobsCommand.ExecuteAsync(null);
        }
    }
}
```

- [ ] **Step 3: 建置確認編譯通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml src/TableSpec.Desktop/Views/MaintenancePlanManagerWindow.axaml.cs
git commit -m "新增維護計劃管理視窗"
```

---

### Task 9: Desktop 層 — 精靈視窗 View

**Files:**
- Create: `src/TableSpec.Desktop/Views/MaintenancePlanWizardWindow.axaml`
- Create: `src/TableSpec.Desktop/Views/MaintenancePlanWizardWindow.axaml.cs`

- [ ] **Step 1: 建立精靈視窗 AXAML**

精靈視窗包含三個步驟面板（透過 `IsVisible` 切換），底部有「上一步」「下一步」「執行」「取消」按鈕。

- 步驟一：基本設定表單（資料庫下拉、路徑輸入、帳號密碼、時間選擇）
- 步驟二：勾選步驟清單（6 個 CheckBox）
- 步驟三：確認與執行（檢查結果 DataGrid、SQL 預覽、執行日誌）

視窗大小 `700x650`，`WindowStartupLocation="CenterOwner"`。

- [ ] **Step 2: 建立精靈視窗 Code-Behind**

```csharp
// src/TableSpec.Desktop/Views/MaintenancePlanWizardWindow.axaml.cs
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class MaintenancePlanWizardWindow : Window
{
    public MaintenancePlanWizardWindow()
    {
        InitializeComponent();
    }

    public MaintenancePlanWizardWindow(MaintenancePlanWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
```

- [ ] **Step 3: 建置確認編譯通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/TableSpec.Desktop/Views/MaintenancePlanWizardWindow.axaml src/TableSpec.Desktop/Views/MaintenancePlanWizardWindow.axaml.cs
git commit -m "新增維護計劃精靈視窗"
```

---

### Task 10: Desktop 層 — 排程編輯對話框

**Files:**
- Create: `src/TableSpec.Desktop/ViewModels/ScheduleEditViewModel.cs`
- Create: `src/TableSpec.Desktop/Views/ScheduleEditWindow.axaml`
- Create: `src/TableSpec.Desktop/Views/ScheduleEditWindow.axaml.cs`

- [ ] **Step 1: 建立 ScheduleEditViewModel**

```csharp
// src/TableSpec.Desktop/ViewModels/ScheduleEditViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 排程編輯 ViewModel
/// </summary>
public partial class ScheduleEditViewModel : ViewModelBase
{
    private readonly IAgentJobService? _jobService;
    private Guid _jobId;

    [ObservableProperty]
    private TimeSpan _scheduleTime;

    [ObservableProperty]
    private int _selectedFreqType = 4; // 每日

    [ObservableProperty]
    private int _freqInterval = 1;

    [ObservableProperty]
    private bool _isSaved;

    public ScheduleEditViewModel() { }

    public ScheduleEditViewModel(IAgentJobService jobService, Guid jobId, int currentTime, int freqType)
    {
        _jobService = jobService;
        _jobId = jobId;
        ScheduleTime = new TimeSpan(currentTime / 10000, (currentTime / 100) % 100, currentTime % 100);
        SelectedFreqType = freqType;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_jobService == null) return;
        var time = ScheduleTime.Hours * 10000 + ScheduleTime.Minutes * 100;
        await _jobService.UpdateScheduleAsync(_jobId, SelectedFreqType, FreqInterval, time);
        IsSaved = true;
    }
}
```

- [ ] **Step 2: 建立排程編輯視窗 AXAML**

小對話框，包含：時間選擇器、頻率選擇（每日/每週）、儲存/取消按鈕。視窗大小 `350x250`。

- [ ] **Step 3: 建立排程編輯視窗 Code-Behind**

```csharp
// src/TableSpec.Desktop/Views/ScheduleEditWindow.axaml.cs
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class ScheduleEditWindow : Window
{
    public ScheduleEditWindow()
    {
        InitializeComponent();
    }

    public ScheduleEditWindow(ScheduleEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

- [ ] **Step 4: 建置確認通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/TableSpec.Desktop/ViewModels/ScheduleEditViewModel.cs src/TableSpec.Desktop/Views/ScheduleEditWindow.axaml src/TableSpec.Desktop/Views/ScheduleEditWindow.axaml.cs
git commit -m "新增排程編輯對話框"
```

---

### Task 11: DI 註冊與選單整合

**Files:**
- Modify: `src/TableSpec.Desktop/Program.cs`
- Modify: `src/TableSpec.Desktop/Views/MainWindow.axaml`
- Modify: `src/TableSpec.Desktop/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: 在 Program.cs 註冊新服務**

在 `ConfigureServices()` 中新增：

```csharp
// === 維護計劃 ===
// Repositories
services.AddSingleton<IDatabaseInfoRepository>(sp =>
    new DatabaseInfoRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
services.AddSingleton<IAgentJobRepository>(sp =>
    new AgentJobRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

// Services
services.AddSingleton<IMaintenancePlanSqlGenerator, MaintenancePlanSqlGenerator>();
services.AddSingleton<IMaintenancePlanService, MaintenancePlanService>();
services.AddSingleton<IAgentJobService, AgentJobService>();

// ViewModels
services.AddTransient<MaintenancePlanManagerViewModel>();
services.AddTransient<MaintenancePlanWizardViewModel>();
```

- [ ] **Step 2: 在 MainWindow.axaml 新增選單項目**

在「工具」選單中，`<Separator/>` 後新增：

```xml
<MenuItem Header="資料庫維護計劃(_D)" Command="{Binding OpenMaintenancePlanCommand}" InputGesture="Ctrl+D" IsEnabled="{Binding IsConnected}"
          ToolTip.Tip="管理資料庫自動備份與還原排程">
    <MenuItem.Icon>
        <TextBlock Text="🔧" FontSize="14"/>
    </MenuItem.Icon>
</MenuItem>
```

- [ ] **Step 3: 在 MainWindowViewModel 新增命令**

```csharp
[RelayCommand]
private async Task OpenMaintenancePlanAsync()
{
    if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

    var viewModel = App.Services?.GetRequiredService<MaintenancePlanManagerViewModel>()
        ?? new MaintenancePlanManagerViewModel();
    var window = new MaintenancePlanManagerWindow(viewModel);
    await window.ShowDialog(desktop.MainWindow!);
}
```

- [ ] **Step 4: 建置確認通過**

Run: `dotnet build src/TableSpec.Desktop`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/TableSpec.Desktop/Program.cs src/TableSpec.Desktop/Views/MainWindow.axaml src/TableSpec.Desktop/ViewModels/MainWindowViewModel.cs
git commit -m "整合維護計劃功能至選單與 DI"
```

---

### Task 12: 執行全部測試與最終驗證

**Files:** None (verification only)

- [ ] **Step 1: 執行全部測試**

Run: `dotnet test`
Expected: All tests PASS

- [ ] **Step 2: 建置全方案**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings (or existing warnings only)

- [ ] **Step 3: 執行應用程式手動驗證**

Run: `dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj`

驗證項目：
- 「工具」選單出現「資料庫維護計劃」
- 未連線時選單項目為停用
- 連線後可開啟管理視窗
- 管理視窗載入 Job 清單
- 「新增計劃」按鈕開啟精靈
- 精靈三步驟切換正常
- 前置檢查結果顯示正確

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "完成資料庫維護計劃功能"
```
