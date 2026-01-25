namespace TableSpec.Domain.Entities;

/// <summary>
/// 耗時查詢實體
/// </summary>
public class ExpensiveQuery
{
    /// <summary>
    /// 查詢類型（Query/Procedure）
    /// </summary>
    public required string QueryType { get; init; }

    /// <summary>
    /// 查詢文字
    /// </summary>
    public required string QueryText { get; init; }

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public string? DatabaseName { get; init; }

    /// <summary>
    /// 物件名稱（預存程序名稱）
    /// </summary>
    public string? ObjectName { get; init; }

    /// <summary>
    /// 執行次數
    /// </summary>
    public long ExecutionCount { get; init; }

    /// <summary>
    /// 總經過時間（毫秒）
    /// </summary>
    public long TotalElapsedTimeMs { get; init; }

    /// <summary>
    /// 最大經過時間（毫秒）
    /// </summary>
    public long MaxElapsedTimeMs { get; init; }

    /// <summary>
    /// 總 CPU 使用時間（毫秒）
    /// </summary>
    public long TotalWorkerTimeMs { get; init; }

    /// <summary>
    /// 平均 CPU 使用時間（毫秒）
    /// </summary>
    public decimal AvgWorkerTimeMs { get; init; }

    /// <summary>
    /// 執行計畫（XML）
    /// </summary>
    public string? QueryPlan { get; init; }
}
