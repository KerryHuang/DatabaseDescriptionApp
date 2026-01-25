namespace TableSpec.Domain.Entities;

/// <summary>
/// 健康狀態摘要實體
/// </summary>
public class HealthStatusSummary
{
    /// <summary>
    /// 檢查類型
    /// </summary>
    public required string CheckType { get; init; }

    /// <summary>
    /// 檢查項目總數
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// 正常數量
    /// </summary>
    public int OkCount { get; init; }

    /// <summary>
    /// 警告數量
    /// </summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// 危險數量
    /// </summary>
    public int CriticalCount { get; init; }

    /// <summary>
    /// 最後檢查時間
    /// </summary>
    public DateTime? LastCheckTime { get; init; }

    /// <summary>
    /// 整體狀態
    /// </summary>
    public string OverallStatus => CriticalCount > 0 ? "CRITICAL" : WarningCount > 0 ? "WARNING" : "OK";
}
