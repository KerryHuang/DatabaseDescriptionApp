-- 查看健康報表
EXEC DBA.dbo.sp_HealthReport;

-- 查看當前狀態
SELECT * FROM DBA.dbo.vw_CurrentStatus;

-- 查看最近告警
SELECT * FROM DBA.dbo.vw_RecentAlerts;

----資料庫等候事件
WITH Waits AS
	(SELECT
		wait_type,
		wait_time_ms / 1000.0 AS WaitS,
		(wait_time_ms - signal_wait_time_ms) / 1000.0 AS ResourceS,
		signal_wait_time_ms / 1000.0 AS SignalS,
		waiting_tasks_count AS WaitCount,
		100.0 * wait_time_ms / SUM (wait_time_ms) OVER() AS Percentage
	FROM sys.dm_os_wait_stats
	--/* 不須觀察的系統等待事件
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
		--*/
	)
SELECT
	W1.wait_type [等待類型],
	CAST (W1.WaitS AS DECIMAL(14, 2)) [等待(秒)],
	CAST (W1.ResourceS AS DECIMAL(14, 2)) [等待資源(秒)],
	CAST (W1.SignalS AS DECIMAL(14, 2)) [執行緒獲得資源進入 runnable 	queue到開始執行的時間(秒)],
	W1.WaitCount [等待次數],
	CAST (W1.Percentage AS DECIMAL(4, 2)) [等待時間所占全部等待百分率],
	CAST ((W1.WaitS / CASE WHEN W1.WaitCount=0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL (14, 4)) [平均等待時間(秒)],
	CAST ((W1.ResourceS / CASE WHEN W1.WaitCount=0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL (14, 4)) [平均等待資源(秒)],
	CAST ((W1.SignalS / CASE WHEN W1.WaitCount=0 THEN 1 ELSE W1.WaitCount END) AS DECIMAL (14, 4)) [平均執行緒獲得資源進入 runnable queue到開始執行的時間(秒)]
FROM Waits W1
order by [等待(秒)] desc



----最耗時的前五句
SELECT TOP 5
        dest.text
       ,deqs.execution_count
       ,deqs.total_elapsed_time
       ,deqs.max_elapsed_time
FROM    sys.dm_exec_query_stats AS deqs
        CROSS APPLY sys.dm_exec_sql_text(deqs.sql_handle) AS dest
ORDER BY deqs.max_elapsed_time DESC

----最耗時的前 5 的 sp
SELECT TOP 5
		db_name(deps.database_id) db,
		OBJECT_NAME(deps.object_id,deps.database_id) spName,
        dest.text
       ,deps.execution_count
       ,deps.total_elapsed_time
       ,deps.max_elapsed_time
FROM    Sys.dm_exec_procedure_stats AS deps
        outer APPLY sys.dm_exec_sql_text(deps.sql_handle) AS dest
ORDER BY deps.max_elapsed_time DESC


----耗資源的語法或預存程序
SELECT TOP 5 SUBSTRING(dest.text, (deqs.statement_start_offset/2)+1,
((CASE deqs.statement_end_offset WHEN -1 THEN DATALENGTH(dest.text)
  ELSE deqs.statement_end_offset END - deqs.statement_start_offset)/2) + 1) [查詢語法],
  total_worker_time/execution_count [平均CPU 使用時間(毫秒)],
total_worker_time [總耗時],execution_count [執行次數],deqp.query_plan [執行計畫]
FROM sys.dm_exec_query_stats AS deqs
CROSS APPLY sys.dm_exec_sql_text(deqs.sql_handle) AS dest
CROSS APPLY sys.dm_exec_query_plan(deqs.plan_handle) AS deqp
ORDER BY total_worker_time/execution_count DESC

----索引狀態(會跑很久)
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED
drop table if exists #tmp
SELECT top(0) CONVERT(sysname,'?') as DBName
	  ,s.name as SchemaName
      ,OBJECT_NAME(idx.[object_id]) as [TableName]
      ,idx.[name] as [IndexName]
	  ,idx.type_desc as IndexType
	  ,(SUM(ddps.[used_page_count]) * 8.) / 1024 AS [IndexSizeMB]
      ,ddius.[user_seeks]
      ,ddius.[user_scans]
      ,ddius.[user_lookups]
      ,ddius.[user_updates]
      ,ddius.[last_user_seek]
      ,ddius.[last_user_scan]
      ,ddius.[last_user_lookup]
      ,ddius.[last_user_update]
	  ,avg_fragmentation_in_percent
	  ,avg_page_space_used_in_percent
INTO #tmp
FROM [sys].[dm_db_index_usage_stats] ddius
INNER JOIN [sys].[indexes] idx ON idx.[index_id] = ddius.[index_id] AND idx.[object_id] = ddius.[object_id]
INNER JOIN [sys].[dm_db_index_physical_stats] (DB_ID(),NULL,NULL,NULL,NULL) AS PHS ON PHS.[index_id] = ddius.[index_id] AND PHS.[object_id] = ddius.[object_id]
INNER JOIN [sys].[dm_db_partition_stats] ddps ON ddps.[index_id] = ddius.[index_id] AND ddps.[object_id] = ddius.[object_id]
INNER JOIN [sys].[databases] DB ON DB.[database_id] = PHS.[database_id]
INNER JOIN [sys].[tables] AS t ON t.[object_id] = idx.[object_id]
INNER JOIN [sys].[schemas] AS s ON s.[schema_id]=t.[schema_id]
WHERE 1=2
GROUP BY DB.[name],s.name,idx.object_id,idx.name,idx.type_desc,ddius.user_updates,ddius.user_seeks,ddius.user_scans,ddius.user_lookups,ddius.last_user_seek,ddius.last_user_scan,ddius.last_user_lookup,ddius.last_user_update
,avg_fragmentation_in_percent,avg_page_space_used_in_percent

	exec sp_MSforeachdb 'use [?];
	DECLARE @DBID int
	SET @DBID = DB_ID(''?'')

	IF (@DBID > 4)
	BEGIN
		INSERT INTO #tmp
		SELECT  ''?'' as DBName
			  ,s.name as SchemaName
			  ,OBJECT_NAME(idx.[object_id]) as [TableName]
			  ,idx.[name] as [IndexName]
			  ,idx.type_desc as IndexType
			  ,(SUM(ddps.[used_page_count]) * 8) / 1024 AS [IndexSizeMB]
			  ,ddius.[user_seeks]
			  ,ddius.[user_scans]
			  ,ddius.[user_lookups]
			  ,ddius.[user_updates]
			  ,ddius.[last_user_seek]
			  ,ddius.[last_user_scan]
			  ,ddius.[last_user_lookup]
			  ,ddius.[last_user_update]
			  ,avg_fragmentation_in_percent
			  ,avg_page_space_used_in_percent
		FROM [sys].[dm_db_index_usage_stats] ddius
		INNER JOIN [sys].[indexes] idx ON idx.[index_id] = ddius.[index_id] AND idx.[object_id] = ddius.[object_id]
		INNER JOIN [sys].[dm_db_index_physical_stats] (DB_ID(),NULL,NULL,NULL,''SAMPLED'') AS PHS ON PHS.[index_id] = ddius.[index_id] AND PHS.[object_id] = ddius.[object_id]
		INNER JOIN [sys].[dm_db_partition_stats] ddps ON ddps.[index_id] = ddius.[index_id] AND ddps.[object_id] = ddius.[object_id]
		INNER JOIN [sys].[databases] DB ON DB.[database_id] = PHS.[database_id]
		INNER JOIN [sys].[tables] AS t ON t.[object_id] = idx.[object_id]
		INNER JOIN [sys].[schemas] AS s ON s.[schema_id]=t.[schema_id]
		GROUP BY DB.[name],s.name,idx.object_id,idx.name,idx.type_desc,ddius.user_updates,ddius.user_seeks,ddius.user_scans,ddius.user_lookups,ddius.last_user_seek,ddius.last_user_scan,ddius.last_user_lookup,ddius.last_user_update
		,avg_fragmentation_in_percent,avg_page_space_used_in_percent
	END'

		SELECT
				[DBName],
				[SchemaName],
				[TableName],
				[IndexName],
				[IndexType],
				[IndexSizeMB],
			    [user_seeks],
			    [user_scans],
			    [user_lookups],
			    [user_updates],
			    [last_user_seek],
			    [last_user_scan],
			    [last_user_lookup],
			    [last_user_update],
			    convert(decimal(5,2),avg_fragmentation_in_percent) avg_fragmentation_in_percent,
			    convert(decimal(5,2),avg_page_space_used_in_percent) avg_page_space_used_in_percent
FROM #tmp t

----missing index(會跑很久)

		SELECT mid.statement,
			migs.avg_total_user_cost * (migs.avg_user_impact / 100.0) * (migs.user_seeks + migs.user_scans) AS improvement_measure,
			'CREATE INDEX [missing_index_' + CONVERT (varchar, mig.index_group_handle) + '_' + CONVERT (varchar, mid.index_handle)
			+ '_' + LEFT (PARSENAME(mid.statement, 1), 32) + ']'
			+ ' ON ' + mid.statement
			+ ' (' + ISNULL (mid.equality_columns,'')
			+ CASE WHEN mid.equality_columns IS NOT NULL AND mid.inequality_columns IS NOT NULL THEN ',' ELSE '' END
			+ ISNULL (mid.inequality_columns, '')
			+ ')'
			+ ISNULL (' INCLUDE (' + mid.included_columns + ')', '') AS create_index_statement,
			--migs.*,migs.group_handle,
			migs.unique_compiles,migs.user_seeks,migs.user_scans,
			convert(varchar,migs.last_user_seek,120) last_user_seek,convert(varchar,migs.last_user_scan,120) last_user_scan,
			migs.avg_total_user_cost,migs.avg_user_impact,migs.system_seeks,migs.system_scans,
			convert(varchar,migs.last_system_seek,120) last_system_seek,convert(varchar,migs.last_system_scan,120) last_system_scan,
			migs.avg_total_system_cost,migs.avg_system_impact
			--mid.database_id, mid.[object_id],
		FROM sys.dm_db_missing_index_groups mig
		INNER JOIN sys.dm_db_missing_index_group_stats migs ON migs.group_handle = mig.index_group_handle
		INNER JOIN sys.dm_db_missing_index_details mid ON mig.index_handle = mid.index_handle
		ORDER BY migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) DESC

SELECT
    mid.statement AS 資料表名稱,
    migs.avg_total_user_cost * (migs.avg_user_impact / 100.0) * (migs.user_seeks + migs.user_scans) AS 效能改善指標,
    'CREATE INDEX [missing_index_' + CONVERT(varchar, mig.index_group_handle) + '_' + CONVERT(varchar, mid.index_handle)
    + '_' + LEFT(PARSENAME(mid.statement, 1), 32) + ']'
    + ' ON ' + mid.statement
    + ' (' + ISNULL(mid.equality_columns,'')
    + CASE WHEN mid.equality_columns IS NOT NULL AND mid.inequality_columns IS NOT NULL THEN ',' ELSE '' END
    + ISNULL(mid.inequality_columns, '')
    + ')'
    + ISNULL(' INCLUDE (' + mid.included_columns + ')', '') AS 建議索引語法,
    migs.unique_compiles AS 查詢編譯次數,
    migs.user_seeks AS 使用者Seek次數,
    migs.user_scans AS 使用者Scan次數,
    CONVERT(varchar, migs.last_user_seek, 120) AS 最後Seek時間,
    CONVERT(varchar, migs.last_user_scan, 120) AS 最後Scan時間,
    migs.avg_total_user_cost AS 平均使用者查詢成本,
    migs.avg_user_impact AS 預估改善百分比,
    migs.system_seeks AS 系統Seek次數,
    migs.system_scans AS 系統Scan次數,
    CONVERT(varchar, migs.last_system_seek, 120) AS 最後系統Seek時間,
    CONVERT(varchar, migs.last_system_scan, 120) AS 最後系統Scan時間,
    migs.avg_total_system_cost AS 平均系統查詢成本,
    migs.avg_system_impact AS 系統查詢改善百分比
FROM sys.dm_db_missing_index_groups mig
INNER JOIN sys.dm_db_missing_index_group_stats migs ON migs.group_handle = mig.index_group_handle
INNER JOIN sys.dm_db_missing_index_details mid ON mig.index_handle = mid.index_handle
ORDER BY migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans) DESC

