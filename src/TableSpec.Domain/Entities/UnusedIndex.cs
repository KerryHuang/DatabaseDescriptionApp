namespace TableSpec.Domain.Entities;

/// <summary>
/// 未使用索引資訊
/// </summary>
public class UnusedIndex
{
    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// 結構描述名稱
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// 資料表名稱
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// 索引名稱
    /// </summary>
    public required string IndexName { get; init; }

    /// <summary>
    /// 索引類型（CLUSTERED / NONCLUSTERED）
    /// </summary>
    public required string IndexType { get; init; }

    /// <summary>
    /// 是否為唯一索引
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// 使用者更新次數（INSERT/UPDATE/DELETE 維護索引的次數）
    /// </summary>
    public long UserUpdates { get; init; }

    /// <summary>
    /// 使用者 Seek 次數（應為 0）
    /// </summary>
    public long UserSeeks { get; init; }

    /// <summary>
    /// 使用者 Scan 次數（應為 0）
    /// </summary>
    public long UserScans { get; init; }

    /// <summary>
    /// 最後更新時間
    /// </summary>
    public DateTime? LastUserUpdate { get; init; }

    /// <summary>
    /// 索引欄位列表（逗號分隔）
    /// </summary>
    public string IndexColumns { get; init; } = string.Empty;

    /// <summary>
    /// 索引大小（MB）
    /// </summary>
    public decimal IndexSizeMB { get; init; }

    /// <summary>
    /// 資料列數
    /// </summary>
    public long RowCount { get; init; }

    /// <summary>
    /// DROP INDEX 語法
    /// </summary>
    public string DropIndexStatement => $"DROP INDEX [{IndexName}] ON [{SchemaName}].[{TableName}]";

    /// <summary>
    /// 嚴重度等級（依更新次數分級，更新越多代表維護成本越高）
    /// </summary>
    public string SeverityLevel => UserUpdates switch
    {
        >= 100_000 => "Critical",
        >= 10_000 => "High",
        >= 1_000 => "Medium",
        _ => "Low"
    };
}
