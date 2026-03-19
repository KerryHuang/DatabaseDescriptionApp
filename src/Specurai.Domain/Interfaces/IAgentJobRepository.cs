using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL Agent Job 資料存取介面
/// </summary>
public interface IAgentJobRepository
{
    /// <summary>
    /// 取得 Specurai 相關的 Agent Job 清單
    /// </summary>
    Task<IReadOnlyList<AgentJobInfo>> GetSpecuraiJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得指定 Job 的執行歷史紀錄
    /// </summary>
    /// <param name="jobId">Job 識別碼</param>
    /// <param name="maxRecords">最大回傳筆數</param>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default);

    /// <summary>
    /// 設定 Job 啟用或停用狀態
    /// </summary>
    /// <param name="jobId">Job 識別碼</param>
    /// <param name="enabled">是否啟用</param>
    /// <param name="ct">取消權杖</param>
    Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// 立即執行指定的 Job
    /// </summary>
    /// <param name="jobId">Job 識別碼</param>
    /// <param name="ct">取消權杖</param>
    Task StartJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// 刪除指定的 Job
    /// </summary>
    /// <param name="jobId">Job 識別碼</param>
    /// <param name="ct">取消權杖</param>
    Task DeleteJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// 更新 Job 的排程設定
    /// </summary>
    /// <param name="jobId">Job 識別碼</param>
    /// <param name="freqType">頻率類型</param>
    /// <param name="freqInterval">頻率間隔</param>
    /// <param name="activeStartTime">排程開始時間（HHMMSS 格式）</param>
    /// <param name="ct">取消權杖</param>
    Task UpdateJobScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default);

    /// <summary>
    /// 取得所有未標記 [Specurai] 的 Agent Job 清單
    /// </summary>
    Task<IReadOnlyList<AgentJobInfo>> GetNonSpecuraiJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// 將指定 Job 的 Description 加上 [Specurai] 標記
    /// </summary>
    Task ImportJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// 檢查 SQL Server Agent 服務是否正在執行
    /// </summary>
    Task<bool> IsAgentRunningAsync(CancellationToken ct = default);

    /// <summary>
    /// 檢查目前使用者是否有 Agent 相關權限
    /// </summary>
    Task<bool> HasAgentPermissionAsync(CancellationToken ct = default);
}
