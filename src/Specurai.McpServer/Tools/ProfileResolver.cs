using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線設定檔解析輔助工具（只解析已啟用的連線）
/// </summary>
internal static class ProfileResolver
{
    /// <summary>
    /// 依名稱或 ID 解析單一已啟用的連線設定檔；多個啟用連線同名（歧義）時回傳 null，
    /// 由 <see cref="DescribeMissing"/> 說明原因。
    /// </summary>
    public static ConnectionProfile? Resolve(IConnectionManager cm, string nameOrId)
    {
        var matches = FindEnabledMatches(cm, nameOrId);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static List<ConnectionProfile> FindEnabledMatches(IConnectionManager cm, string nameOrId) =>
        cm.GetEnabledProfiles()
            .Where(p =>
                p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList();

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
    /// 產生「無法解析連線」的錯誤訊息：名稱歧義、已停用、或找不到。
    /// </summary>
    public static string DescribeMissing(IConnectionManager cm, string nameOrId)
    {
        var matches = FindEnabledMatches(cm, nameOrId);
        if (matches.Count > 1)
            return $"連線名稱「{nameOrId}」有歧義：{matches.Count} 個啟用連線同名，請改用 ID 指定：" +
                   string.Join("、", matches.Select(p => $"{p.Id}（{p.Server}/{p.Database}）"));

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
