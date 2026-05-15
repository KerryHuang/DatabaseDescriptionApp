using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// 效能診斷服務實作
/// </summary>
public class PerformanceDiagnosticsService : IPerformanceDiagnosticsService
{
    private readonly IPerformanceDiagnosticsRepository _repository;

    public PerformanceDiagnosticsService(IPerformanceDiagnosticsRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WaitStatistic>> GetWaitStatisticsAsync(int top = 20, decimal minPercentage = 0, CancellationToken ct = default)
        => _repository.GetWaitStatisticsAsync(top, minPercentage, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveQueriesAsync(int top = 5, CancellationToken ct = default)
        => _repository.GetTopExpensiveQueriesAsync(top, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveProceduresAsync(int top = 5, CancellationToken ct = default)
        => _repository.GetTopExpensiveProceduresAsync(top, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExpensiveQuery>> GetTopCpuIntensiveQueriesAsync(int top = 5, CancellationToken ct = default)
        => _repository.GetTopCpuIntensiveQueriesAsync(top, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<StatisticsInfo>> GetStatisticsInfoAsync(CancellationToken ct = default)
        => _repository.GetStatisticsInfoAsync(ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ErrorLogEntry>> GetErrorLogAsync(int days = 7, CancellationToken ct = default)
        => _repository.GetErrorLogAsync(days, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<IndexStatus>> GetIndexStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => _repository.GetIndexStatusAsync(progress, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MissingIndex>> GetMissingIndexesAsync(CancellationToken ct = default)
        => _repository.GetMissingIndexesAsync(ct);

    /// <inheritdoc/>
    public Task ExecuteCreateIndexAsync(string createIndexStatement, CancellationToken ct = default)
        => _repository.ExecuteCreateIndexAsync(createIndexStatement, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<UnusedIndex>> GetUnusedIndexesAsync(CancellationToken ct = default)
        => _repository.GetUnusedIndexesAsync(ct);

    /// <inheritdoc/>
    public Task ExecuteDropIndexAsync(string dropIndexStatement, string databaseName, CancellationToken ct = default)
        => _repository.ExecuteDropIndexAsync(dropIndexStatement, databaseName, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IntegrityCheckStatus>> GetIntegrityCheckStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLastCheckDbAsync(progress, ct);
        return rows.Select(r =>
        {
            // dbi_dbccLastKnownGood 為 SQL Server 本地時間，故使用 DateTime.Now（本地時間）比對，避免跨時區誤差
            int? days = r.LastKnownGood.HasValue
                ? Math.Max(0, (int)(DateTime.Now.Date - r.LastKnownGood.Value.Date).TotalDays)
                : (int?)null;
            return new IntegrityCheckStatus
            {
                DatabaseName = r.DatabaseName,
                LastKnownGood = r.LastKnownGood,
                DaysSince = days,
                Health = ClassifyHealth(days)
            };
        }).ToList();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SuspectPage>> GetSuspectPagesAsync(CancellationToken ct = default)
        => _repository.GetSuspectPagesAsync(ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<CheckDbJobHistory>> GetCheckDbJobHistoryAsync(int top = 50, CancellationToken ct = default)
        => _repository.GetCheckDbJobHistoryAsync(top, ct);

    private static IntegrityHealth ClassifyHealth(int? days) => days switch
    {
        null => IntegrityHealth.Critical,
        < 14 => IntegrityHealth.Healthy,
        < 30 => IntegrityHealth.Warning,
        _ => IntegrityHealth.Critical
    };
}
