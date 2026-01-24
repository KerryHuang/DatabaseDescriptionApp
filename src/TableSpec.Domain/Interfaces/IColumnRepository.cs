using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 欄位查詢 Repository
/// </summary>
public interface IColumnRepository
{
    /// <summary>
    /// 取得指定表格的所有欄位
    /// </summary>
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        string type,
        string schema,
        string tableName,
        CancellationToken ct = default);
}
