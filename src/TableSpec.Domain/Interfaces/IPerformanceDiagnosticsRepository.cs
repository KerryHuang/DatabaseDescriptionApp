using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 效能診斷 Repository 介面
/// </summary>
public interface IPerformanceDiagnosticsRepository
{
    /// <summary>
    /// 取得等候事件統計
    /// </summary>
    /// <param name="top">顯示前 N 筆（預設 20）</param>
    /// <param name="minPercentage">最小百分比篩選（預設 0）</param>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<WaitStatistic>> GetWaitStatisticsAsync(int top = 20, decimal minPercentage = 0, CancellationToken ct = default);

    /// <summary>
    /// 取得最耗時的查詢
    /// </summary>
    Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveQueriesAsync(int top = 5, CancellationToken ct = default);

    /// <summary>
    /// 取得最耗時的預存程序
    /// </summary>
    Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveProceduresAsync(int top = 5, CancellationToken ct = default);

    /// <summary>
    /// 取得最耗 CPU 的查詢（含執行計畫）
    /// </summary>
    Task<IReadOnlyList<ExpensiveQuery>> GetTopCpuIntensiveQueriesAsync(int top = 5, CancellationToken ct = default);

    /// <summary>
    /// 取得統計資訊
    /// </summary>
    Task<IReadOnlyList<StatisticsInfo>> GetStatisticsInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得錯誤記錄
    /// </summary>
    /// <param name="days">查詢天數（預設 7 天）</param>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<ErrorLogEntry>> GetErrorLogAsync(int days = 7, CancellationToken ct = default);

    /// <summary>
    /// 取得索引狀態（耗時較長，需手動觸發）
    /// </summary>
    Task<IReadOnlyList<IndexStatus>> GetIndexStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 取得缺少索引建議（耗時較長，需手動觸發）
    /// </summary>
    Task<IReadOnlyList<MissingIndex>> GetMissingIndexesAsync(CancellationToken ct = default);
}
