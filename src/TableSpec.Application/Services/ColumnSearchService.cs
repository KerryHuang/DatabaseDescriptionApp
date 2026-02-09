using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 欄位搜尋服務實作（支援多資料庫並行搜尋）
/// </summary>
public class ColumnSearchService : IColumnSearchService
{
    private readonly IConnectionManager _connectionManager;
    private readonly Func<string?, ISqlQueryRepository> _repositoryFactory;

    public ColumnSearchService(
        IConnectionManager connectionManager,
        Func<string?, ISqlQueryRepository> repositoryFactory)
    {
        _connectionManager = connectionManager;
        _repositoryFactory = repositoryFactory;
    }

    /// <inheritdoc/>
    public async Task<List<ColumnSearchResult>> SearchColumnsMultiAsync(
        string columnName,
        IReadOnlyList<Guid> profileIds,
        bool exactMatch = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var tasks = profileIds.Select(async profileId =>
        {
            ct.ThrowIfCancellationRequested();

            var connStr = _connectionManager.GetConnectionString(profileId);
            if (string.IsNullOrEmpty(connStr))
                return new List<ColumnSearchResult>();

            var profile = _connectionManager.GetAllProfiles()
                .FirstOrDefault(p => p.Id == profileId);
            var databaseName = profile?.Database ?? _connectionManager.GetProfileName(profileId);

            progress?.Report($"搜尋中：{databaseName}...");

            try
            {
                var repo = _repositoryFactory(connStr);
                var results = await repo.SearchColumnsAsync(columnName, exactMatch, ct);

                foreach (var result in results)
                {
                    result.DatabaseName = databaseName;
                }

                return results;
            }
            catch (Exception ex)
            {
                progress?.Report($"搜尋 {databaseName} 失敗：{ex.Message}");
                return new List<ColumnSearchResult>();
            }
        }).ToList();

        var batchResults = await Task.WhenAll(tasks);

        var allResults = new List<ColumnSearchResult>();
        foreach (var batch in batchResults)
        {
            allResults.AddRange(batch);
        }

        return allResults
            .OrderBy(r => r.DatabaseName)
            .ThenBy(r => r.ObjectType)
            .ThenBy(r => r.SchemaName)
            .ThenBy(r => r.ObjectName)
            .ThenBy(r => r.ColumnName)
            .ToList();
    }
}
