-- ==========================================
-- SQL Server 完整健康監控系統
-- 版本: 3.0 (完整版)
-- 功能: 記憶體、CPU、磁碟、I/O、阻塞、死結、備份、作業等全方位監控
-- ==========================================

SET NOCOUNT ON;
GO

-- ==========================================
-- 步驟 1: 建立 DBA 資料庫
-- ==========================================
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DBA')
BEGIN
    CREATE DATABASE DBA;
END
GO

USE DBA;
GO

-- ==========================================
-- 步驟 2: 建立資料表
-- ==========================================

-- 主監控記錄表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServerHealthLog')
BEGIN
    CREATE TABLE dbo.ServerHealthLog (
        log_id INT IDENTITY(1,1) PRIMARY KEY,
        check_time DATETIME DEFAULT GETDATE(),
        check_type VARCHAR(50) NOT NULL,
        metric_name VARCHAR(100) NOT NULL,
        metric_value DECIMAL(18,2),
        threshold_value DECIMAL(18,2),
        status VARCHAR(20),
        alert_message NVARCHAR(500),
        server_name NVARCHAR(128) DEFAULT @@SERVERNAME,
        database_name NVARCHAR(128),
        additional_info NVARCHAR(MAX)
    );

    CREATE INDEX IX_ServerHealthLog_CheckTime ON dbo.ServerHealthLog(check_time);
    CREATE INDEX IX_ServerHealthLog_Status ON dbo.ServerHealthLog(status);
    CREATE INDEX IX_ServerHealthLog_CheckType ON dbo.ServerHealthLog(check_type);
    CREATE INDEX IX_ServerHealthLog_Composite ON dbo.ServerHealthLog(check_time, check_type, status);
END
GO

-- 監控類別表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MonitoringCategories')
BEGIN
    CREATE TABLE dbo.MonitoringCategories (
        category_id INT IDENTITY(1,1) PRIMARY KEY,
        category_name VARCHAR(50) NOT NULL UNIQUE,
        is_enabled BIT DEFAULT 1,
        check_interval_minutes INT DEFAULT 60,
        description NVARCHAR(500),
        last_check_time DATETIME,
        created_date DATETIME DEFAULT GETDATE()
    );

    INSERT INTO MonitoringCategories (category_name, check_interval_minutes, description)
    VALUES
        ('Memory', 60, N'記憶體使用監控'),
        ('Disk', 60, N'磁碟空間監控'),
        ('CPU', 60, N'CPU 使用率監控'),
        ('Performance', 60, N'效能計數器監控'),
        ('Blocking', 15, N'阻塞與鎖定監控'),
        ('LongQuery', 30, N'長時間執行查詢監控'),
        ('Backup', 1440, N'備份狀態監控（每日）'),
        ('Deadlock', 60, N'死結監控'),
        ('Connections', 60, N'連線數監控'),
        ('TempDB', 60, N'TempDB 使用監控'),
        ('Transaction', 60, N'交易記錄監控'),
        ('Jobs', 60, N'SQL Agent 作業監控'),
        ('IO', 60, N'I/O 效能監控');
END
GO

-- ==========================================
-- 步驟 3: 建立監控預存程序
-- ==========================================

-- 3.1 CPU 監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorCPU')
    DROP PROCEDURE dbo.sp_MonitorCPU;
GO

