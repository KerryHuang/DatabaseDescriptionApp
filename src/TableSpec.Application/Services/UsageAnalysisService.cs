using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 使用狀態分析服務實作
/// </summary>
public class UsageAnalysisService : IUsageAnalysisService
{
    private readonly IUsageAnalysisRepository _repository;

    public UsageAnalysisService(IUsageAnalysisRepository repository)
    {
        _repository = repository;
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
    public Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct)
    {
        // Task 6 實作
        throw new NotImplementedException();
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
