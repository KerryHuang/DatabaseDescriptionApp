using System.Data;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 欄位搜尋結果
/// </summary>
public class ColumnSearchResult
{
    public string ColumnName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string FullObjectName => $"{SchemaName}.{ObjectName}";
}

/// <summary>
/// SQL 查詢 Repository 介面
/// </summary>
public interface ISqlQueryRepository
{
    /// <summary>
    /// 執行 SQL 查詢並返回結果
    /// </summary>
    Task<DataTable> ExecuteQueryAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 取得欄位描述對照表（表名.欄位名 -> 描述）
    /// </summary>
    Task<Dictionary<string, string>> GetColumnDescriptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 搜尋欄位名稱
    /// </summary>
    Task<List<ColumnSearchResult>> SearchColumnsAsync(string columnName, CancellationToken ct = default);
}
