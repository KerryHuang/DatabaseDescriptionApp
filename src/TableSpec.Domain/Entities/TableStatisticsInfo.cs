namespace TableSpec.Domain.Entities;

/// <summary>
/// 資料表統計資訊
/// </summary>
public class TableStatisticsInfo
{
    /// <summary>結構描述名稱（如 dbo）</summary>
    public required string SchemaName { get; init; }

    /// <summary>資料表名稱</summary>
    public required string TableName { get; init; }

    /// <summary>物件類型（TABLE 或 VIEW）</summary>
    public required string ObjectType { get; init; }

    /// <summary>概估資料列數（來自 sys.dm_db_partition_stats，快速）</summary>
    public long ApproximateRowCount { get; init; }

    /// <summary>精確資料列數（來自 COUNT(*)，慢速，可選）</summary>
    public long? ExactRowCount { get; set; }

    /// <summary>欄位數</summary>
    public int ColumnCount { get; init; }

    /// <summary>索引數</summary>
    public int IndexCount { get; init; }

    /// <summary>外鍵/關聯數</summary>
    public int ForeignKeyCount { get; init; }

    /// <summary>資料大小（MB）</summary>
    public decimal DataSizeMB { get; init; }

    /// <summary>索引大小（MB）</summary>
    public decimal IndexSizeMB { get; init; }

    /// <summary>總大小（MB）= 資料 + 索引</summary>
    public decimal TotalSizeMB { get; init; }

    /// <summary>目前顯示的資料列數（優先回傳精確值）</summary>
    public long DisplayRowCount => ExactRowCount ?? ApproximateRowCount;

    /// <summary>是否已取得精確資料列數</summary>
    public bool HasExactRowCount => ExactRowCount.HasValue;
}