CREATE PROCEDURE dbo.sp_MonitorCPU
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SQLProcessCPU INT;
    DECLARE @SystemIdleCPU INT;

    SELECT TOP 1
        @SQLProcessCPU = SQLProcessUtilization,
        @SystemIdleCPU = SystemIdle
    FROM (
        SELECT
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS SystemIdle,
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SQLProcessUtilization,
            timestamp
        FROM (
            SELECT timestamp, CONVERT(XML, record) AS record
            FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
            AND record LIKE '%<SystemHealth>%'
        ) AS x
    ) AS y
    ORDER BY timestamp DESC;

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    VALUES (
        'CPU',
        'SQL Server CPU %',
        ISNULL(@SQLProcessCPU, 0),
        80,
        CASE
            WHEN @SQLProcessCPU > 90 THEN 'CRITICAL'
            WHEN @SQLProcessCPU > 80 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @SQLProcessCPU > 90 THEN N'SQL Server CPU 使用率超過 90%'
            WHEN @SQLProcessCPU > 80 THEN N'SQL Server CPU 使用率超過 80%'
            ELSE NULL
        END
    );

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    VALUES (
        'CPU',
        'System Idle %',
        ISNULL(@SystemIdleCPU, 100),
        20,
        CASE
            WHEN @SystemIdleCPU < 10 THEN 'CRITICAL'
            WHEN @SystemIdleCPU < 20 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @SystemIdleCPU < 10 THEN N'系統閒置率低於 10%'
            WHEN @SystemIdleCPU < 20 THEN N'系統閒置率低於 20%'
            ELSE NULL
        END
    );
END;
GO

-- 3.2 阻塞監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBlocking')
    DROP PROCEDURE dbo.sp_MonitorBlocking;
GO

CREATE PROCEDURE dbo.sp_MonitorBlocking
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BlockingCount INT;
    DECLARE @BlockingInfo NVARCHAR(MAX);

    SELECT @BlockingCount = COUNT(*)
    FROM sys.dm_exec_requests r
    WHERE r.blocking_session_id <> 0;

    IF @BlockingCount > 0
    BEGIN
        SELECT @BlockingInfo = STRING_AGG(
            CAST(
                'Session ' + CAST(session_id AS NVARCHAR) +
                ' blocked by ' + CAST(blocking_session_id AS NVARCHAR) +
                ' (wait: ' + CAST(wait_time/1000 AS NVARCHAR) + 's)'
            AS NVARCHAR(MAX)),
            '; '
        )
        FROM sys.dm_exec_requests
        WHERE blocking_session_id <> 0;

        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, additional_info)
        VALUES (
            'Blocking',
            'Blocked Sessions',
            @BlockingCount,
            5,
            CASE
                WHEN @BlockingCount > 10 THEN 'CRITICAL'
                WHEN @BlockingCount > 5 THEN 'WARNING'
                ELSE 'OK'
            END,
            CASE
                WHEN @BlockingCount > 10 THEN N'嚴重阻塞: ' + CAST(@BlockingCount AS NVARCHAR) + N' 個會話被阻塞'
                WHEN @BlockingCount > 5 THEN N'偵測到阻塞: ' + CAST(@BlockingCount AS NVARCHAR) + N' 個會話被阻塞'
                ELSE NULL
            END,
            @BlockingInfo
        );
    END
    ELSE
    BEGIN
        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status)
        VALUES ('Blocking', 'Blocked Sessions', 0, 5, 'OK');
    END
END;
GO

-- 3.3 長時間執行查詢監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorLongQueries')
    DROP PROCEDURE dbo.sp_MonitorLongQueries;
GO

CREATE PROCEDURE dbo.sp_MonitorLongQueries
    @ThresholdSeconds INT = 300
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LongQueryCount INT;
    DECLARE @LongQueryInfo NVARCHAR(MAX);

    SELECT @LongQueryCount = COUNT(*)
    FROM sys.dm_exec_requests r
    WHERE r.status IN ('running', 'runnable')
        AND r.total_elapsed_time > (@ThresholdSeconds * 1000)
        AND r.session_id > 50;

    IF @LongQueryCount > 0
    BEGIN
        SELECT @LongQueryInfo = STRING_AGG(
            CAST(
                'Session ' + CAST(r.session_id AS NVARCHAR) +
                ' (' + CAST(r.total_elapsed_time/1000 AS NVARCHAR) + 's): ' +
                ISNULL(DB_NAME(r.database_id), 'N/A')
            AS NVARCHAR(MAX)),
            '; '
        )
        FROM sys.dm_exec_requests r
        WHERE r.status IN ('running', 'runnable')
            AND r.total_elapsed_time > (@ThresholdSeconds * 1000)
            AND r.session_id > 50;

        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, additional_info)
        VALUES (
            'LongQuery',
            'Long Running Queries',
            @LongQueryCount,
            3,
            CASE
                WHEN @LongQueryCount > 5 THEN 'CRITICAL'
                WHEN @LongQueryCount > 3 THEN 'WARNING'
                ELSE 'OK'
            END,
            N'偵測到 ' + CAST(@LongQueryCount AS NVARCHAR) + N' 個長時間執行的查詢 (>' + CAST(@ThresholdSeconds AS NVARCHAR) + N's)',
            @LongQueryInfo
        );
    END
    ELSE
    BEGIN
        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status)
        VALUES ('LongQuery', 'Long Running Queries', 0, 3, 'OK');
    END
