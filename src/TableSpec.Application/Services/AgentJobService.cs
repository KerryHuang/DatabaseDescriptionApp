using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// SQL Agent Job 管理服務實作
/// </summary>
public class AgentJobService : IAgentJobService
{
    private readonly IAgentJobRepository _repository;

    public AgentJobService(IAgentJobRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<AgentJobInfo>> GetJobsAsync(CancellationToken ct = default)
        => _repository.GetTableSpecJobsAsync(ct);

    public Task SetJobEnabledAsync(Guid jobId, bool enabled, CancellationToken ct = default)
        => _repository.SetJobEnabledAsync(jobId, enabled, ct);

    public Task StartJobAsync(Guid jobId, CancellationToken ct = default)
        => _repository.StartJobAsync(jobId, ct);

    public Task DeleteJobAsync(Guid jobId, CancellationToken ct = default)
        => _repository.DeleteJobAsync(jobId, ct);

    public Task UpdateScheduleAsync(Guid jobId, int freqType, int freqInterval, int activeStartTime, CancellationToken ct = default)
        => _repository.UpdateJobScheduleAsync(jobId, freqType, freqInterval, activeStartTime, ct);

    public Task<IReadOnlyList<AgentJobHistory>> GetJobHistoryAsync(Guid jobId, int maxRecords = 20, CancellationToken ct = default)
        => _repository.GetJobHistoryAsync(jobId, maxRecords, ct);
}
