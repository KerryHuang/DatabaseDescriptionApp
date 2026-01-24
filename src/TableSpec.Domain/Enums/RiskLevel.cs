namespace TableSpec.Domain.Enums;

/// <summary>
/// 風險等級
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// 低風險 - 可直接執行
    /// </summary>
    Low = 0,

    /// <summary>
    /// 中風險 - 需人工確認後執行
    /// </summary>
    Medium = 1,

    /// <summary>
    /// 高風險 - 只能匯出腳本
    /// </summary>
    High = 2,

    /// <summary>
    /// 禁止 - 只能匯出腳本並顯示警告
    /// </summary>
    Forbidden = 3
}
