using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Entities;

/// <summary>
/// 備份資訊
/// </summary>
public class BackupInfo
{
    /// <summary>備份 ID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>連線設定檔 ID</summary>
    public Guid ConnectionId { get; init; }

    /// <summary>連線名稱</summary>
    public string ConnectionName { get; init; } = string.Empty;

    /// <summary>資料庫名稱</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>伺服器名稱</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>備份檔案路徑</summary>
    public string BackupFilePath { get; init; } = string.Empty;

    /// <summary>備份時間</summary>
    public DateTime BackupTime { get; init; }

    /// <summary>備份類型</summary>
    public BackupType BackupType { get; init; }

    /// <summary>備份檔案大小（bytes）</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>備份是否已驗證</summary>
    public bool IsVerified { get; init; }

    /// <summary>備份描述/備註</summary>
    public string? Description { get; init; }

    /// <summary>SQL Server 版本</summary>
    public string SqlServerVersion { get; init; } = string.Empty;

    /// <summary>
    /// 格式化的檔案大小
    /// </summary>
    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F2} KB";
            if (FileSizeBytes < 1024 * 1024 * 1024) return $"{FileSizeBytes / (1024.0 * 1024):F2} MB";
            return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
