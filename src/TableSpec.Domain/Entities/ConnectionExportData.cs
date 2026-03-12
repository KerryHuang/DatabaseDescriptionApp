namespace TableSpec.Domain.Entities;

/// <summary>
/// 連線設定匯出資料
/// </summary>
public class ConnectionExportData
{
    /// <summary>
    /// 匯出格式版本
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// 匯出時間
    /// </summary>
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 連線設定清單
    /// </summary>
    public required IReadOnlyList<ConnectionProfile> Profiles { get; init; }
}
