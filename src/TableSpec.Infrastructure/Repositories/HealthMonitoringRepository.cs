using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 健康監控 Repository 實作
/// </summary>
public class HealthMonitoringRepository : IHealthMonitoringRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public HealthMonitoringRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <inheritdoc/>
    public async Task<HealthMonitoringInstallStatus> GetInstallStatusAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
        {
            return new HealthMonitoringInstallStatus
            {
                DatabaseExists = false,
                HealthLogTableExists = false,
                CategoriesTableExists = false,
                MasterProcedureExists = false,
                ViewsExist = false,
                AgentJobsExist = false,
                LogCount = 0
            };
        }

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
DECLARE @DatabaseExists BIT = 0;
DECLARE @HealthLogTableExists BIT = 0;
DECLARE @CategoriesTableExists BIT = 0;
DECLARE @MasterProcedureExists BIT = 0;
DECLARE @ViewsExist BIT = 0;
DECLARE @AgentJobsExist BIT = 0;
DECLARE @LogCount INT = 0;

-- 檢查 DBA 資料庫是否存在
IF EXISTS (SELECT name FROM sys.databases WHERE name = 'DBA')
BEGIN
    SET @DatabaseExists = 1;

    -- 檢查資料表
    IF EXISTS (SELECT * FROM DBA.sys.tables WHERE name = 'ServerHealthLog')
    BEGIN
        SET @HealthLogTableExists = 1;
        SELECT @LogCount = COUNT(*) FROM DBA.dbo.ServerHealthLog;
    END

    IF EXISTS (SELECT * FROM DBA.sys.tables WHERE name = 'MonitoringCategories')
        SET @CategoriesTableExists = 1;

    -- 檢查主預存程序
    IF EXISTS (SELECT * FROM DBA.sys.procedures WHERE name = 'sp_MasterHealthCheck')
        SET @MasterProcedureExists = 1;

    -- 檢查視圖
    IF EXISTS (SELECT * FROM DBA.sys.views WHERE name = 'vw_CurrentStatus')
        SET @ViewsExist = 1;
END

-- 檢查 SQL Agent 作業
IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Server Health Monitoring')
    SET @AgentJobsExist = 1;

SELECT
    @DatabaseExists AS DatabaseExists,
    @HealthLogTableExists AS HealthLogTableExists,
    @CategoriesTableExists AS CategoriesTableExists,
    @MasterProcedureExists AS MasterProcedureExists,
    @ViewsExist AS ViewsExist,
    @AgentJobsExist AS AgentJobsExist,
    @LogCount AS LogCount;";

        var result = await connection.QueryFirstOrDefaultAsync<HealthMonitoringInstallStatus>(sql);
        return result ?? new HealthMonitoringInstallStatus
        {
            DatabaseExists = false,
            HealthLogTableExists = false,
            CategoriesTableExists = false,
            MasterProcedureExists = false,
            ViewsExist = false,
            AgentJobsExist = false,
            LogCount = 0
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HealthStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<HealthStatusSummary>();

        // 先檢查 DBA 資料庫是否存在
        if (!await DatabaseExistsAsync(connectionString))
            return Array.Empty<HealthStatusSummary>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    check_type AS CheckType,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN status = 'OK' THEN 1 ELSE 0 END) AS OkCount,
    SUM(CASE WHEN status = 'WARNING' THEN 1 ELSE 0 END) AS WarningCount,
    SUM(CASE WHEN status = 'CRITICAL' THEN 1 ELSE 0 END) AS CriticalCount,
    MAX(check_time) AS LastCheckTime
FROM DBA.dbo.vw_CurrentStatus
GROUP BY check_type
ORDER BY
    SUM(CASE WHEN status = 'CRITICAL' THEN 1 ELSE 0 END) DESC,
    SUM(CASE WHEN status = 'WARNING' THEN 1 ELSE 0 END) DESC;";

        var result = await connection.QueryAsync<HealthStatusSummary>(sql);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HealthMetric>> GetCurrentMetricsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<HealthMetric>();

        if (!await DatabaseExistsAsync(connectionString))
            return Array.Empty<HealthMetric>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    check_type AS CheckType,
    metric_name AS MetricName,
    metric_value AS MetricValue,
    threshold_value AS ThresholdValue,
    status AS Status,
    check_time AS CheckTime,
    alert_message AS AlertMessage,
    status_priority AS StatusPriority
