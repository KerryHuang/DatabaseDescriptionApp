namespace TableSpec.Domain.Entities;

/// <summary>
/// 統計資訊實體
/// </summary>
public class StatisticsInfo
{
    /// <summary>
    /// 資料表名稱（含結構描述）
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// 統計名稱
    /// </summary>
    public required string StatisticName { get; init; }

    /// <summary>
    /// 統計類型（Index Statistic/User Created/Auto Created）
    /// </summary>
    public required string StatisticType { get; init; }

    /// <summary>
    /// 是否為篩選統計
    /// </summary>
    public bool IsFiltered { get; init; }

    /// <summary>
    /// 篩選條件定義
    /// </summary>
    public string? FilterDefinition { get; init; }

    /// <summary>
    /// 最後更新時間
    /// </summary>
    public DateTime? LastUpdated { get; init; }

    /// <summary>
    /// 資料列數
    /// </summary>
    public long Rows { get; init; }

    /// <summary>
    /// 取樣資料列數
    /// </summary>
    public long RowsSampled { get; init; }

    /// <summary>
    /// 未篩選的資料列數
    /// </summary>
    public long UnfilteredRows { get; init; }

    /// <summary>
    /// 資料列修改次數
    /// </summary>
    public long ModificationCounter { get; init; }

    /// <summary>
    /// 直方圖步驟數
    /// </summary>
    public int HistogramSteps { get; init; }

    /// <summary>
    /// 持續取樣百分比
    /// </summary>
    public decimal? PersistedSamplePercent { get; init; }
}
