using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 連線設定檔解析輔助工具
/// </summary>
internal static class ProfileResolver
{
    /// <summary>
    /// 依名稱或 ID 解析單一連線設定檔
    /// </summary>
    public static ConnectionProfile? Resolve(IConnectionManager cm, string nameOrId)
    {
        var profiles = cm.GetAllProfiles();
        return profiles.FirstOrDefault(p =>
            p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            p.Id.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析逗號分隔的名稱/ID 清單為 Guid 清單，空字串回傳所有 Profile ID
    /// </summary>
    public static List<Guid> ResolveMultiple(IConnectionManager cm, string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return cm.GetAllProfiles().Select(p => p.Id).ToList();

        var profiles = cm.GetAllProfiles();
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
}
