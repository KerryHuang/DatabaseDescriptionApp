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

    public bool IsBackupPathValid => !string.IsNullOrWhiteSpace(BackupPath) && (BackupPath.EndsWith('/') || BackupPath.EndsWith('\\'));
    public bool IsRestorePathValid => !string.IsNullOrWhiteSpace(RestorePath) && (RestorePath.EndsWith('/') || RestorePath.EndsWith('\\'));
}
