namespace TableSpec.Domain.Enums;

/// <summary>
/// 差異類型（採用最大化原則，不刪除任何物件）
/// </summary>
public enum DifferenceType
{
    /// <summary>
    /// 新增 - 物件不存在於目標環境
    /// </summary>
    Added = 0,

    /// <summary>
    /// 修改 - 物件屬性不同
    /// </summary>
    Modified = 1
}
