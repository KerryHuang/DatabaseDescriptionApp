using System.Diagnostics;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Velopack;
using Velopack.Sources;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// Windows/Linux 自動更新實作，底層使用 Velopack + GitHub Release 做為更新來源。
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _pendingUpdate;

    public VelopackUpdateService(string githubRepoUrl)
    {
        var source = new GithubSource(githubRepoUrl, accessToken: null, prerelease: false);
        _updateManager = new UpdateManager(source);
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!_updateManager.IsInstalled)
            return null;

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (_pendingUpdate is null)
                return null;

            var target = _pendingUpdate.TargetFullRelease;
            return new UpdateCheckResult
            {
                NewVersion = target.Version.ToString(),
                ReleaseNotes = target.NotesMarkdown ?? string.Empty,
                ReleaseUrl = $"https://github.com/releases/tag/v{target.Version}",
                CanAutoApply = true,
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UpdateService] Velopack 檢查失敗：{ex.Message}");
            return null;
        }
    }

    public async Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_pendingUpdate is null)
            throw new InvalidOperationException("未偵測到待套用的更新，請先呼叫 CheckForUpdateAsync。");

        await _updateManager.DownloadUpdatesAsync(_pendingUpdate, p => progress?.Report(p), ct).ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null)
            throw new InvalidOperationException("未偵測到待套用的更新。");

        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
