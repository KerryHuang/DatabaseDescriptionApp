namespace TableSpec.Domain.Entities;

/// <summary>
/// 多環境使用狀態比對結果
/// </summary>
public class UsageComparison
{
    /// <summary>基準環境名稱</summary>
    public required string BaseEnvironment { get; init; }

    /// <summary>目標環境名稱清單</summary>
    public IReadOnlyList<string> TargetEnvironments { get; init; } = [];

    /// <summary>資料表層級比對列</summary>
    public IReadOnlyList<TableUsageComparisonRow> TableRows { get; init; } = [];

    /// <summary>欄位層級比對列</summary>
    public IReadOnlyList<ColumnUsageComparisonRow> ColumnRows { get; init; } = [];
}

/// <summary>
/// 資料表使用狀態比對列（跨環境）
/// </summary>
public class TableUsageComparisonRow
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }

    /// <summary>各環境的使用狀態（null 表示該環境不存在此表）</summary>
    public Dictionary<string, TableUsageInfo?> EnvironmentStatus { get; init; } = new();
}

/// <summary>
/// 欄位使用狀態比對列（跨環境）
/// </summary>
public class ColumnUsageComparisonRow
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string ColumnName { get; init; }

    /// <summary>各環境的使用狀態（null 表示該環境不存在此欄位）</summary>
    public Dictionary<string, ColumnUsageStatus?> EnvironmentStatus { get; init; } = new();
}
