namespace TableSpec.Domain.Entities;

/// <summary>
/// 參數資訊（用於 Stored Procedure 和 Function）
/// </summary>
public class ParameterInfo
{
    /// <summary>
    /// 參數名稱
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 資料型別
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// 參數長度
    /// </summary>
    public int? Length { get; init; }

    /// <summary>
    /// 是否為輸出參數
    /// </summary>
    public bool IsOutput { get; init; }

    /// <summary>
    /// 預設值
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 參數順序
    /// </summary>
    public int Ordinal { get; init; }
}
