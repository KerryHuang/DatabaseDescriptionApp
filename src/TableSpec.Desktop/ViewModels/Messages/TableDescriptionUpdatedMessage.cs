namespace TableSpec.Desktop.ViewModels.Messages;

/// <summary>
/// 當資料表/檢視表說明更新時發送的訊息
/// </summary>
public sealed class TableDescriptionUpdatedMessage
{
    /// <summary>
    /// 物件類型
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Schema 名稱
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>
    /// 物件名稱
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 新的說明
    /// </summary>
    public string? NewDescription { get; init; }
}
