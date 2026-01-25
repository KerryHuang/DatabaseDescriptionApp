using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 效能診斷服務介面
/// </summary>
public interface IPerformanceDiagnosticsService
{
    /// <summary>
    /// 取得等候事件統計
    /// </summary>
    Task<IReadOnlyList<WaitStatistic>> GetWaitStatisticsAsync(CancellationToken ct = default);

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
    Task<IReadOnlyList<ErrorLogEntry>> GetErrorLogAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得索引狀態（耗時較長，需手動觸發）
    /// </summary>
    Task<IReadOnlyList<IndexStatus>> GetIndexStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 取得缺少索引建議
    /// </summary>
    Task<IReadOnlyList<MissingIndex>> GetMissingIndexesAsync(CancellationToken ct = default);
}
