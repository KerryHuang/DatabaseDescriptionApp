using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線設定檔解析輔助工具（只解析已啟用的連線）
/// </summary>
internal static class ProfileResolver
{
    /// <summary>
    /// 依名稱或 ID 解析單一已啟用的連線設定檔
    /// </summary>
    public static ConnectionProfile? Resolve(IConnectionManager cm, string nameOrId)
    {
        var profiles = cm.GetEnabledProfiles();
        return profiles.FirstOrDefault(p =>
            p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 依名稱或 ID 解析連線設定檔（含已停用的，供管理型工具使用）
    /// </summary>
    public static ConnectionProfile? ResolveAny(IConnectionManager cm, string nameOrId)
    {
        var profiles = cm.GetAllProfiles();
        return profiles.FirstOrDefault(p =>
            p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析逗號分隔的名稱/ID 清單為 Guid 清單，空字串回傳所有已啟用的 Profile ID
    /// </summary>
    public static List<Guid> ResolveMultiple(IConnectionManager cm, string commaSeparated)
    {
        var profiles = cm.GetEnabledProfiles();

        if (string.IsNullOrWhiteSpace(commaSeparated))
            return profiles.Select(p => p.Id).ToList();

        var result = new List<Guid>();

        foreach (var item in commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var profile = profiles.FirstOrDefault(p =>
                p.Name.Equals(item, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Equals(item, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
                result.Add(profile.Id);
        }

        return result;
    }

    /// <summary>
    /// 產生「找不到連線」的錯誤訊息；若該連線存在但已停用，回傳更明確的說明。
    /// </summary>
    public static string DescribeMissing(IConnectionManager cm, string nameOrId)
    {
        var disabled = cm.GetAllProfiles()
            .FirstOrDefault(p =>
                !p.IsEnabled &&
                (p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
                 p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase)));

        return disabled != null
            ? $"連線「{disabled.Name}」已停用，請先在連線設定中啟用。"
            : $"找不到名稱或 ID 為「{nameOrId}」的連線設定。";
    }
}
