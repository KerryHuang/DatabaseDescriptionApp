namespace TableSpec.Domain.Entities;

/// <summary>
/// 趨勢資料點實體
/// </summary>
public class TrendDataPoint
{
    /// <summary>
    /// 檢查時間
    /// </summary>
    public required DateTime CheckTime { get; init; }

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
}
