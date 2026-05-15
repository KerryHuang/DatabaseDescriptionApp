using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// 資料庫資訊查詢介面（用於維護計劃前置檢查）
/// </summary>
public interface IDatabaseInfoRepository
{
    /// <summary>
    /// 取得伺服器上所有資料庫名稱
    /// </summary>
    Task<IReadOnlyList<string>> GetDatabaseNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得指定資料庫的復原模式
    /// </summary>
    /// <param name="databaseName">資料庫名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<string> GetRecoveryModelAsync(string databaseName, CancellationToken ct = default);

    /// <summary>
    /// 取得指定資料庫的邏輯檔案名稱與實體路徑
    /// </summary>
    /// <param name="databaseName">資料庫名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<(string LogicalName, string PhysicalName)>> GetLogicalFileNamesAsync(string databaseName, CancellationToken ct = default);

    /// <summary>
    /// 檢查指定登入帳號是否存在
    /// </summary>
    /// <param name="loginName">登入帳號名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<bool> LoginExistsAsync(string loginName, CancellationToken ct = default);

    /// <summary>
    /// 檢查指定資料庫使用者是否存在
    /// </summary>
    /// <param name="databaseName">資料庫名稱</param>
    /// <param name="userName">使用者名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<bool> DatabaseUserExistsAsync(string databaseName, string userName, CancellationToken ct = default);

    /// <summary>
    /// 檢查指定使用者是否為資料庫的 db_owner 成員
    /// </summary>
    /// <param name="databaseName">資料庫名稱</param>
    /// <param name="userName">使用者名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<bool> IsDbOwnerMemberAsync(string databaseName, string userName, CancellationToken ct = default);

    /// <summary>
    /// 檢查指定名稱的 Agent Job 是否已存在
    /// </summary>
    /// <param name="jobName">Job 名稱</param>
    /// <param name="ct">取消權杖</param>
    Task<bool> AgentJobExistsAsync(string jobName, CancellationToken ct = default);

    /// <summary>
    /// 取得指定資料庫的相容性層級
    /// </summary>
    Task<int> GetCompatibilityLevelAsync(string databaseName, CancellationToken ct = default);

    /// <summary>
    /// 取得當前 SQL Server 版本對應的相容性層級
    /// </summary>
    Task<int> GetServerCompatibilityLevelAsync(CancellationToken ct = default);

    /// <summary>
    /// 檢查是否為 Azure SQL Database 環境
    /// </summary>
    Task<bool> IsAzureSqlDatabaseAsync(CancellationToken ct = default);

    /// <summary>
    /// 在交易中執行 SQL 指令
    /// </summary>
    /// <param name="sql">SQL 指令</param>
    /// <param name="ct">取消權杖</param>
    Task ExecuteSqlWithTransactionAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 執行 SQL 指令
    /// </summary>
    /// <param name="sql">SQL 指令</param>
    /// <param name="ct">取消權杖</param>
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 取得指定資料庫的所有檔案資訊（含所在磁碟可用空間）
    /// </summary>
    Task<IReadOnlyList<DatabaseFileInfo>> GetDatabaseFilesAsync(string databaseName, CancellationToken ct = default);
}
