namespace TableSpec.Domain.Entities;

/// <summary>
/// 備份歷史記錄集合
/// </summary>
public class BackupHistory
{
    /// <summary>所有備份記錄</summary>
    public List<BackupInfo> Backups { get; set; } = [];

    /// <summary>
    /// 取得指定連線的備份記錄
    /// </summary>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <returns>備份記錄集合（按時間降序排列）</returns>
    public IEnumerable<BackupInfo> GetByConnection(Guid connectionId) =>
        Backups.Where(b => b.ConnectionId == connectionId)
               .OrderByDescending(b => b.BackupTime);

    /// <summary>
    /// 取得指定連線的最新備份
    /// </summary>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <returns>最新備份資訊，若無則回傳 null</returns>
    public BackupInfo? GetLatestBackup(Guid connectionId) =>
        GetByConnection(connectionId).FirstOrDefault();

    /// <summary>
    /// 檢查是否有指定時間內的備份
    /// </summary>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <param name="maxAge">最大時間間隔</param>
    /// <returns>是否有最近備份</returns>
    public bool HasRecentBackup(Guid connectionId, TimeSpan maxAge) =>
        GetByConnection(connectionId)
            .Any(b => DateTime.Now - b.BackupTime <= maxAge);

    /// <summary>
    /// 新增備份記錄
    /// </summary>
    /// <param name="backupInfo">備份資訊</param>
    public void Add(BackupInfo backupInfo)
    {
        Backups.Add(backupInfo);
    }

    /// <summary>
    /// 移除備份記錄
    /// </summary>
    /// <param name="backupId">備份 ID</param>
    /// <returns>是否成功移除</returns>
    public bool Remove(Guid backupId)
    {
        var backup = Backups.FirstOrDefault(b => b.Id == backupId);
        if (backup != null)
        {
            Backups.Remove(backup);
            return true;
        }
        return false;
    }
}
