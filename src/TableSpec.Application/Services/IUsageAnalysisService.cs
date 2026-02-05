using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 使用狀態分析服務介面
/// </summary>
public interface IUsageAnalysisService
{
    /// <summary>單環境掃描</summary>
    Task<UsageScanResult> ScanAsync(int years, IProgress<string>? progress, CancellationToken ct);

    /// <summary>多環境比對</summary>
    Task<UsageComparison> CompareAsync(
        Guid baseProfileId, List<Guid> targetProfileIds,
        int years, IProgress<string>? progress, CancellationToken ct);

    /// <summary>刪除資料表</summary>
    Task DeleteTableAsync(string schemaName, string tableName, CancellationToken ct);

    /// <summary>刪除欄位</summary>
    Task DeleteColumnAsync(string schemaName, string tableName, string columnName, CancellationToken ct);

    /// <summary>產生 DROP TABLE 語法</summary>
    string GenerateDropTableSql(string schemaName, string tableName);

    /// <summary>產生 DROP COLUMN 語法</summary>
    string GenerateDropColumnSql(string schemaName, string tableName, string columnName);
}
