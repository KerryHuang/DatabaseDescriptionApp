namespace Specurai.Domain.Entities;

/// <summary>
/// SQL Agent Job 資訊
/// </summary>
public class AgentJobInfo
{
    public required Guid JobId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsEnabled { get; init; }
    public DateTime? LastRunDate { get; init; }
    public int? LastRunOutcome { get; init; }
    public DateTime? NextRunDate { get; init; }
    public int? ScheduleTime { get; init; }
    public int? ScheduleFreqType { get; init; }

    public bool IsSpecuraiJob => Description?.Contains("[Specurai]") == true;

    public string LastRunOutcomeText => LastRunOutcome switch
    {
        0 => "失敗",
        1 => "成功",
        3 => "取消",
        5 => "未知",
        _ => "無記錄"
    };

    public string StatusText => IsEnabled ? "啟用" : "停用";
}