END;
GO

-- 3.4 備份狀態監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBackups')
    DROP PROCEDURE dbo.sp_MonitorBackups;
GO

CREATE PROCEDURE dbo.sp_MonitorBackups
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NoBackupCount INT;
    DECLARE @NoBackupDBs NVARCHAR(MAX);

    SELECT
        @NoBackupCount = COUNT(*),
        @NoBackupDBs = STRING_AGG(CAST(d.name AS NVARCHAR(MAX)), ', ')
    FROM sys.databases d
    LEFT JOIN (
        SELECT
            database_name,
            MAX(backup_finish_date) AS last_backup_date
        FROM msdb.dbo.backupset
        WHERE type = 'D'
        GROUP BY database_name
    ) b ON d.name = b.database_name
    WHERE d.database_id > 4
        AND d.state = 0
        AND (b.last_backup_date IS NULL OR b.last_backup_date < DATEADD(HOUR, -24, GETDATE()));

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, additional_info)
    VALUES (
        'Backup',
        'Databases Without Recent Backup',
        @NoBackupCount,
        0,
        CASE
            WHEN @NoBackupCount > 5 THEN 'CRITICAL'
            WHEN @NoBackupCount > 0 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @NoBackupCount > 0 THEN N'有 ' + CAST(@NoBackupCount AS NVARCHAR) + N' 個資料庫超過24小時未備份'
            ELSE N'所有資料庫備份狀態正常'
        END,
        @NoBackupDBs
    );
END;
GO

-- 3.5 死結監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorDeadlocks')
    DROP PROCEDURE dbo.sp_MonitorDeadlocks;
GO

CREATE PROCEDURE dbo.sp_MonitorDeadlocks
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DeadlockCount INT = 0;

    SELECT @DeadlockCount = ISNULL(cntr_value, 0)
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Number of Deadlocks/sec'
        AND object_name LIKE '%Locks%';

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    VALUES (
        'Deadlock',
        'Deadlocks Detected',
        @DeadlockCount,
        5,
        CASE
            WHEN @DeadlockCount > 10 THEN 'CRITICAL'
            WHEN @DeadlockCount > 5 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @DeadlockCount > 10 THEN N'偵測到大量死結: ' + CAST(@DeadlockCount AS NVARCHAR)
            WHEN @DeadlockCount > 5 THEN N'偵測到死結: ' + CAST(@DeadlockCount AS NVARCHAR)
            ELSE NULL
        END
    );
END;
GO

-- 3.6 連線數監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorConnections')
    DROP PROCEDURE dbo.sp_MonitorConnections;
GO

