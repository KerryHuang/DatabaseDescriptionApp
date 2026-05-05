namespace Specurai.Domain.Enums;

/// <summary>
/// Migration 執行日誌狀態
/// </summary>
public enum MigrationLogStatus
{
    /// <summary>
    /// 執行成功
    /// </summary>
    Success = 0,

    /// <summary>
    /// 使用者略過（未勾選）
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// 執行失敗
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 高風險，不執行
    /// </summary>
    HighRisk = 3,

    /// <summary>
    /// 因前一條語句失敗而自動回滾，本身未執行
    /// </summary>
    RolledBack = 4
}
