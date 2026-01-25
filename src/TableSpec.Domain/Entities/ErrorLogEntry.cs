namespace TableSpec.Domain.Entities;

/// <summary>
/// SQL Server 錯誤記錄實體
/// </summary>
public class ErrorLogEntry
{
    /// <summary>
    /// 記錄日期
    /// </summary>
    public DateTime LogDate { get; init; }

    /// <summary>
    /// 處理程序資訊
    /// </summary>
    public string? ProcessInfo { get; init; }

    /// <summary>
    /// 記錄文字
    /// </summary>
    public string? Text { get; init; }
}
