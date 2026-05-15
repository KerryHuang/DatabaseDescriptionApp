namespace Specurai.Domain.Entities;

/// <summary>資料庫完整性健康分級</summary>
public enum IntegrityHealth
{
    Healthy,
    Warning,
    Critical,
    Unknown
}

/// <summary>單一資料庫的 CHECKDB 健康狀態</summary>
public class IntegrityCheckStatus
{
    public required string DatabaseName { get; init; }
    /// <summary>最後一次成功 CHECKDB 的時間；null 表示從未或無法判斷</summary>
    public DateTime? LastKnownGood { get; init; }
    /// <summary>距今天數；null 表示無資料</summary>
    public int? DaysSince { get; init; }
    public required IntegrityHealth Health { get; init; }
}
