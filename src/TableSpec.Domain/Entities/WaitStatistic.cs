namespace TableSpec.Domain.Entities;

/// <summary>
/// 等候事件統計實體
/// </summary>
public class WaitStatistic
{
    /// <summary>
    /// 等候類型
    /// </summary>
    public required string WaitType { get; init; }

    /// <summary>
    /// 等候時間（秒）
    /// </summary>
    public decimal WaitTimeSeconds { get; init; }

    /// <summary>
    /// 等候資源時間（秒）
    /// </summary>
    public decimal ResourceWaitSeconds { get; init; }

    /// <summary>
    /// 執行緒獲得資源進入 runnable queue 到開始執行的時間（秒）
    /// </summary>
    public decimal SignalWaitSeconds { get; init; }

    /// <summary>
    /// 等候次數
    /// </summary>
    public long WaitCount { get; init; }

    /// <summary>
    /// 等候時間所占全部等待百分率
    /// </summary>
    public decimal Percentage { get; init; }

    /// <summary>
    /// 平均等候時間（秒）
    /// </summary>
    public decimal AvgWaitTimeSeconds { get; init; }

    /// <summary>
    /// 平均等候資源時間（秒）
    /// </summary>
    public decimal AvgResourceWaitSeconds { get; init; }

    /// <summary>
    /// 平均執行緒信號等候時間（秒）
    /// </summary>
    public decimal AvgSignalWaitSeconds { get; init; }
}
