using System;
using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities;

/// <summary>
/// 維護計劃設定參數
/// </summary>
public class MaintenancePlanConfig
{
    public required string DatabaseName { get; init; }
    public required string BackupPath { get; init; }
    public required string RestorePath { get; init; }
    public required string TestDatabaseName { get; init; }
    public required string LoginName { get; init; }
    public required string LoginPassword { get; init; }
    public required int BackupTime { get; init; }
    public required int RestoreTime { get; init; }
    public required IReadOnlyList<MaintenancePlanStep> SelectedSteps { get; init; }
    public int RetentionDays { get; init; } = 7;
    public string RecoveryModel { get; init; } = "FULL";
    /// <summary>資料檔 autogrowth 固定 MB</summary>
    public int AutoGrowthDataMB { get; init; } = 256;
    /// <summary>記錄檔 autogrowth 固定 MB</summary>
    public int AutoGrowthLogMB { get; init; } = 128;
    /// <summary>預擴資料檔的緩衝 GB（目前大小 + 此值，再湊整到 GB）</summary>
    public int PreExpandBufferGB { get; init; } = 5;
    /// <summary>CheckDB 排程小時（0-23 整點）</summary>
    public int CheckDbHour { get; init; } = 3;
    /// <summary>CheckDB 排程星期</summary>
    public DayOfWeek CheckDbDayOfWeek { get; init; } = DayOfWeek.Sunday;

    public bool IsBackupPathValid => !string.IsNullOrWhiteSpace(BackupPath) && (BackupPath.EndsWith('/') || BackupPath.EndsWith('\\'));
    public bool IsRestorePathValid => !string.IsNullOrWhiteSpace(RestorePath) && (RestorePath.EndsWith('/') || RestorePath.EndsWith('\\'));
}
