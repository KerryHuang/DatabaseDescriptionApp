using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 使用狀態分析服務實作
/// </summary>
public class UsageAnalysisService : IUsageAnalysisService
{
    private readonly IUsageAnalysisRepository _repository;
    private readonly IConnectionManager? _connectionManager;
    private readonly Func<string?, IUsageAnalysisRepository>? _repositoryFactory;

    /// <summary>
    /// 單環境建構函式（用於掃描和刪除操作）
    /// </summary>
    public UsageAnalysisService(IUsageAnalysisRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// 多環境建構函式（用於跨環境比對）
    /// </summary>
    /// <param name="repository">預設 Repository</param>
    /// <param name="connectionManager">連線管理服務</param>
    /// <param name="repositoryFactory">Repository 工廠，依連線字串建立對應 Repository</param>
    public UsageAnalysisService(
        IUsageAnalysisRepository repository,
        IConnectionManager connectionManager,
        Func<string?, IUsageAnalysisRepository> repositoryFactory)
    {
        _repository = repository;
        _connectionManager = connectionManager;
        _repositoryFactory = repositoryFactory;
    }

    /// <inheritdoc/>
    public async Task<UsageScanResult> ScanAsync(
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("正在取得資料表使用狀態...");
        var tables = await _repository.GetTableUsageAsync(ct);

        progress?.Report("正在分析欄位使用狀態...");
        var columns = await _repository.GetColumnUsageStatusAsync(progress, ct);

        return new UsageScanResult
        {
            Tables = tables,
            Columns = columns,
            YearsThreshold = years
        };
    }

    /// <inheritdoc/>
    public async Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        if (_connectionManager == null || _repositoryFactory == null)
            throw new InvalidOperationException("多環境比對需要 ConnectionManager 和 Repository 工廠");

        var allEnvironments = new List<(string Name, IUsageAnalysisRepository Repo)>();

        // 基準環境
        var baseConnStr = _connectionManager.GetConnectionString(baseProfileId);
        var baseName = _connectionManager.GetProfileName(baseProfileId);
        var baseRepo = _repositoryFactory(baseConnStr);
        allEnvironments.Add((baseName, baseRepo));

        // 目標環境
        foreach (var targetId in targetProfileIds)
        {
            ct.ThrowIfCancellationRequested();
            var connStr = _connectionManager.GetConnectionString(targetId);
            var name = _connectionManager.GetProfileName(targetId);
            var repo = _repositoryFactory(connStr);
            allEnvironments.Add((name, repo));
        }

        // 收集各環境資料
        var envData = new Dictionary<string, (IReadOnlyList<TableUsageInfo> Tables, IReadOnlyList<ColumnUsageStatus> Columns)>();
        foreach (var (name, repo) in allEnvironments)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"正在掃描環境：{name}...");
            var tables = await repo.GetTableUsageAsync(ct);
            var columns = await repo.GetColumnUsageStatusAsync(progress, ct);
            envData[name] = (tables, columns);
        }

        // 合併表資料
        var allTableKeys = envData.Values
            .SelectMany(d => d.Tables.Select(t => (t.SchemaName, t.TableName)))
            .Distinct()
            .OrderBy(k => k.SchemaName).ThenBy(k => k.TableName)
            .ToList();

        var tableRows = allTableKeys.Select(key => new TableUsageComparisonRow
        {
            SchemaName = key.SchemaName,
            TableName = key.TableName,
            EnvironmentStatus = allEnvironments.ToDictionary(
                e => e.Name,
                e => envData[e.Name].Tables.FirstOrDefault(
                    t => t.SchemaName == key.SchemaName && t.TableName == key.TableName))
        }).ToList();

        // 合併欄位資料
        var allColumnKeys = envData.Values
            .SelectMany(d => d.Columns.Select(c => (c.SchemaName, c.TableName, c.ColumnName)))
            .Distinct()
            .OrderBy(k => k.SchemaName).ThenBy(k => k.TableName).ThenBy(k => k.ColumnName)
            .ToList();

        var columnRows = allColumnKeys.Select(key => new ColumnUsageComparisonRow
        {
            SchemaName = key.SchemaName,
            TableName = key.TableName,
            ColumnName = key.ColumnName,
            EnvironmentStatus = allEnvironments.ToDictionary(
                e => e.Name,
                e => envData[e.Name].Columns.FirstOrDefault(
                    c => c.SchemaName == key.SchemaName && c.TableName == key.TableName && c.ColumnName == key.ColumnName))
        }).ToList();

        return new UsageComparison
        {
            BaseEnvironment = baseName,
            TargetEnvironments = targetProfileIds.Select(id => _connectionManager.GetProfileName(id)).ToList(),
            TableRows = tableRows,
            ColumnRows = columnRows
        };
    }

    /// <inheritdoc/>
    public async Task DeleteTableAsync(string schemaName, string tableName, CancellationToken ct)
    {
        var sql = GenerateDropTableSql(schemaName, tableName);
        await _repository.ExecuteSqlAsync(sql, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteColumnAsync(
        string schemaName, string tableName, string columnName, CancellationToken ct)
    {
        var sql = GenerateDropColumnSql(schemaName, tableName, columnName);
        await _repository.ExecuteSqlAsync(sql, ct);
    }

    /// <inheritdoc/>
    public string GenerateDropTableSql(string schemaName, string tableName)
        => $"DROP TABLE [{schemaName}].[{tableName}]";

    /// <inheritdoc/>
    public string GenerateDropColumnSql(string schemaName, string tableName, string columnName)
        => $"ALTER TABLE [{schemaName}].[{tableName}] DROP COLUMN [{columnName}]";
}
