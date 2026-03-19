namespace TableSpec.Domain.Entities;

/// <summary>
/// SQL Agent Job 執行歷史
/// </summary>
public class AgentJobHistory
{
    public required Guid JobId { get; init; }
    public required string StepName { get; init; }
    public required DateTime RunDate { get; init; }
    public required int RunStatus { get; init; }
    public required int DurationSeconds { get; init; }
    public string? Message { get; init; }

    public string RunStatusText => RunStatus switch
    {
        0 => "失敗",
        1 => "成功",
        _ => "未知"
    };
}
