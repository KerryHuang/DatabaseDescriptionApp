namespace TableSpec.Domain.Entities;

/// <summary>
/// 缺少索引建議實體
/// </summary>
public class MissingIndex
{
    /// <summary>
    /// 資料表名稱（含結構描述）
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// 效能改善指標
    /// </summary>
    public decimal ImprovementMeasure { get; init; }

    /// <summary>
    /// 建立索引語法
    /// </summary>
    public required string CreateIndexStatement { get; init; }

    /// <summary>
    /// 查詢編譯次數
    /// </summary>
    public long UniqueCompiles { get; init; }

    /// <summary>
    /// 使用者 Seek 次數
    /// </summary>
    public long UserSeeks { get; init; }

    /// <summary>
    /// 使用者 Scan 次數
    /// </summary>
    public long UserScans { get; init; }

    /// <summary>
    /// 最後 Seek 時間
    /// </summary>
    public DateTime? LastUserSeek { get; init; }

    /// <summary>
    /// 最後 Scan 時間
    /// </summary>
    public DateTime? LastUserScan { get; init; }

    /// <summary>
    /// 平均使用者查詢成本
    /// </summary>
    public decimal AvgTotalUserCost { get; init; }

    /// <summary>
    /// 預估改善百分比
    /// </summary>
    public decimal AvgUserImpact { get; init; }

    /// <summary>
    /// 系統 Seek 次數
    /// </summary>
    public long SystemSeeks { get; init; }

    /// <summary>
    /// 系統 Scan 次數
    /// </summary>
    public long SystemScans { get; init; }

    /// <summary>
    /// 最後系統 Seek 時間
    /// </summary>
    public DateTime? LastSystemSeek { get; init; }

    /// <summary>
    /// 最後系統 Scan 時間
    /// </summary>
    public DateTime? LastSystemScan { get; init; }

    /// <summary>
    /// 平均系統查詢成本
    /// </summary>
    public decimal AvgTotalSystemCost { get; init; }

    /// <summary>
    /// 系統查詢改善百分比
    /// </summary>
    public decimal AvgSystemImpact { get; init; }
}
