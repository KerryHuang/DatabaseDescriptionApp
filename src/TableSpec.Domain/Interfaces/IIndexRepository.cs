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

    /// <summary>
    /// 刪除指定索引
    /// </summary>
    /// <param name="schema">結構描述名稱</param>
    /// <param name="tableName">資料表名稱</param>
    /// <param name="indexName">索引名稱</param>
    /// <param name="ct">取消權杖</param>
    Task DropIndexAsync(
        string schema,
        string tableName,
        string indexName,
        CancellationToken ct = default);
}
