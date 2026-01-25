namespace TableSpec.Domain.Entities;

/// <summary>
/// 監控類別實體
/// </summary>
public class MonitoringCategory
{
    /// <summary>
    /// 類別 ID
    /// </summary>
    public required int CategoryId { get; init; }

    /// <summary>
    /// 類別名稱
    /// </summary>
    public required string CategoryName { get; init; }

    /// <summary>
    /// 是否啟用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 檢查間隔（分鐘）
    /// </summary>
    public int CheckIntervalMinutes { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 上次檢查時間
    /// </summary>
    public DateTime? LastCheckTime { get; init; }

    /// <summary>
    /// 建立日期
    /// </summary>
    public DateTime CreatedDate { get; init; }
}
