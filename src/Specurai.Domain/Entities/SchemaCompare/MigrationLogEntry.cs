using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Migration 執行日誌單筆記錄
/// </summary>
public class MigrationLogEntry
{
    /// <summary>
    /// 物件名稱（如 [dbo].[Users]）
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// 執行動作描述（如 ADD COLUMN、CREATE TABLE）
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 執行狀態
    /// </summary>
    public MigrationLogStatus Status { get; set; }

    /// <summary>
    /// 執行耗時（執行成功時才有值）
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// 錯誤訊息（Failed 時才有值）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 備註（如「高風險未執行」、「使用者取消」）
    /// </summary>
    public string? Note { get; set; }
}
