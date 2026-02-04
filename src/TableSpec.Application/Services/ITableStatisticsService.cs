using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 資料表統計服務介面
/// </summary>
public interface ITableStatisticsService
{
    /// <summary>
    /// 取得所有資料表的統計資訊
    /// </summary>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<TableStatisticsInfo>> GetAllTableStatisticsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// 取得單一資料表的精確資料列數
    /// </summary>
    /// <param name="schemaName">結構描述名稱</param>
    /// <param name="tableName">資料表名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<long> GetExactRowCountAsync(
        string schemaName,
        string tableName,
        CancellationToken ct = default);
}