CREATE PROCEDURE dbo.sp_MonitorConnections
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalConnections INT;
    DECLARE @ActiveConnections INT;
    DECLARE @MaxConnections INT;

    SELECT @TotalConnections = COUNT(*)
    FROM sys.dm_exec_sessions
    WHERE session_id > 50;

    SELECT @ActiveConnections = COUNT(*)
    FROM sys.dm_exec_requests;

    SELECT @MaxConnections = CAST(value AS INT)
    FROM sys.configurations
    WHERE name = 'user connections';

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    VALUES (
        'Connections',
        'Total Connections',
        @TotalConnections,
        CASE WHEN @MaxConnections = 0 THEN 500 ELSE @MaxConnections * 0.8 END,
        CASE
            WHEN @MaxConnections > 0 AND @TotalConnections > @MaxConnections * 0.9 THEN 'CRITICAL'
            WHEN @MaxConnections > 0 AND @TotalConnections > @MaxConnections * 0.8 THEN 'WARNING'
            WHEN @MaxConnections = 0 AND @TotalConnections > 500 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @MaxConnections > 0 AND @TotalConnections > @MaxConnections * 0.9
                THEN N'連線數接近上限: ' + CAST(@TotalConnections AS NVARCHAR) + '/' + CAST(@MaxConnections AS NVARCHAR)
            WHEN @TotalConnections > 500
                THEN N'連線數偏高: ' + CAST(@TotalConnections AS NVARCHAR)
            ELSE NULL
        END
    );

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status)
    VALUES ('Connections', 'Active Connections', @ActiveConnections, 100, 'OK');
END;
GO

-- 3.7 TempDB 監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTempDB')
    DROP PROCEDURE dbo.sp_MonitorTempDB;
GO

CREATE PROCEDURE dbo.sp_MonitorTempDB
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TempDBUsedMB DECIMAL(10,2);
    DECLARE @TempDBTotalMB DECIMAL(10,2);
    DECLARE @TempDBUsagePercent DECIMAL(5,2);

    SELECT
        @TempDBTotalMB = SUM(size * 8.0 / 1024),
        @TempDBUsedMB = SUM(FILEPROPERTY(name, 'SpaceUsed') * 8.0 / 1024)
    FROM sys.master_files
    WHERE database_id = DB_ID('tempdb');

    SET @TempDBUsagePercent = (@TempDBUsedMB / NULLIF(@TempDBTotalMB, 0)) * 100;

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, additional_info)
    VALUES (
        'TempDB',
        'TempDB Usage %',
        @TempDBUsagePercent,
        80,
        CASE
            WHEN @TempDBUsagePercent > 90 THEN 'CRITICAL'
            WHEN @TempDBUsagePercent > 80 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @TempDBUsagePercent > 90 THEN N'TempDB 使用率超過 90%'
            WHEN @TempDBUsagePercent > 80 THEN N'TempDB 使用率超過 80%'
            ELSE NULL
        END,
        N'Used: ' + CAST(@TempDBUsedMB AS NVARCHAR) + 'MB / Total: ' + CAST(@TempDBTotalMB AS NVARCHAR) + 'MB'
    );
END;
GO

-- 3.8 交易記錄監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTransactionLog')
    DROP PROCEDURE dbo.sp_MonitorTransactionLog;
GO

CREATE PROCEDURE dbo.sp_MonitorTransactionLog
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, database_name)
    SELECT
        'Transaction',
        'Log Space Used %',
        log_space_used_percent,
        80,
        CASE
            WHEN log_space_used_percent > 90 THEN 'CRITICAL'
            WHEN log_space_used_percent > 80 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN log_space_used_percent > 90 THEN database_name + N' 交易記錄使用率超過 90%'
            WHEN log_space_used_percent > 80 THEN database_name + N' 交易記錄使用率超過 80%'
            ELSE NULL
        END,
        database_name
    FROM (
        SELECT
            DB_NAME(database_id) AS database_name,
            CAST(used_log_space_in_bytes * 100.0 / NULLIF(total_log_size_in_bytes, 0) AS DECIMAL(5,2)) AS log_space_used_percent
        FROM sys.dm_db_log_space_usage
    ) t
    WHERE database_name NOT IN ('tempdb');
END;
GO

-- 3.9 SQL Agent 作業監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorSQLAgentJobs')
    DROP PROCEDURE dbo.sp_MonitorSQLAgentJobs;
GO

