using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 欄位使用統計 Repository
/// </summary>
public interface IColumnUsageRepository
{
    /// <summary>
    /// 取得所有欄位的使用明細（來自 Table 和 View）
    /// </summary>
    /// <param name="ct">取消權杖</param>
    /// <returns>欄位使用明細清單</returns>
    Task<IReadOnlyList<ColumnUsageDetail>> GetAllColumnUsagesAsync(CancellationToken ct = default);
}
