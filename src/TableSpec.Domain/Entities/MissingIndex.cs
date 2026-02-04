namespace TableSpec.Domain.Entities;

/// <summary>
/// 缺少索引建議實體
/// </summary>
public class MissingIndex
{
    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>
    /// 結構描述名稱
    /// </summary>
    public string SchemaName { get; init; } = string.Empty;

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

    /// <summary>
    /// 純資料表名稱（不含資料庫和結構描述）
    /// </summary>
    public string ShortTableName
    {
        get
        {
            // TableName 格式：[Database].[Schema].[Table]
            var parts = TableName.Split('.');
            if (parts.Length >= 3)
                return parts[2].Trim('[', ']');
            if (parts.Length >= 1)
                return parts[^1].Trim('[', ']');
            return TableName;
        }
    }

    /// <summary>
    /// 嚴重度等級（依改善指標分級）
    /// </summary>
    public string SeverityLevel => ImprovementMeasure switch
    {
        >= 1_000_000m => "Critical",
        >= 100_000m => "High",
        >= 10_000m => "Medium",
        _ => "Low"
    };
}
