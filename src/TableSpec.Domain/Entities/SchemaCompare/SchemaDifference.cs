using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// Schema 物件類型
/// </summary>
public enum SchemaObjectType
{
    /// <summary>
    /// 表格
    /// </summary>
    Table = 0,

    /// <summary>
    /// 欄位
    /// </summary>
    Column = 1,

    /// <summary>
    /// 索引
    /// </summary>
    Index = 2,

    /// <summary>
    /// 約束
    /// </summary>
    Constraint = 3,

    /// <summary>
    /// 檢視表
    /// </summary>
    View = 4,

    /// <summary>
    /// 預存程序
    /// </summary>
    StoredProcedure = 5,

    /// <summary>
    /// 函數
    /// </summary>
    Function = 6,

    /// <summary>
    /// 觸發程序
    /// </summary>
    Trigger = 7
}

/// <summary>
/// 單一差異項目
/// </summary>
public class SchemaDifference
{
    /// <summary>
    /// 物件類型
    /// </summary>
    public SchemaObjectType ObjectType { get; set; }

    /// <summary>
    /// 物件名稱
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// 差異類型
    /// </summary>
    public DifferenceType DifferenceType { get; set; }

    /// <summary>
    /// 風險等級
    /// </summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>
    /// 屬性名稱（修改時）
    /// </summary>
    public string? PropertyName { get; set; }

    /// <summary>
    /// 來源值
    /// </summary>
    public string? SourceValue { get; set; }

    /// <summary>
    /// 目標值
    /// </summary>
    public string? TargetValue { get; set; }

    /// <summary>
    /// 差異描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 同步動作
    /// </summary>
    public SyncAction SyncAction { get; set; } = SyncAction.Skip;

    /// <summary>
    /// 是否可以直接執行
    /// </summary>
    public bool CanExecuteDirectly => RiskLevel == RiskLevel.Low || RiskLevel == RiskLevel.Medium;
}
