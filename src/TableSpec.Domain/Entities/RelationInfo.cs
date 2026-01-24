namespace TableSpec.Domain.Entities;

/// <summary>
/// 關聯類型
/// </summary>
public enum RelationType
{
    /// <summary>
    /// 本表參考其他表（FK）
    /// </summary>
    Outgoing,

    /// <summary>
    /// 其他表參考本表
    /// </summary>
    Incoming
}

/// <summary>
/// 關聯資訊（Foreign Key）
/// </summary>
public class RelationInfo
{
    /// <summary>
    /// 條件約束名稱
    /// </summary>
    public required string ConstraintName { get; init; }

    /// <summary>
    /// 來源表格
    /// </summary>
    public required string FromTable { get; init; }

    /// <summary>
    /// 來源欄位
    /// </summary>
    public required string FromColumn { get; init; }

    /// <summary>
    /// 目標表格
    /// </summary>
    public required string ToTable { get; init; }

    /// <summary>
    /// 目標欄位
    /// </summary>
    public required string ToColumn { get; init; }

    /// <summary>
    /// 關聯類型
    /// </summary>
    public RelationType Type { get; init; }
}
