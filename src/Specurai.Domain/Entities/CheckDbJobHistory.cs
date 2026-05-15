namespace Specurai.Domain.Entities;

/// <summary>CHECKDB SQL Agent Job 的單筆執行紀錄</summary>
public class CheckDbJobHistory
{
    public required string JobName { get; init; }
    public required DateTime RunAt { get; init; }
    public required TimeSpan Duration { get; init; }
    /// <summary>原始 run_status：1=成功 0=失敗 3=取消 4=重試</summary>
    public required int RunStatus { get; init; }
    public required string Message { get; init; }

    public string StatusText => RunStatus switch
    {
        1 => "成功",
        0 => "失敗",
        3 => "取消",
        4 => "重試",
        _ => "其他"
    };
}
