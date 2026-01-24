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
}
