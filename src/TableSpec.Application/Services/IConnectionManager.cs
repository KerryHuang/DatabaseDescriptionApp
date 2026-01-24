using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

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
    /// 連線變更事件
    /// </summary>
    event EventHandler<ConnectionProfile?>? CurrentProfileChanged;
}
