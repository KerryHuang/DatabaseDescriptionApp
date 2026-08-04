using System.Diagnostics;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Velopack;
using Velopack.Sources;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// Windows/Linux 自動更新實作，底層使用 Velopack + GitHub Release 做為更新來源。
/// 採 lazy init 避免未執行 VelopackApp.Build() 的情境（例如單元測試）因 locator 未設定而直接拋錯。
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly string _githubRepoUrl;
    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;

    public VelopackUpdateService(string githubRepoUrl)
    {
        _githubRepoUrl = githubRepoUrl;
    }

    private UpdateManager? GetManagerOrNull()
    {
        if (_updateManager is not null) return _updateManager;
        try
        {
            var source = new GithubSource(_githubRepoUrl, accessToken: null, prerelease: false);
            _updateManager = new UpdateManager(source);
            return _updateManager;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UpdateService] Velopack 初始化失敗：{ex.Message}");
            return null;
        }
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var manager = GetManagerOrNull();
        if (manager is null || !manager.IsInstalled)
            return null;

        try
        {
            _pendingUpdate = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (_pendingUpdate is null)
                return null;

            var target = _pendingUpdate.TargetFullRelease;
            return new UpdateCheckResult
            {
                NewVersion = target.Version.ToString(),
                ReleaseNotes = target.NotesMarkdown ?? string.Empty,
                ReleaseUrl = $"{_githubRepoUrl}/releases/tag/v{target.Version}",
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
        var manager = GetManagerOrNull()
            ?? throw new InvalidOperationException("Velopack 尚未初始化。");
        if (_pendingUpdate is null)
            throw new InvalidOperationException("未偵測到待套用的更新，請先呼叫 CheckForUpdateAsync。");

        await manager.DownloadUpdatesAsync(_pendingUpdate, p => progress?.Report(p), ct).ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        var manager = GetManagerOrNull()
            ?? throw new InvalidOperationException("Velopack 尚未初始化。");
        if (_pendingUpdate is null)
            throw new InvalidOperationException("未偵測到待套用的更新。");

        manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
