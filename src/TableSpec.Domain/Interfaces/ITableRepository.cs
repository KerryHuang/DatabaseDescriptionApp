using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 資料庫物件查詢 Repository
/// </summary>
public interface ITableRepository
{
    /// <summary>
    /// 取得所有資料庫物件（Table、View、SP、Function）
    /// </summary>
    Task<IReadOnlyList<TableInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 依類型取得資料庫物件
    /// </summary>
    Task<IReadOnlyList<TableInfo>> GetByTypeAsync(string type, CancellationToken ct = default);
}
