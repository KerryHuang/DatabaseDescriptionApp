using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 使用狀態分析 Repository 介面
/// </summary>
public interface IUsageAnalysisRepository
{
    /// <summary>
    /// 取得資料表使用狀態
    /// </summary>
    Task<IReadOnlyList<TableUsageInfo>> GetTableUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得欄位使用狀態（含查詢計畫引用、NULL/預設值檢查）
    /// </summary>
    Task<IReadOnlyList<ColumnUsageStatus>> GetColumnUsageStatusAsync(
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 執行 SQL 語法（用於 DROP TABLE / DROP COLUMN）
    /// </summary>
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);
}
