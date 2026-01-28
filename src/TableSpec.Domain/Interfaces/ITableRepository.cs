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

    /// <summary>
    /// 更新物件說明
    /// </summary>
    /// <param name="type">物件類型（BASE TABLE、VIEW、PROCEDURE、FUNCTION）</param>
    /// <param name="schema">Schema 名稱</param>
    /// <param name="objectName">物件名稱</param>
    /// <param name="description">說明文字</param>
    /// <param name="ct">取消權杖</param>
    Task UpdateTableDescriptionAsync(
        string type,
        string schema,
        string objectName,
        string? description,
        CancellationToken ct = default);
}
