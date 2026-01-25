namespace TableSpec.Domain.Entities;

/// <summary>
/// 即時健康指標實體
/// </summary>
public class HealthMetric
{
    /// <summary>
    /// 檢查類型
    /// </summary>
    public required string CheckType { get; init; }

    /// <summary>
    /// 指標名稱
    /// </summary>
    public required string MetricName { get; init; }

    /// <summary>
    /// 指標值
    /// </summary>
    public decimal? MetricValue { get; init; }

    /// <summary>
    /// 閾值
    /// </summary>
    public decimal? ThresholdValue { get; init; }

    /// <summary>
    /// 狀態
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 檢查時間
    /// </summary>
    public DateTime CheckTime { get; init; }

    /// <summary>
    /// 告警訊息
    /// </summary>
    public string? AlertMessage { get; init; }

    /// <summary>
    /// 狀態優先級（用於排序）
    /// </summary>
    public int StatusPriority { get; init; }
}
