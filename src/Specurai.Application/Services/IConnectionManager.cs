using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 連線管理服務介面
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// 取得所有連線設定
    /// </summary>
    IReadOnlyList<ConnectionProfile> GetAllProfiles();

    /// <summary>
    /// 取得所有已啟用的連線設定（供功能面的連線選擇使用）
    /// </summary>
    IReadOnlyList<ConnectionProfile> GetEnabledProfiles();

    /// <summary>
    /// 取得目前使用的連線設定
    /// </summary>
    ConnectionProfile? GetCurrentProfile();

    /// <summary>
    /// 設定目前使用的連線
    /// </summary>
    void SetCurrentProfile(Guid profileId);

    /// <summary>
    /// 新增連線設定
    /// </summary>
    void AddProfile(ConnectionProfile profile);

    /// <summary>
    /// 更新連線設定
    /// </summary>
    void UpdateProfile(ConnectionProfile profile);

    /// <summary>
    /// 刪除連線設定
    /// </summary>
    void DeleteProfile(Guid profileId);

    /// <summary>
    /// 測試連線
    /// </summary>
    Task<bool> TestConnectionAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>
    /// 建立連線字串
    /// </summary>
    string BuildConnectionString(ConnectionProfile profile);

    /// <summary>
    /// 取得目前連線字串
    /// </summary>
    string? GetCurrentConnectionString();

    /// <summary>
    /// 取得指定連線設定的連線字串
    /// </summary>
    /// <param name="profileId">連線設定檔 ID</param>
    /// <returns>連線字串，若找不到則回傳 null</returns>
    string? GetConnectionString(Guid profileId);

    /// <summary>
    /// 取得指定連線設定的名稱
    /// </summary>
    /// <param name="profileId">連線設定檔 ID</param>
    /// <returns>連線設定名稱，若找不到則回傳 ID 字串</returns>
    string GetProfileName(Guid profileId);

    /// <summary>
    /// 註冊臨時連線設定（不持久化，僅存在於記憶體中）
    /// </summary>
    void RegisterTemporaryProfiles(IReadOnlyList<ConnectionProfile> profiles);

    /// <summary>
    /// 取得目前生效的資料庫名稱（覆寫值優先，否則為目前設定檔的預設資料庫）
    /// </summary>
    string? GetCurrentDatabase();

    /// <summary>
    /// 設定目前資料庫覆寫（null 表示重設回設定檔預設資料庫）。
    /// 僅存在於記憶體中不持久化；切換連線設定檔時自動清除。
    /// </summary>
    void SetCurrentDatabase(string? databaseName);

    /// <summary>
    /// 取得目前連線伺服器上的使用者資料庫清單（database_id > 4 且 ONLINE）。
    /// 無目前設定檔時回傳空清單；連線或查詢失敗時擲出例外，由呼叫端決定 degrade 行為。
    /// </summary>
    Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得指定連線設定檔伺服器上的使用者資料庫清單。
    /// 連線或查詢失敗時擲出例外，由呼叫端決定 degrade 行為。
    /// </summary>
    Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>
    /// 目前資料庫變更事件（參數為新的生效資料庫名稱）
    /// </summary>
    event EventHandler<string?>? CurrentDatabaseChanged;

    /// <summary>
    /// 連線變更事件
    /// </summary>
    event EventHandler<ConnectionProfile?>? CurrentProfileChanged;
}
