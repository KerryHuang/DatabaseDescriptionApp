namespace TableSpec.Domain.Entities;

/// <summary>
/// 健康監控安裝狀態實體
/// </summary>
public class HealthMonitoringInstallStatus
{
    /// <summary>
    /// DBA 資料庫是否存在
    /// </summary>
    public required bool DatabaseExists { get; init; }

    /// <summary>
    /// ServerHealthLog 資料表是否存在
    /// </summary>
    public required bool HealthLogTableExists { get; init; }

    /// <summary>
    /// MonitoringCategories 資料表是否存在
    /// </summary>
    public required bool CategoriesTableExists { get; init; }

    /// <summary>
    /// 主監控預存程序是否存在
    /// </summary>
    public required bool MasterProcedureExists { get; init; }

    /// <summary>
    /// 視圖是否存在
    /// </summary>
    public required bool ViewsExist { get; init; }

    /// <summary>
    /// SQL Agent 作業是否存在
    /// </summary>
    public required bool AgentJobsExist { get; init; }

    /// <summary>
    /// 監控記錄數量
    /// </summary>
    public int LogCount { get; init; }

    /// <summary>
    /// 是否完整安裝
    /// </summary>
    public bool IsFullyInstalled => DatabaseExists && HealthLogTableExists &&
        CategoriesTableExists && MasterProcedureExists && ViewsExist;

    /// <summary>
    /// 是否部分安裝
    /// </summary>
    public bool IsPartiallyInstalled => DatabaseExists && (HealthLogTableExists || CategoriesTableExists || MasterProcedureExists);
}