CREATE PROCEDURE dbo.sp_MonitorSQLAgentJobs
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FailedJobCount INT;
    DECLARE @FailedJobs NVARCHAR(MAX);

    SELECT
        @FailedJobCount = COUNT(DISTINCT j.name),
        @FailedJobs = STRING_AGG(CAST(j.name AS NVARCHAR(MAX)), ', ')
    FROM msdb.dbo.sysjobs j
    INNER JOIN msdb.dbo.sysjobhistory h ON j.job_id = h.job_id
    WHERE h.run_status = 0
        AND h.step_id = 0
        AND msdb.dbo.agent_datetime(h.run_date, h.run_time) >= DATEADD(HOUR, -24, GETDATE());

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message, additional_info)
    VALUES (
        'Jobs',
        'Failed Jobs (24h)',
        ISNULL(@FailedJobCount, 0),
        0,
        CASE
            WHEN @FailedJobCount > 5 THEN 'CRITICAL'
            WHEN @FailedJobCount > 0 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @FailedJobCount > 0 THEN N'最近24小時有 ' + CAST(@FailedJobCount AS NVARCHAR) + N' 個作業失敗'
            ELSE N'所有作業執行正常'
        END,
        @FailedJobs
    );
END;
GO

-- 3.10 效能計數器監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorPerformance')
    DROP PROCEDURE dbo.sp_MonitorPerformance;
GO

CREATE PROCEDURE dbo.sp_MonitorPerformance
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BatchRequests BIGINT;
    DECLARE @LazyWrites BIGINT;

    SELECT @BatchRequests = cntr_value
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Batch Requests/sec';

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status)
    VALUES ('Performance', 'Batch Requests/sec', ISNULL(@BatchRequests, 0), 10000, 'OK');

    SELECT @LazyWrites = cntr_value
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Lazy writes/sec'
        AND object_name LIKE '%Buffer Manager%';

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    VALUES (
        'Performance',
        'Lazy Writes/sec',
        ISNULL(@LazyWrites, 0),
        20,
        CASE
            WHEN @LazyWrites > 20 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE WHEN @LazyWrites > 20 THEN N'Lazy Writes 過高，可能有記憶體壓力' ELSE NULL END
    );
END;
GO

-- 3.11 記憶體與磁碟監控
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_LogHealthMetrics')
    DROP PROCEDURE dbo.sp_LogHealthMetrics;
GO

CREATE PROCEDURE dbo.sp_LogHealthMetrics
AS
BEGIN
    SET NOCOUNT ON;

    -- 記憶體使用率
    DECLARE @TargetMB BIGINT, @TotalMB BIGINT, @UsagePercent DECIMAL(5,2);

    SELECT @TargetMB = cntr_value/1024
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Target Server Memory (KB)'
        AND object_name LIKE '%Memory Manager%';

    SELECT @TotalMB = cntr_value/1024
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Total Server Memory (KB)'
        AND object_name LIKE '%Memory Manager%';

    SET @UsagePercent = CAST((@TotalMB * 100.0 / NULLIF(@TargetMB, 0)) AS DECIMAL(5,2));

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    SELECT
        'Memory',
        'Memory Usage %',
        @UsagePercent,
        85.00,
        CASE
            WHEN @UsagePercent > 95 THEN 'CRITICAL'
            WHEN @UsagePercent > 85 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN @UsagePercent > 95 THEN N'記憶體使用率超過 95%'
            WHEN @UsagePercent > 85 THEN N'記憶體使用率超過 85%'
            ELSE NULL
        END;

    -- Page Life Expectancy
    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    SELECT
        'Memory',
        'Page Life Expectancy',
        cntr_value,
        300,
        CASE
            WHEN cntr_value < 300 THEN 'CRITICAL'
            WHEN cntr_value < 600 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN cntr_value < 300 THEN N'PLE 低於 300 秒'
            WHEN cntr_value < 600 THEN N'PLE 低於 600 秒'
            ELSE NULL
        END
    FROM sys.dm_os_performance_counters
    WHERE counter_name = 'Page life expectancy'
        AND object_name LIKE '%Buffer Manager%';

    -- 磁碟空間
    CREATE TABLE #TempDrive (Drive CHAR(1), MB_Free INT);
    INSERT INTO #TempDrive EXEC master.dbo.xp_fixeddrives;

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, threshold_value, status, alert_message)
    SELECT
        'Disk',
        'Free Space MB - ' + Drive + ':',
        MB_Free,
        20480,
        CASE
            WHEN MB_Free < 5120 THEN 'CRITICAL'
            WHEN MB_Free < 10240 THEN 'WARNING'
            ELSE 'OK'
        END,
        CASE
            WHEN MB_Free < 5120 THEN N'磁碟 ' + Drive + N': 可用空間少於 5GB'
            WHEN MB_Free < 10240 THEN N'磁碟 ' + Drive + N': 可用空間少於 10GB'
            ELSE NULL
        END
    FROM #TempDrive;

    DROP TABLE #TempDrive;
