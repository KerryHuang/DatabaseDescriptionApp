using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 索引查詢 Repository
/// </summary>
public interface IIndexRepository
{
    /// <summary>
    /// 取得指定表格的所有索引
    /// </summary>
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string schema,
        string tableName,
        CancellationToken ct = default);
}
