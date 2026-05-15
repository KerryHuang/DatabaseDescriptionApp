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
    public Task<string> GetRecoveryModelAsync(string databaseName, CancellationToken ct = default)
        => _dbInfoRepo.GetRecoveryModelAsync(databaseName, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StepCheckResult>> CheckStepsAsync(MaintenancePlanConfig config, CancellationToken ct = default)
    {
        var results = new List<StepCheckResult>();
        string? recoveryModel = null;

        async Task<string> GetRecoveryModel() =>
            recoveryModel ??= await _dbInfoRepo.GetRecoveryModelAsync(config.DatabaseName, ct);

        foreach (var step in config.SelectedSteps)
        {
            var result = step switch
            {
                MaintenancePlanStep.SetCompatibilityLevel => await CheckCompatibilityLevelAsync(config, ct),
                MaintenancePlanStep.SetRecoveryModel => await CheckRecoveryModelAsync(config, await GetRecoveryModel()),
                MaintenancePlanStep.RenameLogicalFiles => await CheckLogicalFilesAsync(config, ct),
                MaintenancePlanStep.CreateLoginAndUser => await CheckLoginAndUserAsync(config, ct),
                MaintenancePlanStep.AddToDbOwner => await CheckDbOwnerAsync(config, ct),
                MaintenancePlanStep.CreateBackupJob => await CheckJobAsync(config, MaintenancePlanStep.CreateBackupJob, $"{config.DatabaseName}_{await GetRecoveryModel()}Backup", ct),
                MaintenancePlanStep.CreateRestoreJob => await CheckJobAsync(config, MaintenancePlanStep.CreateRestoreJob, $"{config.DatabaseName}_FullRestore", ct),
                MaintenancePlanStep.AdjustAutoGrowth => await CheckAutoGrowthAsync(config, ct),
                MaintenancePlanStep.PreExpandDataFile => await CheckPreExpandAsync(config, ct),
                MaintenancePlanStep.CreateCheckDbJob => await CheckJobAsync(config, MaintenancePlanStep.CreateCheckDbJob, $"{config.DatabaseName}_CheckDb", ct),
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

        // 獨立步驟：重新命名邏輯檔名（ALTER DATABASE MODIFY FILE 不能在交易中執行）
        var renameStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.RenameLogicalFiles && r.SelectedAction != "跳過");
        if (renameStep is not null)
        {
            progress?.Report("正在重新命名邏輯檔名...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.RenameLogicalFiles, config, renameStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("邏輯檔名更新完成。");
        }

        ct.ThrowIfCancellationRequested();

        // 交易群組：資料庫設定步驟
        var dbSteps = checkResults.Where(r =>
            r.Step is MaintenancePlanStep.SetRecoveryModel
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

        // 交易群組：CheckDB Job
        var checkDbStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateCheckDbJob && r.SelectedAction != "跳過");
        if (checkDbStep != null)
        {
            progress?.Report("正在建立完整性檢查排程...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateCheckDbJob, config, checkDbStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("完整性檢查排程建立完成。");
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
    public async Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, CancellationToken ct = default)
    {
        var baseSql = _sqlGenerator.GenerateFullSql(config, checkResults);

        var autoGrowthActive = checkResults.Any(r => r.Step == MaintenancePlanStep.AdjustAutoGrowth && r.SelectedAction != "跳過");
        var preExpandActive = checkResults.Any(r => r.Step == MaintenancePlanStep.PreExpandDataFile && r.SelectedAction != "跳過");

        if (!autoGrowthActive && !preExpandActive)
            return baseSql;

        var files = await _dbInfoRepo.GetDatabaseFilesAsync(config.DatabaseName, ct);
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

    private static Task<StepCheckResult> CheckRecoveryModelAsync(MaintenancePlanConfig config, string model)
    {
        var alreadySimple = model == "SIMPLE";
        return Task.FromResult(new StepCheckResult
        {
            Step = MaintenancePlanStep.SetRecoveryModel,
            AlreadyExists = alreadySimple,
            CurrentStatus = $"目前復原模式：{model}",
            AvailableActions = alreadySimple ? ["跳過"] : ["執行", "跳過"]
        });
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

        // 計算每個資料檔的實際擴增 MB（與 SqlGenerator 邏輯一致：目前大小向上湊整 GB + bufferGB）
        var bufferGB = config.PreExpandBufferGB;
        DatabaseFileInfo? insufficient = null;
        foreach (var f in dataFiles)
        {
            if (!f.VolumeFreeGB.HasValue) continue;
            var currentGB = (int)Math.Ceiling(f.SizeMB / 1024.0);
            var targetMB = (currentGB + bufferGB) * 1024;
            var growthMB = targetMB - f.SizeMB;
            var requiredGB = growthMB * 1.5 / 1024.0;
            if (f.VolumeFreeGB.Value < requiredGB)
            {
                insufficient = f;
                break;
            }
        }
        if (insufficient is not null)
        {
            var iCurrentGB = (int)Math.Ceiling(insufficient.SizeMB / 1024.0);
            var iTargetMB = (iCurrentGB + bufferGB) * 1024;
            var iGrowthMB = iTargetMB - insufficient.SizeMB;
            var iRequired = Math.Round(iGrowthMB * 1.5 / 1024.0, 1);
            return new StepCheckResult
            {
                Step = MaintenancePlanStep.PreExpandDataFile,
                AlreadyExists = true,
                CurrentStatus = $"磁碟空間不足，跳過（{insufficient.VolumeMountPoint} free {insufficient.VolumeFreeGB} GB < 需要 {iRequired} GB）",
                AvailableActions = ["跳過"]
            };
        }

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
}
