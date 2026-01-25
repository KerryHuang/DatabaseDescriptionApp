namespace TableSpec.Domain.Entities;

/// <summary>
/// 索引狀態實體
/// </summary>
public class IndexStatus
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
    /// 索引類型
    /// </summary>
    public required string IndexType { get; init; }

    /// <summary>
    /// 索引大小（MB）
    /// </summary>
    public decimal IndexSizeMB { get; init; }

    /// <summary>
    /// 使用者 Seek 次數
    /// </summary>
    public long UserSeeks { get; init; }

    /// <summary>
    /// 使用者 Scan 次數
    /// </summary>
    public long UserScans { get; init; }

    /// <summary>
    /// 使用者 Lookup 次數
    /// </summary>
    public long UserLookups { get; init; }

    /// <summary>
    /// 使用者更新次數
    /// </summary>
    public long UserUpdates { get; init; }

    /// <summary>
    /// 最後 Seek 時間
    /// </summary>
    public DateTime? LastUserSeek { get; init; }

    /// <summary>
    /// 最後 Scan 時間
    /// </summary>
    public DateTime? LastUserScan { get; init; }

    /// <summary>
    /// 最後 Lookup 時間
    /// </summary>
    public DateTime? LastUserLookup { get; init; }

    /// <summary>
    /// 最後更新時間
    /// </summary>
    public DateTime? LastUserUpdate { get; init; }

    /// <summary>
    /// 平均碎片化百分比
    /// </summary>
    public decimal FragmentationPercent { get; init; }

    /// <summary>
    /// 平均頁面空間使用百分比
    /// </summary>
    public decimal PageSpaceUsedPercent { get; init; }
}
