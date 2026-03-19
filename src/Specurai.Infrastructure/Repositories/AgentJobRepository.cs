using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL Agent Job 資料存取 Repository 實作
/// </summary>
public class AgentJobRepository : IAgentJobRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public AgentJobRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<AgentJobInfo>> GetSpecuraiJobsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<AgentJobInfo>();

        const string sql = @"
SELECT
    j.job_id AS JobId,
    j.name AS Name,
    j.description AS Description,
    CAST(j.[enabled] AS BIT) AS IsEnabled,
    h.last_run_date AS LastRunDateInt,
    h.last_run_time AS LastRunTimeInt,
    h.last_run_outcome AS LastRunOutcome,
    ja.next_scheduled_run_date AS NextRunDate,
    sch.active_start_time AS ScheduleTime,
    sch.freq_type AS ScheduleFreqType
FROM msdb.dbo.sysjobs j
LEFT JOIN (
    SELECT job_id,
           MAX(run_date) AS last_run_date,
           MAX(run_time) AS last_run_time,
           MAX(run_status) AS last_run_outcome
    FROM msdb.dbo.sysjobhistory
    WHERE step_id = 0
    GROUP BY job_id
) h ON j.job_id = h.job_id
LEFT JOIN msdb.dbo.sysjobschedules s ON j.job_id = s.job_id
LEFT JOIN msdb.dbo.sysschedules sch ON s.schedule_id = sch.schedule_id
LEFT JOIN (
    SELECT job_id, next_scheduled_run_date
    FROM msdb.dbo.sysjobactivity
    WHERE session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
) ja ON j.job_id = ja.job_id
WHERE j.description LIKE '%[[]Specurai]%'
ORDER BY j.name";

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(r => new AgentJobInfo
        {
            JobId = r.JobId,
            Name = r.Name,
            Description = (string?)r.Description,
            IsEnabled = r.IsEnabled,
            LastRunDate = ConvertToDateTime((int?)r.LastRunDateInt, (int?)r.LastRunTimeInt),
            LastRunOutcome = (int?)r.LastRunOutcome,
            NextRunDate = (DateTime?)r.NextRunDate,
            ScheduleTime = (int?)r.ScheduleTime,
            ScheduleFreqType = (int?)r.ScheduleFreqType
        }).ToList();
    }

    public async Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<AgentJobHistory>();

        const string sql = @"
SELECT TOP(@MaxRecords)
    job_id AS JobId,
    step_name AS StepName,
    run_date AS RunDateInt,
    run_time AS RunTimeInt,
    run_status AS RunStatus,
    run_duration AS RunDurationInt,
    message AS Message
FROM msdb.dbo.sysjobhistory
WHERE job_id = @JobId
ORDER BY instance_id DESC";

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(sql, new { JobId = jobId, MaxRecords = maxRecords }, cancellationToken: ct));

        return rows.Select(r => new AgentJobHistory
        {
            JobId = r.JobId,
            StepName = r.StepName,
            RunDate = ConvertToDateTime((int?)r.RunDateInt, (int?)r.RunTimeInt) ?? DateTime.MinValue,
            RunStatus = r.RunStatus,
            DurationSeconds = ConvertDurationToSeconds((int?)r.RunDurationInt),
            Message = (string?)r.Message
        }).ToList();
    }

    public async Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        const string sql = "EXEC msdb.dbo.sp_update_job @job_id = @JobId, @enabled = @Enabled";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { JobId = jobId, Enabled = enabled ? 1 : 0 }, cancellationToken: ct));
    }

    public async Task StartJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        const string sql = "EXEC msdb.dbo.sp_start_job @job_id = @JobId";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { JobId = jobId }, cancellationToken: ct));
    }

    public async Task DeleteJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        const string sql = "EXEC msdb.dbo.sp_delete_job @job_id = @JobId, @delete_unused_schedule = 1";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { JobId = jobId }, cancellationToken: ct));
    }

    public async Task UpdateJobScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        const string sql = @"
DECLARE @schedule_id INT;
SELECT @schedule_id = schedule_id FROM msdb.dbo.sysjobschedules WHERE job_id = @JobId;
IF @schedule_id IS NOT NULL
    EXEC msdb.dbo.sp_update_schedule
        @schedule_id = @schedule_id,
        @freq_type = @FreqType,
        @freq_interval = @FreqInterval,
        @active_start_time = @ActiveStartTime";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { JobId = jobId, FreqType = freqType, FreqInterval = freqInterval, ActiveStartTime = activeStartTime }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AgentJobInfo>> GetNonSpecuraiJobsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<AgentJobInfo>();

        const string sql = @"
