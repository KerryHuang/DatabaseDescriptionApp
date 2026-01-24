namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 約束類型
/// </summary>
public enum ConstraintType
{
    /// <summary>
    /// 主鍵
    /// </summary>
    PrimaryKey = 0,

    /// <summary>
    /// 外鍵
    /// </summary>
    ForeignKey = 1,

    /// <summary>
    /// 唯一約束
    /// </summary>
    Unique = 2,

    /// <summary>
    /// 檢查約束
    /// </summary>
    Check = 3,

    /// <summary>
    /// 預設值約束
    /// </summary>
    Default = 4
}

/// <summary>
/// 約束結構
/// </summary>
public class SchemaConstraint
{
    /// <summary>
    /// 約束名稱
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 約束類型
    /// </summary>
    public ConstraintType ConstraintType { get; set; }

    /// <summary>
    /// 約束欄位清單
    /// </summary>
    public IList<string> Columns { get; set; } = new List<string>();

    /// <summary>
    /// 參照表格（外鍵用）
    /// </summary>
    public string? ReferencedTable { get; set; }

    /// <summary>
    /// 參照欄位清單（外鍵用）
    /// </summary>
    public IList<string>? ReferencedColumns { get; set; }

    /// <summary>
    /// ON DELETE 規則（外鍵用）
    /// </summary>
    public string? OnDeleteAction { get; set; }

    /// <summary>
    /// ON UPDATE 規則（外鍵用）
    /// </summary>
    public string? OnUpdateAction { get; set; }

    /// <summary>
    /// 定義內容（Check/Default 約束用）
    /// </summary>
    public string? Definition { get; set; }
}
