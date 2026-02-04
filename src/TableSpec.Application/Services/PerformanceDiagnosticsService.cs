using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

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
}