SELECT
    j.job_id AS JobId,
    j.name AS Name,
    j.description AS Description,
    CAST(j.[enabled] AS BIT) AS IsEnabled,
    h.last_run_date AS LastRunDateInt,
    h.last_run_time AS LastRunTimeInt,
    h.last_run_outcome AS LastRunOutcome,
    ja.next_scheduled_run_date AS NextRunDate,
    sch.active_start_time AS ScheduleTime,
    sch.freq_type AS ScheduleFreqType
FROM msdb.dbo.sysjobs j
LEFT JOIN (
    SELECT job_id,
           MAX(run_date) AS last_run_date,
           MAX(run_time) AS last_run_time,
           MAX(run_status) AS last_run_outcome
    FROM msdb.dbo.sysjobhistory
    WHERE step_id = 0
    GROUP BY job_id
) h ON j.job_id = h.job_id
LEFT JOIN msdb.dbo.sysjobschedules s ON j.job_id = s.job_id
LEFT JOIN msdb.dbo.sysschedules sch ON s.schedule_id = sch.schedule_id
LEFT JOIN (
    SELECT job_id, next_scheduled_run_date
    FROM msdb.dbo.sysjobactivity
    WHERE session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
) ja ON j.job_id = ja.job_id
WHERE j.description NOT LIKE '%[[]Specurai]%'
   OR j.description IS NULL
ORDER BY j.name";

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(sql, cancellationToken: ct));

        return rows.Select(r => new AgentJobInfo
        {
            JobId = r.JobId,
            Name = r.Name,
            Description = (string?)r.Description,
            IsEnabled = r.IsEnabled,
            LastRunDate = ConvertToDateTime((int?)r.LastRunDateInt, (int?)r.LastRunTimeInt),
            LastRunOutcome = (int?)r.LastRunOutcome,
            NextRunDate = (DateTime?)r.NextRunDate,
            ScheduleTime = (int?)r.ScheduleTime,
            ScheduleFreqType = (int?)r.ScheduleFreqType
        }).ToList();
    }

    public async Task ImportJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        const string sql = @"
DECLARE @desc NVARCHAR(512);
SELECT @desc = ISNULL(j.description, N'') FROM msdb.dbo.sysjobs j WHERE j.job_id = @JobId;
SET @desc = CASE WHEN @desc = N'' THEN N'[Specurai]' ELSE N'[Specurai] ' + @desc END;
EXEC msdb.dbo.sp_update_job @job_id = @JobId, @description = @desc";

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { JobId = jobId }, cancellationToken: ct));
    }

    public async Task<bool> IsAgentRunningAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        const string sql = "SELECT COUNT(1) FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%' AND status = 4";

        await using var connection = new SqlConnection(connectionString);
        try
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
            return count > 0;
        }
        catch
        {
            // dm_server_services 在 Azure SQL 或權限不足時可能無法存取
            return false;
        }
    }

    public async Task<bool> HasAgentPermissionAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        const string sql = @"
SELECT COUNT(1)
FROM msdb.sys.database_role_members rm
JOIN msdb.sys.database_principals r ON rm.role_principal_id = r.principal_id
JOIN msdb.sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE r.name IN ('SQLAgentOperatorRole', 'SQLAgentReaderRole', 'SQLAgentUserRole', 'db_owner', 'sysadmin')
  AND m.name = SUSER_SNAME()";

        await using var connection = new SqlConnection(connectionString);
        try
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 將 SQL Agent 的日期整數（YYYYMMDD）和時間整數（HHMMSS）轉換為 DateTime
    /// </summary>
    private static DateTime? ConvertToDateTime(int? dateInt, int? timeInt)
    {
        if (dateInt is null or 0)
            return null;

        var year = dateInt.Value / 10000;
        var month = (dateInt.Value % 10000) / 100;
        var day = dateInt.Value % 100;

        var hour = 0;
        var minute = 0;
        var second = 0;
        if (timeInt.HasValue)
        {
            hour = timeInt.Value / 10000;
            minute = (timeInt.Value % 10000) / 100;
            second = timeInt.Value % 100;
        }

        try
        {
            return new DateTime(year, month, day, hour, minute, second);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 將 SQL Agent 的持續時間整數（HHMMSS）轉換為秒數
    /// </summary>
    private static int ConvertDurationToSeconds(int? durationInt)
    {
        if (durationInt is null)
            return 0;

        var hours = durationInt.Value / 10000;
        var minutes = (durationInt.Value % 10000) / 100;
        var seconds = durationInt.Value % 100;

        return hours * 3600 + minutes * 60 + seconds;
    }
}
