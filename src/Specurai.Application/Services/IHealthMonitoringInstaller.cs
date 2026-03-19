namespace Specurai.Application.Services;

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

    /// <summary>
    /// 產生可匯出的健康監控安裝 SQL 腳本（含 SQLCMD 變數宣告和說明）
    /// </summary>
    Task<string> GenerateExportSqlAsync(CancellationToken ct = default);
}