----統計的更新狀況
SELECT sch.name + '.' + so.name AS "Table",
    ss.name AS "Statistic",
    CASE
        WHEN ss.auto_Created = 0 AND ss.user_created = 0 THEN 'Index Statistic'
        WHEN ss.auto_created = 0 AND ss.user_created = 1 THEN 'User Created'
        WHEN ss.auto_created = 1 AND ss.user_created = 0 THEN 'Auto Created'
        WHEN ss.AUTO_created = 1 AND ss.user_created = 1 THEN 'Not Possible?'
    END AS "Statistic Type",
    CASE
        WHEN ss.has_filter = 1 THEN 'Filtered Index'
        WHEN ss.has_filter = 0 THEN 'No Filter'
    END AS "Filtered?",
    CASE
        WHEN ss.filter_definition IS NULL THEN ''
        WHEN ss.filter_definition IS NOT NULL THEN ss.filter_definition
    END AS "Filter Definition",
    sp.last_updated AS "Stats Last Updated",
    sp.rows AS "Rows",
    sp.rows_sampled AS "Rows Sampled",
    sp.unfiltered_rows AS "Unfiltered Rows",
    sp.modification_counter AS "Row Modifications",
    sp.steps AS "Histogram Steps",
	persisted_sample_percent
FROM sys.stats ss
JOIN sys.objects so ON ss.object_id = so.object_id
JOIN sys.schemas sch ON so.schema_id = sch.schema_id
OUTER APPLY sys.dm_db_stats_properties(so.object_id, ss.stats_id) AS sp
WHERE so.TYPE = 'U'
--AND sp.last_updated <getdate() - 30
ORDER BY sp.last_updated DESC;


----資料庫錯誤訊息，帶ERRORLOG檔
EXEC master.dbo.xp_readerrorlog