END;
GO

-- 3.12 主監控程序
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MasterHealthCheck')
    DROP PROCEDURE dbo.sp_MasterHealthCheck;
GO

CREATE PROCEDURE dbo.sp_MasterHealthCheck
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @StartTime DATETIME = GETDATE();

    BEGIN TRY
        EXEC sp_LogHealthMetrics;
        EXEC sp_MonitorCPU;
        EXEC sp_MonitorBlocking;
        EXEC sp_MonitorLongQueries @ThresholdSeconds = 300;
        EXEC sp_MonitorBackups;
        EXEC sp_MonitorDeadlocks;
        EXEC sp_MonitorConnections;
        EXEC sp_MonitorTempDB;
        EXEC sp_MonitorTransactionLog;
        EXEC sp_MonitorSQLAgentJobs;
        EXEC sp_MonitorPerformance;

        DECLARE @Duration INT = DATEDIFF(SECOND, @StartTime, GETDATE());

        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, status, alert_message)
        VALUES ('System', 'Health Check Execution', @Duration, 'OK', N'監控執行成功，耗時 ' + CAST(@Duration AS NVARCHAR) + N' 秒');

    END TRY
    BEGIN CATCH
        INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, status, alert_message)
        VALUES ('System', 'Health Check Error', 0, 'CRITICAL', ERROR_MESSAGE());
    END CATCH
END;
GO

-- 3.13 清理程序
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_CleanupHealthLog')
    DROP PROCEDURE dbo.sp_CleanupHealthLog;
GO

CREATE PROCEDURE dbo.sp_CleanupHealthLog
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DeletedRows INT;
    DECLARE @CutoffDate DATETIME = DATEADD(DAY, -@RetentionDays, GETDATE());

    DELETE FROM DBA.dbo.ServerHealthLog
    WHERE check_time < @CutoffDate;

    SET @DeletedRows = @@ROWCOUNT;

    ALTER INDEX ALL ON DBA.dbo.ServerHealthLog REBUILD;

    INSERT INTO ServerHealthLog (check_type, metric_name, metric_value, status, alert_message)
    VALUES ('System', 'Log Cleanup', @DeletedRows, 'OK',
            N'已刪除 ' + CAST(@DeletedRows AS NVARCHAR) + N' 筆超過 ' + CAST(@RetentionDays AS NVARCHAR) + N' 天的記錄');
END;
GO

-- ==========================================
-- 步驟 4: 建立視圖
-- ==========================================

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_RecentAlerts')
    DROP VIEW dbo.vw_RecentAlerts;
GO

CREATE VIEW dbo.vw_RecentAlerts
AS
SELECT
    log_id,
    check_time,
    check_type,
    metric_name,
    metric_value,
    threshold_value,
    status,
    alert_message,
    database_name,
    additional_info
FROM dbo.ServerHealthLog
WHERE status IN ('WARNING', 'CRITICAL')
    AND check_time >= DATEADD(DAY, -7, GETDATE());
GO

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_CurrentStatus')
    DROP VIEW dbo.vw_CurrentStatus;
GO

CREATE VIEW dbo.vw_CurrentStatus
AS
WITH LatestMetrics AS (
    SELECT
        check_type,
        metric_name,
        metric_value,
        threshold_value,
        status,
        check_time,
        alert_message,
        ROW_NUMBER() OVER (PARTITION BY check_type, metric_name ORDER BY check_time DESC) AS rn
    FROM ServerHealthLog
)
SELECT
    check_type,
    metric_name,
    metric_value,
    threshold_value,
    status,
    check_time,
    alert_message,
    CASE
        WHEN status = 'CRITICAL' THEN 3
        WHEN status = 'WARNING' THEN 2
        WHEN status = 'OK' THEN 1
        ELSE 0
    END AS status_priority
