using Specurai.Application.Models;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// 維護計劃服務實作
/// </summary>
public class MaintenancePlanService : IMaintenancePlanService
{
    private readonly IDatabaseInfoRepository _dbInfoRepo;
    private readonly IAgentJobRepository _agentJobRepo;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;

    public MaintenancePlanService(
        IDatabaseInfoRepository dbInfoRepo,
        IAgentJobRepository agentJobRepo,
        IMaintenancePlanSqlGenerator sqlGenerator)
    {
        _dbInfoRepo = dbInfoRepo;
        _agentJobRepo = agentJobRepo;
        _sqlGenerator = sqlGenerator;
    }

    /// <inheritdoc />
    public async Task<(bool IsReady, string? ErrorMessage)> CheckPrerequisitesAsync(CancellationToken ct = default)
    {
        // 檢查是否為 Azure SQL Database
        if (await _dbInfoRepo.IsAzureSqlDatabaseAsync(ct))
            return (false, "Azure SQL Database 不支援 SQL Agent，無法執行維護計劃。");

        // 檢查 Agent 是否執行中
        if (!await _agentJobRepo.IsAgentRunningAsync(ct))
            return (false, "SQL Server Agent 服務未執行，請先啟動 Agent 服務。");

        // 檢查權限
        if (!await _agentJobRepo.HasAgentPermissionAsync(ct))
            return (false, "目前使用者沒有 SQL Agent 操作權限，請使用具有足夠權限的帳號連線。");

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StepCheckResult>> CheckStepsAsync(MaintenancePlanConfig config, CancellationToken ct = default)
    {
        var results = new List<StepCheckResult>();

        foreach (var step in config.SelectedSteps)
        {
            var result = step switch
            {
                MaintenancePlanStep.SetCompatibilityLevel => await CheckCompatibilityLevelAsync(config, ct),
                MaintenancePlanStep.SetRecoveryModel => await CheckRecoveryModelAsync(config, ct),
                MaintenancePlanStep.RenameLogicalFiles => await CheckLogicalFilesAsync(config, ct),
                MaintenancePlanStep.CreateLoginAndUser => await CheckLoginAndUserAsync(config, ct),
                MaintenancePlanStep.AddToDbOwner => await CheckDbOwnerAsync(config, ct),
                MaintenancePlanStep.CreateBackupJob => await CheckJobAsync(config, MaintenancePlanStep.CreateBackupJob, $"{config.DatabaseName}_FullBackup", ct),
                MaintenancePlanStep.CreateRestoreJob => await CheckJobAsync(config, MaintenancePlanStep.CreateRestoreJob, $"{config.DatabaseName}_FullRestore", ct),
                _ => throw new ArgumentOutOfRangeException(nameof(step), step, "未知的維護計劃步驟")
            };
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task ExecutePlanAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // 獨立步驟：更新相容性層級（ALTER DATABASE 不能在交易中執行）
        var compatStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.SetCompatibilityLevel && r.SelectedAction != "跳過");
        if (compatStep != null)
        {
            progress?.Report("正在更新相容性層級...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.SetCompatibilityLevel, config, compatStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("相容性層級更新完成。");
        }

        ct.ThrowIfCancellationRequested();

        // 交易群組：資料庫設定步驟
        var dbSteps = checkResults.Where(r =>
            r.Step is MaintenancePlanStep.SetRecoveryModel
                or MaintenancePlanStep.RenameLogicalFiles
                or MaintenancePlanStep.CreateLoginAndUser
                or MaintenancePlanStep.AddToDbOwner
            && r.SelectedAction != "跳過").ToList();

        if (dbSteps.Count > 0)
        {
            progress?.Report("正在執行資料庫設定步驟...");
            var sql = string.Join("\n", dbSteps.Select(r => _sqlGenerator.GenerateStepSql(r.Step, config, r.SelectedAction)));
            await _dbInfoRepo.ExecuteSqlWithTransactionAsync(sql, ct);
            progress?.Report("資料庫設定步驟完成。");
        }

        ct.ThrowIfCancellationRequested();

        // 交易群組 2：備份 Job
        var backupStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateBackupJob && r.SelectedAction != "跳過");
        if (backupStep != null)
        {
            progress?.Report("正在建立備份排程...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, backupStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("備份排程建立完成。");
        }

        ct.ThrowIfCancellationRequested();

        // 交易群組 3：還原 Job
        var restoreStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob && r.SelectedAction != "跳過");
        if (restoreStep != null)
        {
            progress?.Report("正在建立還原排程...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, config, restoreStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("還原排程建立完成。");
        }

        progress?.Report("維護計劃執行完成。");
    }

    /// <inheritdoc />
    public Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults)
    {
        var sql = _sqlGenerator.GenerateFullSql(config, checkResults);
        return Task.FromResult(sql);
    }

    private async Task<StepCheckResult> CheckCompatibilityLevelAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var currentLevel = await _dbInfoRepo.GetCompatibilityLevelAsync(config.DatabaseName, ct);
        var serverLevel = await _dbInfoRepo.GetServerCompatibilityLevelAsync(ct);
        var isMatch = currentLevel >= serverLevel;
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.SetCompatibilityLevel,
            AlreadyExists = isMatch,
            CurrentStatus = isMatch
                ? $"相容性層級已為最新（{currentLevel}）"
                : $"目前：{currentLevel}，伺服器：{serverLevel}",
            AvailableActions = isMatch ? ["跳過"] : ["執行", "跳過"]
        };
    }

    private async Task<StepCheckResult> CheckRecoveryModelAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var model = await _dbInfoRepo.GetRecoveryModelAsync(config.DatabaseName, ct);
        var alreadySimple = model == "SIMPLE";
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.SetRecoveryModel,
            AlreadyExists = alreadySimple,
            CurrentStatus = $"目前復原模式：{model}",
            AvailableActions = alreadySimple ? ["跳過"] : ["執行", "跳過"]
        };
    }

    private async Task<StepCheckResult> CheckLogicalFilesAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var files = await _dbInfoRepo.GetLogicalFileNamesAsync(config.DatabaseName, ct);
        // 邏輯檔名應以資料庫名稱為前綴（如 DB_Data、DB_Log），不是的話就需要重命名
        var allCorrect = files.Count > 0 && files.All(f =>
            f.LogicalName.StartsWith(config.DatabaseName, StringComparison.OrdinalIgnoreCase));
        var incorrectFiles = files.Where(f =>
            !f.LogicalName.StartsWith(config.DatabaseName, StringComparison.OrdinalIgnoreCase)).ToList();
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.RenameLogicalFiles,
            AlreadyExists = allCorrect,
            CurrentStatus = allCorrect
                ? $"邏輯檔名已正確（{string.Join(", ", files.Select(f => f.LogicalName))}）"
                : $"發現不符合的邏輯檔名：{string.Join(", ", incorrectFiles.Select(f => f.LogicalName))}",
            AvailableActions = allCorrect ? ["跳過"] : ["執行", "跳過"]
        };
    }

    private async Task<StepCheckResult> CheckLoginAndUserAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var loginExists = await _dbInfoRepo.LoginExistsAsync(config.LoginName, ct);
        var userExists = await _dbInfoRepo.DatabaseUserExistsAsync(config.DatabaseName, config.LoginName, ct);
        var bothExist = loginExists && userExists;
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.CreateLoginAndUser,
            AlreadyExists = bothExist,
            CurrentStatus = bothExist ? "登入帳號與使用者已存在" : $"登入帳號：{(loginExists ? "存在" : "不存在")}，資料庫使用者：{(userExists ? "存在" : "不存在")}",
            AvailableActions = bothExist ? ["跳過"] : ["建立", "跳過"]
        };
    }

    private async Task<StepCheckResult> CheckDbOwnerAsync(MaintenancePlanConfig config, CancellationToken ct)
    {
        var isMember = await _dbInfoRepo.IsDbOwnerMemberAsync(config.DatabaseName, config.LoginName, ct);
        return new StepCheckResult
        {
            Step = MaintenancePlanStep.AddToDbOwner,
            AlreadyExists = isMember,
            CurrentStatus = isMember ? "已為 db_owner 成員" : "尚未加入 db_owner",
            AvailableActions = isMember ? ["跳過"] : ["執行", "跳過"]
        };
    }

    private async Task<StepCheckResult> CheckJobAsync(MaintenancePlanConfig config, MaintenancePlanStep step, string jobName, CancellationToken ct)
    {
        var exists = await _dbInfoRepo.AgentJobExistsAsync(jobName, ct);
        return new StepCheckResult
        {
            Step = step,
            AlreadyExists = exists,
            CurrentStatus = exists ? $"Job [{jobName}] 已存在" : $"Job [{jobName}] 不存在",
            AvailableActions = exists ? ["跳過", "重建"] : ["建立", "跳過"]
        };
    }
}
