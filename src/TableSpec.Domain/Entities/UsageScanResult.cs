namespace TableSpec.Domain.Entities;

/// <summary>
/// 使用狀態掃描結果
/// </summary>
public class UsageScanResult
{
    /// <summary>資料表使用狀態清單</summary>
    public IReadOnlyList<TableUsageInfo> Tables { get; init; } = [];

    /// <summary>欄位使用狀態清單</summary>
    public IReadOnlyList<ColumnUsageStatus> Columns { get; init; } = [];

    /// <summary>時間門檻（年）</summary>
    public int YearsThreshold { get; init; }

    /// <summary>未使用資料表數量</summary>
    public int UnusedTableCount(int years) =>
        Tables.Count(t => !t.HasQueryActivity && !t.HasRecentUpdate(years));

    /// <summary>使用中資料表數量</summary>
    public int UsedTableCount(int years) =>
        Tables.Count - UnusedTableCount(years);

    /// <summary>未使用欄位數量</summary>
    public int UnusedColumnCount => Columns.Count(c => !c.IsUsed);

    /// <summary>使用中欄位數量</summary>
    public int UsedColumnCount => Columns.Count(c => c.IsUsed);
}
