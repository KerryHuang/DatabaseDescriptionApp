namespace Specurai.Domain.Entities;

/// <summary>單一 DB 的 CHECKDB 原始查詢結果(Health 分級由 Service 計算)</summary>
public class LastCheckDbRow
{
    public required string DatabaseName { get; init; }
    public DateTime? LastKnownGood { get; init; }
}
