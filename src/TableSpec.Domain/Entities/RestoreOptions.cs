using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Entities;

/// <summary>
/// 還原選項
/// </summary>
public class RestoreOptions
{
    /// <summary>還原模式</summary>
    public RestoreMode Mode { get; init; }

    /// <summary>目標資料庫名稱（新資料庫時使用）</summary>
    public string? TargetDatabaseName { get; init; }

    /// <summary>資料檔案路徑（新資料庫時可指定）</summary>
    public string? DataFilePath { get; init; }

    /// <summary>日誌檔案路徑（新資料庫時可指定）</summary>
    public string? LogFilePath { get; init; }

    /// <summary>是否覆蓋現有資料庫</summary>
    public bool WithReplace { get; init; }

    /// <summary>還原後是否立即可用</summary>
    public bool WithRecovery { get; init; } = true;

    /// <summary>是否顯示進度</summary>
    public bool ShowProgress { get; init; } = true;
}