FROM DBA.dbo.vw_CurrentStatus
ORDER BY status_priority DESC, check_type, metric_name;";

        var result = await connection.QueryAsync<HealthMetric>(sql);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HealthLogEntry>> GetRecentAlertsAsync(int days = 7, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<HealthLogEntry>();

        if (!await DatabaseExistsAsync(connectionString))
            return Array.Empty<HealthLogEntry>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    log_id AS LogId,
    check_time AS CheckTime,
    check_type AS CheckType,
    metric_name AS MetricName,
    metric_value AS MetricValue,
    threshold_value AS ThresholdValue,
    status AS Status,
    alert_message AS AlertMessage,
    server_name AS ServerName,
    database_name AS DatabaseName,
    additional_info AS AdditionalInfo
FROM DBA.dbo.ServerHealthLog
WHERE check_time >= DATEADD(DAY, -@Days, GETDATE())
ORDER BY check_time DESC;";

        var result = await connection.QueryAsync<HealthLogEntry>(sql, new { Days = days });
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TrendDataPoint>> GetTrendDataAsync(string checkType, string metricName, int days = 30, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TrendDataPoint>();

        if (!await DatabaseExistsAsync(connectionString))
            return Array.Empty<TrendDataPoint>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    check_time AS CheckTime,
    check_type AS CheckType,
    metric_name AS MetricName,
    metric_value AS MetricValue,
    threshold_value AS ThresholdValue,
    status AS Status
FROM DBA.dbo.ServerHealthLog
WHERE check_type = @CheckType
    AND metric_name = @MetricName
    AND check_time >= DATEADD(DAY, -@Days, GETDATE())
ORDER BY check_time;";

        var result = await connection.QueryAsync<TrendDataPoint>(sql, new { CheckType = checkType, MetricName = metricName, Days = days });
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MonitoringCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<MonitoringCategory>();

        if (!await DatabaseExistsAsync(connectionString))
            return Array.Empty<MonitoringCategory>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    category_id AS CategoryId,
    category_name AS CategoryName,
    is_enabled AS IsEnabled,
    check_interval_minutes AS CheckIntervalMinutes,
    description AS Description,
    last_check_time AS LastCheckTime,
    created_date AS CreatedDate
FROM DBA.dbo.MonitoringCategories
ORDER BY category_name;";

        var result = await connection.QueryAsync<MonitoringCategory>(sql);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateCategoryAsync(int categoryId, bool isEnabled, int checkIntervalMinutes, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        if (!await DatabaseExistsAsync(connectionString))
            return;

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
UPDATE DBA.dbo.MonitoringCategories
SET is_enabled = @IsEnabled,
    check_interval_minutes = @CheckIntervalMinutes
WHERE category_id = @CategoryId;";

        await connection.ExecuteAsync(sql, new
        {
            CategoryId = categoryId,
            IsEnabled = isEnabled,
            CheckIntervalMinutes = checkIntervalMinutes
        });
    }

    /// <inheritdoc/>
    public async Task ExecuteHealthCheckAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        if (!await DatabaseExistsAsync(connectionString))
            return;

        await using var connection = new SqlConnection(connectionString);

        await connection.ExecuteAsync(
            "EXEC DBA.dbo.sp_MasterHealthCheck;",
            commandTimeout: 300);
    }

    private async Task<bool> DatabaseExistsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryFirstOrDefaultAsync<int>(
            "SELECT CASE WHEN EXISTS (SELECT name FROM sys.databases WHERE name = 'DBA') THEN 1 ELSE 0 END");
        return result == 1;
    }
}
