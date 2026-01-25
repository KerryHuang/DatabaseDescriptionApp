namespace TableSpec.Domain.Entities;

/// <summary>
/// 健康監控記錄實體
/// </summary>
public class HealthLogEntry
{
    /// <summary>
    /// 記錄 ID
    /// </summary>
    public required int LogId { get; init; }

    /// <summary>
    /// 檢查時間
    /// </summary>
    public required DateTime CheckTime { get; init; }

    /// <summary>
    /// 檢查類型（CPU、Memory、Disk 等）
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
    /// 狀態（OK、WARNING、CRITICAL）
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 告警訊息
    /// </summary>
    public string? AlertMessage { get; init; }

    /// <summary>
    /// 伺服器名稱
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public string? DatabaseName { get; init; }

    /// <summary>
    /// 額外資訊
    /// </summary>
    public string? AdditionalInfo { get; init; }
}
