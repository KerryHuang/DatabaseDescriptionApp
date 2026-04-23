using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// macOS 專用的更新檢查實作：僅偵測新版本並引導使用者手動下載。
/// 不呼叫 Velopack，以避開 Apple 公證障礙。
/// </summary>
public sealed class GitHubReleaseUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _currentVersion;

    public GitHubReleaseUpdateService(HttpClient httpClient, string owner, string repo, string currentVersion)
    {
        _httpClient = httpClient;
        _owner = owner;
        _repo = repo;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd($"Specurai-Updater/{_currentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Trace.WriteLine($"[UpdateService] GitHub API 回應 {(int)response.StatusCode}");
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<GitHubReleaseDto>(cancellationToken: ct).ConfigureAwait(false);
            if (dto is null || dto.Draft || dto.Prerelease || string.IsNullOrWhiteSpace(dto.TagName))
                return null;

            var newVersion = dto.TagName.TrimStart('v', 'V');
            if (!IsNewer(newVersion, _currentVersion))
                return null;

            return new UpdateCheckResult
            {
                NewVersion = newVersion,
                ReleaseNotes = dto.Body ?? string.Empty,
                ReleaseUrl = dto.HtmlUrl ?? string.Empty,
                CanAutoApply = false,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Trace.WriteLine($"[UpdateService] 檢查更新失敗：{ex.Message}");
            return null;
        }
    }

    public Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        => throw new InvalidOperationException("macOS 平台不支援自動下載，請引導使用者至 GitHub Release 頁面。");

    public void ApplyAndRestart()
        => throw new InvalidOperationException("macOS 平台不支援自動套用。");

    private static bool IsNewer(string candidate, string current)
    {
        if (!Version.TryParse(candidate, out var a) || !Version.TryParse(current, out var b))
            return false;
        return a > b;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
    }
}
