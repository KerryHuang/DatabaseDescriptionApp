using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// SQL Agent Job 管理服務介面
/// </summary>
public interface IAgentJobService
{
    /// <summary>取得 TableSpec 相關的 Agent Job 清單</summary>
    Task<IReadOnlyList<AgentJobInfo>> GetJobsAsync(CancellationToken ct = default);

    /// <summary>設定 Job 啟用或停用</summary>
    Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default);

    /// <summary>立即執行 Job</summary>
    Task StartJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>刪除 Job</summary>
    Task DeleteJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>更新 Job 排程</summary>
    Task UpdateScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default);

    /// <summary>取得 Job 執行歷史</summary>
    Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default);
}
