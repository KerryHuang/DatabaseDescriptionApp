using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 效能診斷 Repository 實作
/// </summary>
public class PerformanceDiagnosticsRepository : IPerformanceDiagnosticsRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public PerformanceDiagnosticsRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WaitStatistic>> GetWaitStatisticsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<WaitStatistic>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
WITH Waits AS
(SELECT
    wait_type,
    wait_time_ms / 1000.0 AS WaitS,
    (wait_time_ms - signal_wait_time_ms) / 1000.0 AS ResourceS,
    signal_wait_time_ms / 1000.0 AS SignalS,
    waiting_tasks_count AS WaitCount,
    100.0 * wait_time_ms / NULLIF(SUM(wait_time_ms) OVER(), 0) AS Percentage
FROM sys.dm_os_wait_stats
WHERE wait_type NOT IN (
    'BROKER_EVENTHANDLER','BROKER_RECEIVE_WAITFOR','BROKER_TASK_STOP','BROKER_TO_FLUSH','BROKER_TRANSMITTER',
    'CHECKPOINT_QUEUE', 'CLR_AUTO_EVENT','CLR_MANUAL_EVENT','CLR_SEMAPHORE', 'LAZYWRITER_SLEEP',
    'DBMIRRORING_CMD','DBMIRROR_EVENTS_QUEUE', 'DISPATCHER_QUEUE_SEMAPHORE',
    'FT_IFTS_SCHEDULER_IDLE_WAIT', 'FT_IFTSHC_MUTEX',
    'LOGMGR_QUEUE', 'ONDEMAND_TASK_QUEUE',
    'REQUEST_FOR_DEADLOCK_SEARCH','RESOURCE_QUEUE',
    'SLEEP_BPOOL_FLUSH','SLEEP_TASK','SLEEP_SYSTEMTASK','SQLTRACE_BUFFER_FLUSH','SQLTRACE_INCREMENTAL_FLUSH_SLEEP','SQLTRACE_LOCK','SQLTRACE_WAIT_ENTRIES',
    'TRACEWRITE',
    'WAITFOR',
    'XE_DISPATCHER_JOIN', 'XE_DISPATCHER_WAIT','XE_TIMER_EVENT')
)
SELECT
    W1.wait_type AS WaitType,
    CAST(W1.WaitS AS DECIMAL(14, 2)) AS WaitTimeSeconds,
    CAST(W1.ResourceS AS DECIMAL(14, 2)) AS ResourceWaitSeconds,
    CAST(W1.SignalS AS DECIMAL(14, 2)) AS SignalWaitSeconds,
    W1.WaitCount,
    CAST(W1.Percentage AS DECIMAL(5, 2)) AS Percentage,
    CAST((W1.WaitS / CASE WHEN W1.WaitCount = 0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL(14, 4)) AS AvgWaitTimeSeconds,
    CAST((W1.ResourceS / CASE WHEN W1.WaitCount = 0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL(14, 4)) AS AvgResourceWaitSeconds,
    CAST((W1.SignalS / CASE WHEN W1.WaitCount = 0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL(14, 4)) AS AvgSignalWaitSeconds
FROM Waits W1
ORDER BY WaitTimeSeconds DESC;";

        var result = await connection.QueryAsync<WaitStatistic>(sql);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveQueriesAsync(int top = 5, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ExpensiveQuery>();

        await using var connection = new SqlConnection(connectionString);

        var sql = $@"
SELECT TOP (@Top)
    'Query' AS QueryType,
    dest.text AS QueryText,
    NULL AS DatabaseName,
    NULL AS ObjectName,
    deqs.execution_count AS ExecutionCount,
    deqs.total_elapsed_time / 1000 AS TotalElapsedTimeMs,
    deqs.max_elapsed_time / 1000 AS MaxElapsedTimeMs,
    deqs.total_worker_time / 1000 AS TotalWorkerTimeMs,
    CAST(deqs.total_worker_time / 1000.0 / NULLIF(deqs.execution_count, 0) AS DECIMAL(18, 2)) AS AvgWorkerTimeMs,
    NULL AS QueryPlan
FROM sys.dm_exec_query_stats AS deqs
CROSS APPLY sys.dm_exec_sql_text(deqs.sql_handle) AS dest
ORDER BY deqs.max_elapsed_time DESC;";

        var result = await connection.QueryAsync<ExpensiveQuery>(sql, new { Top = top });
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExpensiveQuery>> GetTopExpensiveProceduresAsync(int top = 5, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ExpensiveQuery>();

        await using var connection = new SqlConnection(connectionString);

        var sql = $@"
SELECT TOP (@Top)
    'Procedure' AS QueryType,
    dest.text AS QueryText,
    DB_NAME(deps.database_id) AS DatabaseName,
    OBJECT_NAME(deps.object_id, deps.database_id) AS ObjectName,
    deps.execution_count AS ExecutionCount,
    deps.total_elapsed_time / 1000 AS TotalElapsedTimeMs,
    deps.max_elapsed_time / 1000 AS MaxElapsedTimeMs,
    deps.total_worker_time / 1000 AS TotalWorkerTimeMs,
    CAST(deps.total_worker_time / 1000.0 / NULLIF(deps.execution_count, 0) AS DECIMAL(18, 2)) AS AvgWorkerTimeMs,
    NULL AS QueryPlan
FROM sys.dm_exec_procedure_stats AS deps
OUTER APPLY sys.dm_exec_sql_text(deps.sql_handle) AS dest
ORDER BY deps.max_elapsed_time DESC;";

        var result = await connection.QueryAsync<ExpensiveQuery>(sql, new { Top = top });
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExpensiveQuery>> GetTopCpuIntensiveQueriesAsync(int top = 5, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ExpensiveQuery>();

        await using var connection = new SqlConnection(connectionString);

        var sql = $@"
SELECT TOP (@Top)
    'Query' AS QueryType,
    SUBSTRING(dest.text, (deqs.statement_start_offset/2)+1,
        ((CASE deqs.statement_end_offset WHEN -1 THEN DATALENGTH(dest.text)
          ELSE deqs.statement_end_offset END - deqs.statement_start_offset)/2) + 1) AS QueryText,
    NULL AS DatabaseName,
    NULL AS ObjectName,
    deqs.execution_count AS ExecutionCount,
    deqs.total_elapsed_time / 1000 AS TotalElapsedTimeMs,
    deqs.max_elapsed_time / 1000 AS MaxElapsedTimeMs,
    deqs.total_worker_time / 1000 AS TotalWorkerTimeMs,
    CAST(deqs.total_worker_time / 1000.0 / NULLIF(deqs.execution_count, 0) AS DECIMAL(18, 2)) AS AvgWorkerTimeMs,
    CAST(deqp.query_plan AS NVARCHAR(MAX)) AS QueryPlan
FROM sys.dm_exec_query_stats AS deqs
CROSS APPLY sys.dm_exec_sql_text(deqs.sql_handle) AS dest
CROSS APPLY sys.dm_exec_query_plan(deqs.plan_handle) AS deqp
ORDER BY deqs.total_worker_time / NULLIF(deqs.execution_count, 0) DESC;";

        var result = await connection.QueryAsync<ExpensiveQuery>(sql, new { Top = top });
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StatisticsInfo>> GetStatisticsInfoAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<StatisticsInfo>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    sch.name + '.' + so.name AS TableName,
    ss.name AS StatisticName,
    CASE
        WHEN ss.auto_created = 0 AND ss.user_created = 0 THEN 'Index Statistic'
        WHEN ss.auto_created = 0 AND ss.user_created = 1 THEN 'User Created'
        WHEN ss.auto_created = 1 AND ss.user_created = 0 THEN 'Auto Created'
        ELSE 'Unknown'
    END AS StatisticType,
    CASE WHEN ss.has_filter = 1 THEN 1 ELSE 0 END AS IsFiltered,
    ISNULL(ss.filter_definition, '') AS FilterDefinition,
    sp.last_updated AS LastUpdated,
    ISNULL(sp.rows, 0) AS Rows,
    ISNULL(sp.rows_sampled, 0) AS RowsSampled,
    ISNULL(sp.unfiltered_rows, 0) AS UnfilteredRows,
    ISNULL(sp.modification_counter, 0) AS ModificationCounter,
    ISNULL(sp.steps, 0) AS HistogramSteps,
    sp.persisted_sample_percent AS PersistedSamplePercent
FROM sys.stats ss
JOIN sys.objects so ON ss.object_id = so.object_id
JOIN sys.schemas sch ON so.schema_id = sch.schema_id
OUTER APPLY sys.dm_db_stats_properties(so.object_id, ss.stats_id) AS sp
WHERE so.type = 'U'
ORDER BY sp.last_updated DESC;";

        var result = await connection.QueryAsync<StatisticsInfo>(sql);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ErrorLogEntry>> GetErrorLogAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ErrorLogEntry>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
CREATE TABLE #ErrorLog (LogDate DATETIME, ProcessInfo NVARCHAR(100), Text NVARCHAR(MAX));
INSERT INTO #ErrorLog EXEC master.dbo.xp_readerrorlog;
SELECT LogDate, ProcessInfo, Text FROM #ErrorLog ORDER BY LogDate DESC;
DROP TABLE #ErrorLog;";

        var result = await connection.QueryAsync<ErrorLogEntry>(sql, commandTimeout: 60);
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IndexStatus>> GetIndexStatusAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<IndexStatus>();

        var results = new List<IndexStatus>();
        var databases = await GetUserDatabasesAsync(connectionString, ct);

        for (var i = 0; i < databases.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"分析資料庫 {databases[i]} ({i + 1}/{databases.Count})...");

            var dbResults = await GetIndexStatusForDatabaseAsync(connectionString, databases[i], ct);
            results.AddRange(dbResults);
        }

        progress?.Report($"索引分析完成，共 {results.Count} 個索引");
        return results;
    }

    private async Task<IReadOnlyList<string>> GetUserDatabasesAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT name FROM sys.databases
WHERE database_id > 4 AND state = 0 AND is_read_only = 0
ORDER BY name;";

        var result = await connection.QueryAsync<string>(sql);
        return result.ToList();
    }

    private async Task<IReadOnlyList<IndexStatus>> GetIndexStatusForDatabaseAsync(string connectionString, string databaseName, CancellationToken ct)
    {
        try
        {
            // 建立連線到特定資料庫
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(builder.ConnectionString);

            const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    @DatabaseName AS DatabaseName,
    s.name AS SchemaName,
    OBJECT_NAME(idx.[object_id]) AS TableName,
    idx.[name] AS IndexName,
    idx.type_desc AS IndexType,
    CAST((SUM(ddps.[used_page_count]) * 8.0) / 1024 AS DECIMAL(18, 2)) AS IndexSizeMB,
    ISNULL(ddius.[user_seeks], 0) AS UserSeeks,
    ISNULL(ddius.[user_scans], 0) AS UserScans,
    ISNULL(ddius.[user_lookups], 0) AS UserLookups,
    ISNULL(ddius.[user_updates], 0) AS UserUpdates,
    ddius.[last_user_seek] AS LastUserSeek,
    ddius.[last_user_scan] AS LastUserScan,
    ddius.[last_user_lookup] AS LastUserLookup,
    ddius.[last_user_update] AS LastUserUpdate,
    CAST(ISNULL(phs.avg_fragmentation_in_percent, 0) AS DECIMAL(5, 2)) AS FragmentationPercent,
    CAST(ISNULL(phs.avg_page_space_used_in_percent, 0) AS DECIMAL(5, 2)) AS PageSpaceUsedPercent
FROM sys.indexes idx
INNER JOIN sys.tables AS t ON t.[object_id] = idx.[object_id]
INNER JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
LEFT JOIN sys.dm_db_index_usage_stats ddius ON idx.[index_id] = ddius.[index_id] AND idx.[object_id] = ddius.[object_id] AND ddius.database_id = DB_ID()
LEFT JOIN sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'SAMPLED') AS phs ON phs.[index_id] = idx.[index_id] AND phs.[object_id] = idx.[object_id]
LEFT JOIN sys.dm_db_partition_stats ddps ON ddps.[index_id] = idx.[index_id] AND ddps.[object_id] = idx.[object_id]
WHERE idx.name IS NOT NULL
GROUP BY s.name, idx.object_id, idx.name, idx.type_desc, ddius.user_updates, ddius.user_seeks, ddius.user_scans, ddius.user_lookups,
         ddius.last_user_seek, ddius.last_user_scan, ddius.last_user_lookup, ddius.last_user_update,
         phs.avg_fragmentation_in_percent, phs.avg_page_space_used_in_percent
ORDER BY IndexSizeMB DESC;";

            var result = await connection.QueryAsync<IndexStatus>(sql, new { DatabaseName = databaseName }, commandTimeout: 300);
            return result.ToList();
        }
        catch
        {
            // 若某資料庫無法存取，略過
            return Array.Empty<IndexStatus>();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MissingIndex>> GetMissingIndexesAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<MissingIndex>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
SELECT
    mid.statement AS TableName,
    migs.avg_total_user_cost * (migs.avg_user_impact / 100.0) * (migs.user_seeks + migs.user_scans) AS ImprovementMeasure,
    'CREATE INDEX [missing_index_' + CONVERT(VARCHAR, mig.index_group_handle) + '_' + CONVERT(VARCHAR, mid.index_handle)
    + '_' + LEFT(PARSENAME(mid.statement, 1), 32) + ']'
    + ' ON ' + mid.statement
    + ' (' + ISNULL(mid.equality_columns, '')
    + CASE WHEN mid.equality_columns IS NOT NULL AND mid.inequality_columns IS NOT NULL THEN ',' ELSE '' END
    + ISNULL(mid.inequality_columns, '')
    + ')'
    + ISNULL(' INCLUDE (' + mid.included_columns + ')', '') AS CreateIndexStatement,
    migs.unique_compiles AS UniqueCompiles,
    migs.user_seeks AS UserSeeks,
    migs.user_scans AS UserScans,
    migs.last_user_seek AS LastUserSeek,
    migs.last_user_scan AS LastUserScan,
    migs.avg_total_user_cost AS AvgTotalUserCost,
    migs.avg_user_impact AS AvgUserImpact,
    migs.system_seeks AS SystemSeeks,
    migs.system_scans AS SystemScans,
    migs.last_system_seek AS LastSystemSeek,
    migs.last_system_scan AS LastSystemScan,
    migs.avg_total_system_cost AS AvgTotalSystemCost,
    migs.avg_system_impact AS AvgSystemImpact
FROM sys.dm_db_missing_index_groups mig
INNER JOIN sys.dm_db_missing_index_group_stats migs ON migs.group_handle = mig.index_group_handle
INNER JOIN sys.dm_db_missing_index_details mid ON mig.index_handle = mid.index_handle
ORDER BY migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) DESC;";

        var result = await connection.QueryAsync<MissingIndex>(sql, commandTimeout: 300);
        return result.ToList();
    }
}
