namespace TableSpec.Domain.Entities;

/// <summary>
/// 索引資訊
/// </summary>
public class IndexInfo
{
    /// <summary>
    /// 索引名稱
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 索引類型（Clustered、NonClustered）
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 索引包含的欄位
    /// </summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>
    /// 是否為唯一索引
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// 是否為主鍵索引
    /// </summary>
    public bool IsPrimaryKey { get; init; }
}
