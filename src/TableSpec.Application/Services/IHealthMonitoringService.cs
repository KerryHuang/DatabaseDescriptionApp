using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 健康監控服務介面
/// </summary>
public interface IHealthMonitoringService
{
    /// <summary>
    /// 取得安裝狀態
    /// </summary>
    Task<HealthMonitoringInstallStatus> GetInstallStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 安裝健康監控系統
    /// </summary>
    Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 移除健康監控系統
    /// </summary>
    Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 取得狀態摘要
    /// </summary>
    Task<IReadOnlyList<HealthStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得目前指標
    /// </summary>
    Task<IReadOnlyList<HealthMetric>> GetCurrentMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得最近告警
    /// </summary>
    Task<IReadOnlyList<HealthLogEntry>> GetRecentAlertsAsync(int days = 7, CancellationToken ct = default);

    /// <summary>
    /// 取得趨勢資料
    /// </summary>
    Task<IReadOnlyList<TrendDataPoint>> GetTrendDataAsync(string checkType, string metricName, int days = 30, CancellationToken ct = default);

    /// <summary>
    /// 執行健康檢查
    /// </summary>
    Task ExecuteHealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得監控類別
    /// </summary>
    Task<IReadOnlyList<MonitoringCategory>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// 更新監控類別
    /// </summary>
    Task UpdateCategoryAsync(int categoryId, bool isEnabled, int checkIntervalMinutes, CancellationToken ct = default);
}

/// <summary>
/// 安裝進度
/// </summary>
public record InstallProgress(int PercentComplete, string Message);

/// <summary>
/// 安裝結果
/// </summary>
public record InstallResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// 移除選項
/// </summary>
public record UninstallOptions(bool KeepHistoryData = false, bool RemoveJobsOnly = false);

/// <summary>
/// 移除結果
/// </summary>
public record UninstallResult(bool Success, string? ErrorMessage = null);
