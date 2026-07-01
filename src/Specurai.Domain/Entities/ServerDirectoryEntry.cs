namespace Specurai.Domain.Entities;

/// <summary>
/// 伺服器端目錄項目（資料夾或備份檔）
/// </summary>
public sealed class ServerDirectoryEntry
{
    /// <summary>名稱（資料夾名或檔名）</summary>
    public required string Name { get; init; }

    /// <summary>完整伺服器端路徑</summary>
    public required string FullPath { get; init; }

    /// <summary>是否為資料夾</summary>
    public required bool IsDirectory { get; init; }
}
