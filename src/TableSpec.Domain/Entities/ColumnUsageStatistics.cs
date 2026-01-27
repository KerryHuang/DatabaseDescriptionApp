namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位使用統計（按欄位名稱分組）
/// </summary>
public class ColumnUsageStatistics
{
    /// <summary>
    /// 欄位名稱
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// 出現次數
    /// </summary>
    public int UsageCount { get; init; }

    /// <summary>
    /// 資料型別是否一致
    /// </summary>
    public bool IsTypeConsistent { get; init; }

    /// <summary>
    /// 長度是否一致
    /// </summary>
    public bool IsLengthConsistent { get; init; }

    /// <summary>
    /// 可空性是否一致
    /// </summary>
    public bool IsNullabilityConsistent { get; init; }

    /// <summary>
    /// 是否完全一致（型別、長度、可空性皆一致）
    /// </summary>
    public bool IsFullyConsistent => IsTypeConsistent && IsLengthConsistent && IsNullabilityConsistent;

    /// <summary>
    /// 主要資料型別（出現最多次的型別）
    /// </summary>
    public required string PrimaryDataType { get; init; }

    /// <summary>
    /// 主要基礎型別
    /// </summary>
    public required string PrimaryBaseType { get; init; }

    /// <summary>
    /// 主要長度
    /// </summary>
    public int PrimaryMaxLength { get; init; }

    /// <summary>
    /// 主要可空性
    /// </summary>
    public bool PrimaryIsNullable { get; init; }

    /// <summary>
    /// 詳細使用清單
    /// </summary>
    public IReadOnlyList<ColumnUsageDetail> Usages { get; init; } = [];
}
