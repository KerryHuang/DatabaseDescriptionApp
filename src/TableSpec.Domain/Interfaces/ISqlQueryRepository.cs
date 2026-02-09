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

    /// <summary>
    /// 來源資料庫名稱
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// 同名欄位中出現次數最多的資料型別
    /// </summary>
    public string PrimaryDataType { get; set; } = string.Empty;

    /// <summary>
    /// 此欄位的資料型別是否與主要型別一致
    /// </summary>
    public bool MatchesPrimaryDataType =>
        !string.IsNullOrEmpty(PrimaryDataType) &&
        string.Equals(DataType, PrimaryDataType, System.StringComparison.OrdinalIgnoreCase);

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
    /// <param name="columnName">欄位名稱關鍵字</param>
    /// <param name="exactMatch">是否精確比對（預設為模糊搜尋）</param>
    /// <param name="ct">取消權杖</param>
    Task<List<ColumnSearchResult>> SearchColumnsAsync(string columnName, bool exactMatch = false, CancellationToken ct = default);
}
