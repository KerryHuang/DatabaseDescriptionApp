using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 資料表查詢服務介面
/// </summary>
public interface ITableQueryService
{
    /// <summary>
    /// 取得所有資料庫物件
    /// </summary>
    Task<IReadOnlyList<TableInfo>> GetAllTablesAsync(CancellationToken ct = default);

    /// <summary>
    /// 依類型取得資料庫物件
    /// </summary>
    Task<IReadOnlyList<TableInfo>> GetTablesByTypeAsync(string type, CancellationToken ct = default);

    /// <summary>
    /// 取得指定表格的欄位資訊
    /// </summary>
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        string type,
        string schema,
        string tableName,
        CancellationToken ct = default);

    /// <summary>
    /// 取得指定表格的索引資訊
    /// </summary>
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string schema,
        string tableName,
        CancellationToken ct = default);

    /// <summary>
    /// 取得指定表格的關聯資訊
    /// </summary>
    Task<IReadOnlyList<RelationInfo>> GetRelationsAsync(
        string schema,
        string tableName,
        CancellationToken ct = default);

    /// <summary>
    /// 取得 SP/Function 的參數資訊
    /// </summary>
    Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(
        string schema,
        string objectName,
        CancellationToken ct = default);

    /// <summary>
    /// 取得 SP/Function 的程式碼定義
    /// </summary>
    Task<string?> GetDefinitionAsync(
        string schema,
        string objectName,
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

    /// <summary>
    /// 更新資料表/檢視表說明
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
