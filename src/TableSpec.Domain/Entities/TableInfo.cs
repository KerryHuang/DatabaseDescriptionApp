namespace TableSpec.Domain.Entities;

/// <summary>
/// 資料庫物件資訊（Table、View、Stored Procedure、Function）
/// </summary>
public class TableInfo
{
    /// <summary>
    /// 物件類型（TABLE、VIEW、PROCEDURE、FUNCTION）
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 結構描述（如 dbo）
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>
    /// 物件名稱
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 物件說明
    /// </summary>
    public string? Description { get; init; }
}
