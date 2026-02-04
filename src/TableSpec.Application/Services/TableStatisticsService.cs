using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 資料表統計服務實作
/// </summary>
public class TableStatisticsService : ITableStatisticsService
{
    private readonly ITableStatisticsRepository _repository;

    public TableStatisticsService(ITableStatisticsRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TableStatisticsInfo>> GetAllTableStatisticsAsync(
        CancellationToken ct = default)
        => _repository.GetAllTableStatisticsAsync(ct);

    /// <inheritdoc/>
    public Task<long> GetExactRowCountAsync(
        string schemaName, string tableName,
        CancellationToken ct = default)
        => _repository.GetExactRowCountAsync(schemaName, tableName, ct);
}
