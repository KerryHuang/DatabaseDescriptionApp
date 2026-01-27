namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位使用明細（每個表格/檢視中的使用情況）
/// </summary>
public class ColumnUsageDetail
{
    /// <summary>
    /// 欄位名稱
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// Schema 名稱
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// 物件名稱（表格或檢視）
    /// </summary>
    public required string ObjectName { get; init; }

    /// <summary>
    /// 物件類型（TABLE 或 VIEW）
    /// </summary>
    public required string ObjectType { get; init; }

    /// <summary>
    /// 完整資料型別（如 nvarchar(50)）
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// 基礎資料型別（如 nvarchar）
    /// </summary>
    public required string BaseType { get; init; }

    /// <summary>
    /// 最大長度（-1 表示 MAX）
    /// </summary>
    public int MaxLength { get; init; }

    /// <summary>
    /// 精確度
    /// </summary>
    public int Precision { get; init; }

    /// <summary>
    /// 小數位數
    /// </summary>
    public int Scale { get; init; }

    /// <summary>
    /// 是否可為空
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// 與主要型態是否有差異
    /// </summary>
    public bool HasDifference { get; set; }

    /// <summary>
    /// 完整物件名稱（Schema.ObjectName）
    /// </summary>
    public string FullObjectName => $"[{SchemaName}].[{ObjectName}]";
}
