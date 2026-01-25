using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 健康監控服務實作
/// </summary>
public class HealthMonitoringService : IHealthMonitoringService
{
    private readonly IHealthMonitoringRepository _repository;
    private readonly IHealthMonitoringInstaller _installer;

    public HealthMonitoringService(
        IHealthMonitoringRepository repository,
        IHealthMonitoringInstaller installer)
    {
        _repository = repository;
        _installer = installer;
    }

    /// <inheritdoc/>
    public Task<HealthMonitoringInstallStatus> GetInstallStatusAsync(CancellationToken ct = default)
    {
        return _repository.GetInstallStatusAsync(ct);
    }

    /// <inheritdoc/>
    public Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        return _installer.InstallAsync(progress, ct);
    }

    /// <inheritdoc/>
    public Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        return _installer.UninstallAsync(options, progress, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HealthStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default)
    {
        return _repository.GetStatusSummaryAsync(ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HealthMetric>> GetCurrentMetricsAsync(CancellationToken ct = default)
    {
        return _repository.GetCurrentMetricsAsync(ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HealthLogEntry>> GetRecentAlertsAsync(int days = 7, CancellationToken ct = default)
    {
        return _repository.GetRecentAlertsAsync(days, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrendDataPoint>> GetTrendDataAsync(string checkType, string metricName, int days = 30, CancellationToken ct = default)
    {
        return _repository.GetTrendDataAsync(checkType, metricName, days, ct);
    }

    /// <inheritdoc/>
    public Task ExecuteHealthCheckAsync(CancellationToken ct = default)
    {
        return _repository.ExecuteHealthCheckAsync(ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MonitoringCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return _repository.GetCategoriesAsync(ct);
    }

    /// <inheritdoc/>
    public Task UpdateCategoryAsync(int categoryId, bool isEnabled, int checkIntervalMinutes, CancellationToken ct = default)
    {
        return _repository.UpdateCategoryAsync(categoryId, isEnabled, checkIntervalMinutes, ct);
    }
}
