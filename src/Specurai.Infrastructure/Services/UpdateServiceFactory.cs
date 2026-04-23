using System.Runtime.InteropServices;
using Specurai.Application.Services;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 依作業系統分派 IUpdateService 實作。
/// </summary>
public static class UpdateServiceFactory
{
    public static IUpdateService Create(HttpClient httpClient, string owner, string repo, string currentVersion)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new GitHubReleaseUpdateService(httpClient, owner, repo, currentVersion);

        return new VelopackUpdateService($"https://github.com/{owner}/{repo}");
    }
}
