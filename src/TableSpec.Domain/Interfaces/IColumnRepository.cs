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

    /// <summary>
    /// 更新欄位說明
    /// </summary>
    /// <param name="schema">Schema 名稱</param>
    /// <param name="objectName">物件名稱（資料表或檢視）</param>
    /// <param name="columnName">欄位名稱</param>
    /// <param name="description">說明文字</param>
    /// <param name="objectType">物件類型（TABLE 或 VIEW），預設為 TABLE</param>
    /// <param name="ct">取消權杖</param>
    Task UpdateColumnDescriptionAsync(
        string schema,
        string objectName,
        string columnName,
        string? description,
        string objectType = "TABLE",
        CancellationToken ct = default);
}
