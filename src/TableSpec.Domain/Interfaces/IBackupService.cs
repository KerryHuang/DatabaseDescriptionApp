using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 資料庫備份服務介面
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// 備份資料庫
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <param name="connectionName">連線名稱</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="backupType">備份類型</param>
    /// <param name="description">備份描述</param>
    /// <param name="progress">進度回報</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>備份資訊</returns>
    Task<BackupInfo> BackupDatabaseAsync(
        string connectionString,
        Guid connectionId,
        string connectionName,
        string backupPath,
        BackupType backupType = BackupType.Full,
        string? description = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 還原資料庫
    /// </summary>
    /// <param name="connectionString">連線字串（連接到 master 資料庫）</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="options">還原選項</param>
    /// <param name="progress">進度回報</param>
    /// <param name="cancellationToken">取消權杖</param>
    Task RestoreDatabaseAsync(
        string connectionString,
        string backupPath,
        RestoreOptions options,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 驗證備份檔案
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>驗證結果</returns>
    Task<BackupVerifyResult> VerifyBackupAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得備份檔案資訊
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>備份檔案資訊</returns>
    Task<BackupFileInfo> GetBackupFileInfoAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得備份歷史記錄
    /// </summary>
    /// <returns>備份歷史記錄</returns>
    BackupHistory GetBackupHistory();

    /// <summary>
    /// 儲存備份歷史記錄
    /// </summary>
    /// <param name="history">備份歷史記錄</param>
    void SaveBackupHistory(BackupHistory history);

    /// <summary>
    /// 新增備份記錄到歷史
    /// </summary>
    /// <param name="backupInfo">備份資訊</param>
    void AddToHistory(BackupInfo backupInfo);

    /// <summary>
    /// 從歷史中移除備份記錄
    /// </summary>
    /// <param name="backupId">備份 ID</param>
    void RemoveFromHistory(Guid backupId);
}

/// <summary>
/// 備份進度
/// </summary>
public class BackupProgress
{
    /// <summary>完成百分比</summary>
    public int PercentComplete { get; init; }

    /// <summary>進度訊息</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 還原進度
/// </summary>
public class RestoreProgress
{
    /// <summary>完成百分比</summary>
    public int PercentComplete { get; init; }

    /// <summary>進度訊息</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 備份驗證結果
/// </summary>
public class BackupVerifyResult
{
    /// <summary>是否有效</summary>
    public bool IsValid { get; init; }

    /// <summary>錯誤訊息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>備份檔案資訊</summary>
    public BackupFileInfo? FileInfo { get; init; }
}

/// <summary>
/// 備份檔案資訊
/// </summary>
public class BackupFileInfo
{
    /// <summary>資料庫名稱</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>伺服器名稱</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>備份開始時間</summary>
    public DateTime BackupStartTime { get; init; }

    /// <summary>備份完成時間</summary>
    public DateTime BackupFinishTime { get; init; }

    /// <summary>備份類型</summary>
    public BackupType BackupType { get; init; }

    /// <summary>備份大小（bytes）</summary>
    public long BackupSizeBytes { get; init; }

    /// <summary>SQL Server 版本</summary>
    public string SqlServerVersion { get; init; } = string.Empty;

    /// <summary>資料庫版本</summary>
    public int DatabaseVersion { get; init; }

    /// <summary>備份描述</summary>
    public string? Description { get; init; }

    /// <summary>邏輯檔案清單</summary>
    public List<BackupLogicalFile> LogicalFiles { get; init; } = [];

    /// <summary>
    /// 格式化的備份大小
    /// </summary>
    public string FormattedBackupSize
    {
        get
        {
            if (BackupSizeBytes < 1024) return $"{BackupSizeBytes} B";
            if (BackupSizeBytes < 1024 * 1024) return $"{BackupSizeBytes / 1024.0:F2} KB";
            if (BackupSizeBytes < 1024 * 1024 * 1024) return $"{BackupSizeBytes / (1024.0 * 1024):F2} MB";
            return $"{BackupSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}

/// <summary>
/// 備份中的邏輯檔案
/// </summary>
public class BackupLogicalFile
{
    /// <summary>邏輯名稱</summary>
    public string LogicalName { get; init; } = string.Empty;

    /// <summary>實體路徑</summary>
    public string PhysicalName { get; init; } = string.Empty;

    /// <summary>檔案類型（D=Data, L=Log）</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>檔案大小（bytes）</summary>
    public long SizeBytes { get; init; }
}
