namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位資訊
/// </summary>
public class ColumnInfo
{
    /// <summary>
    /// 結構描述
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>
    /// 表格名稱
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// 欄位名稱
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// 資料型別
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// 欄位長度
    /// </summary>
    public int? Length { get; init; }

    /// <summary>
    /// 預設值
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 是否為主鍵
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// 是否為唯一索引
    /// </summary>
    public bool IsUniqueKey { get; init; }

    /// <summary>
    /// 是否有索引
    /// </summary>
    public bool IsIndexed { get; init; }

    /// <summary>
    /// 是否允許空值
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// 欄位說明
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 原始欄位說明（用於比對是否有變更）
    /// </summary>
    public string? OriginalDescription { get; set; }
}
