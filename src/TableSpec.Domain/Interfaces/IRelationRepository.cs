using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 關聯查詢 Repository
/// </summary>
public interface IRelationRepository
{
    /// <summary>
    /// 取得指定表格的所有關聯（包含 Outgoing 和 Incoming）
    /// </summary>
    Task<IReadOnlyList<RelationInfo>> GetRelationsAsync(
        string schema,
        string tableName,
        CancellationToken ct = default);
}
