namespace TableSpec.Application.Services;

/// <summary>
/// 健康監控安裝器介面
/// </summary>
public interface IHealthMonitoringInstaller
{
    /// <summary>
    /// 安裝健康監控系統
    /// </summary>
    Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 移除健康監控系統
    /// </summary>
    Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
}