FROM LatestMetrics
WHERE rn = 1;
GO

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_TrendAnalysis')
    DROP VIEW dbo.vw_TrendAnalysis;
GO

CREATE VIEW dbo.vw_TrendAnalysis
AS
SELECT
    check_time,
    check_type,
    metric_name,
    metric_value,
    threshold_value,
    status,
    CAST(check_time AS DATE) AS check_date,
    DATEPART(HOUR, check_time) AS check_hour
FROM ServerHealthLog
WHERE check_time >= DATEADD(DAY, -30, GETDATE());
GO

-- ==========================================
-- 步驟 5: 建立 SQL Agent 作業 - 每小時監控
-- ==========================================
USE msdb;
GO

IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Server Health Monitoring')
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Server Health Monitoring';
END

DECLARE @jobId BINARY(16);
EXEC msdb.dbo.sp_add_job
    @job_name = N'DBA - Server Health Monitoring',
    @enabled = 1,
    @description = N'每小時自動執行完整健康檢查',
    @category_name = N'Database Maintenance',
    @job_id = @jobId OUTPUT;

EXEC msdb.dbo.sp_add_jobstep
    @job_name = N'DBA - Server Health Monitoring',
    @step_name = N'Execute Health Check',
    @subsystem = N'TSQL',
    @command = N'EXEC DBA.dbo.sp_MasterHealthCheck;',
    @database_name = N'DBA',
    @on_success_action = 1,
    @on_fail_action = 2,
    @retry_attempts = 3,
    @retry_interval = 5;

EXEC msdb.dbo.sp_add_schedule
    @schedule_name = N'Hourly Health Check',
    @enabled = 1,
    @freq_type = 4,
    @freq_interval = 1,
    @freq_subday_type = 8,
    @freq_subday_interval = 1,
    @active_start_time = 0;

EXEC msdb.dbo.sp_attach_schedule
    @job_name = N'DBA - Server Health Monitoring',
    @schedule_name = N'Hourly Health Check';

EXEC msdb.dbo.sp_add_jobserver
    @job_name = N'DBA - Server Health Monitoring';
GO

-- ==========================================
-- 步驟 6: 建立 SQL Agent 作業 - 每日清理
-- ==========================================
IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Cleanup Health Log')
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Cleanup Health Log';
END

DECLARE @jobId2 BINARY(16);
EXEC msdb.dbo.sp_add_job
    @job_name = N'DBA - Cleanup Health Log',
    @enabled = 1,
    @description = N'每日清理超過30天的記錄',
    @category_name = N'Database Maintenance',
    @job_id = @jobId2 OUTPUT;

EXEC msdb.dbo.sp_add_jobstep
    @job_name = N'DBA - Cleanup Health Log',
    @step_name = N'Cleanup Old Records',
    @subsystem = N'TSQL',
    @command = N'EXEC DBA.dbo.sp_CleanupHealthLog @RetentionDays = 30;',
    @database_name = N'DBA';

EXEC msdb.dbo.sp_add_schedule
    @schedule_name = N'Daily Cleanup at 2AM',
    @enabled = 1,
    @freq_type = 4,
    @freq_interval = 1,
    @freq_subday_type = 1,
    @active_start_time = 20000;

EXEC msdb.dbo.sp_attach_schedule
    @job_name = N'DBA - Cleanup Health Log',
    @schedule_name = N'Daily Cleanup at 2AM';

EXEC msdb.dbo.sp_add_jobserver
    @job_name = N'DBA - Cleanup Health Log';
GO

-- ==========================================
-- 步驟 7: 執行初次監控
-- ==========================================
USE DBA;
GO

EXEC dbo.sp_MasterHealthCheck;
GO
