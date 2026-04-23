# Specurai 自動更新功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 Specurai.Desktop 啟動時自動偵測 GitHub Release 的新版本，Windows/Linux 可一鍵下載並重啟套用（Velopack），macOS 則以對話框引導使用者手動下載並執行 xattr 解鎖。

**Architecture:** 依 Clean Architecture 四層（Domain → Application → Infrastructure → Desktop）新增 `UpdateCheckResult` Entity、`IUpdateService` 抽象以及依 OS 分派的兩個實作（`VelopackUpdateService`、`GitHubReleaseUpdateService`）；Desktop 層新增 `UpdateNotificationViewModel` 控制主視窗徽章，`UpdateDialogViewModel` 處理 Release Notes / 下載進度 / 重啟，`MacOsUpdateInstructionsDialog` 提供 Mac 專用的手動安裝指引。

**Tech Stack:** Avalonia 11, CommunityToolkit.Mvvm 8, Velopack 0.0.1298, xUnit + NSubstitute + FluentAssertions, .NET 8

**Spec:** [docs/superpowers/specs/2026-04-23-auto-update-design.md](../specs/2026-04-23-auto-update-design.md)

---

## 檔案結構

### 新增

- `src/Specurai.Domain/Entities/UpdateCheckResult.cs`
- `src/Specurai.Application/Services/IUpdateService.cs`
- `src/Specurai.Infrastructure/Services/VelopackUpdateService.cs`
- `src/Specurai.Infrastructure/Services/GitHubReleaseUpdateService.cs`
- `src/Specurai.Infrastructure/Services/UpdateServiceFactory.cs`
- `src/Specurai.Desktop/ViewModels/UpdateNotificationViewModel.cs`
- `src/Specurai.Desktop/ViewModels/UpdateDialogViewModel.cs`
- `src/Specurai.Desktop/Views/UpdateDialog.axaml` + `.cs`
- `src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml` + `.cs`
- `tests/Specurai.Infrastructure.Tests/Services/GitHubReleaseUpdateServiceTests.cs`
- `tests/Specurai.Infrastructure.Tests/Services/UpdateServiceFactoryTests.cs`
- `tests/Specurai.Desktop.Tests/ViewModels/UpdateNotificationViewModelTests.cs`
- `tests/Specurai.Desktop.Tests/ViewModels/UpdateDialogViewModelTests.cs`

### 修改

- `src/Specurai.Desktop/Program.cs` — 新增 DI 註冊
- `src/Specurai.Desktop/Views/MainWindow.axaml` — 右上角徽章按鈕、「說明」選單「檢查更新」
- `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` — 暴露 `UpdateNotification` 屬性、新增「檢查更新」命令
- `.github/workflows/release.yml` — `dotnet publish` 加 `-p:Version=${{ steps.get-version.outputs.version }}`

---

## Task 1：新增 Domain Entity `UpdateCheckResult`

**Files:**
- Create: `src/Specurai.Domain/Entities/UpdateCheckResult.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/UpdateCheckResultTests.cs`

- [ ] **Step 1.1：撰寫失敗測試**

Create `tests/Specurai.Domain.Tests/Entities/UpdateCheckResultTests.cs`:

```csharp
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class UpdateCheckResultTests
{
    [Fact]
    public void 可透過Init建立實例並保留所有欄位()
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            NewVersion = "1.7.0",
            ReleaseNotes = "修正若干問題",
            ReleaseUrl = "https://github.com/example/repo/releases/tag/v1.7.0",
            CanAutoApply = true,
        };

        // Assert
        result.NewVersion.Should().Be("1.7.0");
        result.ReleaseNotes.Should().Be("修正若干問題");
        result.ReleaseUrl.Should().Be("https://github.com/example/repo/releases/tag/v1.7.0");
        result.CanAutoApply.Should().BeTrue();
    }
}
```

- [ ] **Step 1.2：驗證測試失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~UpdateCheckResultTests"`
Expected: FAIL — `UpdateCheckResult` 型別不存在

- [ ] **Step 1.3：建立 Entity**

Create `src/Specurai.Domain/Entities/UpdateCheckResult.cs`:

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 應用程式更新檢查結果。
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>新版本號（不含 "v" 前綴），例如 "1.7.0"。</summary>
    public required string NewVersion { get; init; }

    /// <summary>Release Notes 原始文字（可為 Markdown）。</summary>
    public required string ReleaseNotes { get; init; }

    /// <summary>GitHub Release 頁面 URL（macOS 手動下載時使用）。</summary>
    public required string ReleaseUrl { get; init; }

    /// <summary>是否可由應用程式自動套用（Win/Linux = true，macOS = false）。</summary>
    public required bool CanAutoApply { get; init; }
}
```

- [ ] **Step 1.4：驗證測試通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~UpdateCheckResultTests"`
Expected: PASS

- [ ] **Step 1.5：Commit**

```bash
git add src/Specurai.Domain/Entities/UpdateCheckResult.cs tests/Specurai.Domain.Tests/Entities/UpdateCheckResultTests.cs
git commit -m "feat(domain): 新增 UpdateCheckResult Entity

自動更新功能的資料傳輸物件，涵蓋版本號、Release Notes、
下載連結與是否可自動套用。"
```

---

## Task 2：新增 Application 介面 `IUpdateService`

**Files:**
- Create: `src/Specurai.Application/Services/IUpdateService.cs`

- [ ] **Step 2.1：建立介面**

Create `src/Specurai.Application/Services/IUpdateService.cs`:

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 應用程式自動更新服務抽象。
/// 實作依作業系統分派：Win/Linux 使用 Velopack、macOS 使用 GitHub API + 瀏覽器。
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// 檢查是否有比目前執行版本更新的穩定版本。
    /// 檢查失敗（離線、API 限流等）應回傳 null 並寫入 trace log，不得拋出例外。
    /// </summary>
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// 下載已偵測到的更新封裝（僅 CanAutoApply = true 時有效）。
    /// </summary>
    Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 套用已下載的更新並重啟應用程式（僅 CanAutoApply = true 時有效）。
    /// </summary>
    void ApplyAndRestart();
}
```

- [ ] **Step 2.2：驗證編譯通過**

Run: `dotnet build src/Specurai.Application/Specurai.Application.csproj`
Expected: Build succeeded

- [ ] **Step 2.3：Commit**

```bash
git add src/Specurai.Application/Services/IUpdateService.cs
git commit -m "feat(application): 新增 IUpdateService 抽象介面

定義自動更新三個核心操作：檢查、下載、套用重啟。
實作由 Infrastructure 層依作業系統提供。"
```

---

## Task 3：實作 `GitHubReleaseUpdateService`（macOS 用）

此服務以 GitHub REST API 查詢最新 Release、比對版本、開啟瀏覽器。其邏輯純粹、可單元測試，先做可讓後續跨平台邏輯有參照。

**Files:**
- Create: `src/Specurai.Infrastructure/Services/GitHubReleaseUpdateService.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/GitHubReleaseUpdateServiceTests.cs`

- [ ] **Step 3.1：撰寫「有新版」測試**

Create `tests/Specurai.Infrastructure.Tests/Services/GitHubReleaseUpdateServiceTests.cs`:

```csharp
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class GitHubReleaseUpdateServiceTests
{
    private const string CurrentVersion = "1.6.0";

    private static HttpClient CreateClient(HttpStatusCode status, string? body) =>
        new(new StubHandler(status, body));

    private static GitHubReleaseUpdateService CreateService(HttpClient client, string currentVersion = CurrentVersion)
        => new(client, "kerryhuang317", "DatabaseDescriptionApp", currentVersion);

    [Fact]
    public async Task CheckForUpdateAsync_有新版_回傳UpdateCheckResult()
    {
        // Arrange
        var json = """
        {
          "tag_name": "v1.7.0",
          "name": "v1.7.0",
          "body": "修正若干問題",
          "html_url": "https://github.com/owner/repo/releases/tag/v1.7.0",
          "prerelease": false,
          "draft": false
        }
        """;
        var client = CreateClient(HttpStatusCode.OK, json);
        var service = CreateService(client);

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert
        result.Should().NotBeNull();
        result!.NewVersion.Should().Be("1.7.0");
        result.ReleaseNotes.Should().Be("修正若干問題");
        result.ReleaseUrl.Should().Be("https://github.com/owner/repo/releases/tag/v1.7.0");
        result.CanAutoApply.Should().BeFalse();
    }

    private sealed class StubHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
            });
    }
}
```

- [ ] **Step 3.2：驗證測試失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GitHubReleaseUpdateService"`
Expected: FAIL — `GitHubReleaseUpdateService` 型別不存在

- [ ] **Step 3.3：建立最小實作**

Create `src/Specurai.Infrastructure/Services/GitHubReleaseUpdateService.cs`:

```csharp
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
```

- [ ] **Step 3.4：驗證測試通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GitHubReleaseUpdateService"`
Expected: PASS（1 passed）

- [ ] **Step 3.5：補齊邊界測試**

Append to `GitHubReleaseUpdateServiceTests.cs`:

```csharp
    [Fact]
    public async Task CheckForUpdateAsync_相同版本_回傳Null()
    {
        var json = """{"tag_name":"v1.6.0","body":"","html_url":"","prerelease":false,"draft":false}""";
        var service = CreateService(CreateClient(HttpStatusCode.OK, json));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_PreRelease_回傳Null()
    {
        var json = """{"tag_name":"v1.7.0","body":"","html_url":"","prerelease":true,"draft":false}""";
        var service = CreateService(CreateClient(HttpStatusCode.OK, json));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_Draft_回傳Null()
    {
        var json = """{"tag_name":"v1.7.0","body":"","html_url":"","prerelease":false,"draft":true}""";
        var service = CreateService(CreateClient(HttpStatusCode.OK, json));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_API404_回傳Null()
    {
        var service = CreateService(CreateClient(HttpStatusCode.NotFound, null));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_API429_回傳Null()
    {
        var service = CreateService(CreateClient(HttpStatusCode.TooManyRequests, null));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_版本格式異常_回傳Null()
    {
        var json = """{"tag_name":"not-a-version","body":"","html_url":"","prerelease":false,"draft":false}""";
        var service = CreateService(CreateClient(HttpStatusCode.OK, json));
        var result = await service.CheckForUpdateAsync();
        result.Should().BeNull();
    }
```

- [ ] **Step 3.6：驗證所有測試通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GitHubReleaseUpdateService"`
Expected: PASS（7 passed）

- [ ] **Step 3.7：Commit**

```bash
git add src/Specurai.Infrastructure/Services/GitHubReleaseUpdateService.cs \
        tests/Specurai.Infrastructure.Tests/Services/GitHubReleaseUpdateServiceTests.cs
git commit -m "feat(infrastructure): 新增 GitHubReleaseUpdateService

macOS 專用更新檢查實作，透過 GitHub REST API 偵測最新版本。
略過 prerelease 與 draft，網路錯誤與 API 限流靜默失敗。"
```

---

## Task 4：實作 `VelopackUpdateService`（Windows/Linux 用）

**Files:**
- Create: `src/Specurai.Infrastructure/Services/VelopackUpdateService.cs`

此服務包裝 Velopack `UpdateManager`，無法以 `HttpMessageHandler` mock（Velopack 內部型別大多為 `sealed`），採用手動煙霧測試驗證。

- [ ] **Step 4.1：建立實作**

Create `src/Specurai.Infrastructure/Services/VelopackUpdateService.cs`:

```csharp
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

        await _updateManager.DownloadUpdatesAsync(_pendingUpdate, p => progress?.Report(p)).ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null)
            throw new InvalidOperationException("未偵測到待套用的更新。");

        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
```

- [ ] **Step 4.2：驗證編譯**

Run: `dotnet build src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 4.3：Commit**

```bash
git add src/Specurai.Infrastructure/Services/VelopackUpdateService.cs
git commit -m "feat(infrastructure): 新增 VelopackUpdateService

Win/Linux 自動更新實作，以 Velopack UpdateManager 搭配
GithubSource 為更新來源，僅接受 prerelease=false 的版本。"
```

---

## Task 5：新增 `UpdateServiceFactory`

**Files:**
- Create: `src/Specurai.Infrastructure/Services/UpdateServiceFactory.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/UpdateServiceFactoryTests.cs`

- [ ] **Step 5.1：撰寫失敗測試**

Create `tests/Specurai.Infrastructure.Tests/Services/UpdateServiceFactoryTests.cs`:

```csharp
using System.Net.Http;
using System.Runtime.InteropServices;
using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class UpdateServiceFactoryTests
{
    [Fact]
    public void Create_Windows或Linux_回傳VelopackUpdateService()
    {
        // Skip 條件：僅在 Windows/Linux 執行時有意義
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var service = UpdateServiceFactory.Create(new HttpClient(), "kerryhuang317", "DatabaseDescriptionApp", "1.6.0");

        service.Should().BeOfType<VelopackUpdateService>();
    }

    [Fact]
    public void Create_macOS_回傳GitHubReleaseUpdateService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var service = UpdateServiceFactory.Create(new HttpClient(), "kerryhuang317", "DatabaseDescriptionApp", "1.6.0");

        service.Should().BeOfType<GitHubReleaseUpdateService>();
    }
}
```

- [ ] **Step 5.2：驗證測試失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~UpdateServiceFactory"`
Expected: FAIL — 型別不存在

- [ ] **Step 5.3：建立 Factory**

Create `src/Specurai.Infrastructure/Services/UpdateServiceFactory.cs`:

```csharp
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
```

- [ ] **Step 5.4：驗證測試通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~UpdateServiceFactory"`
Expected: PASS（依執行平台，其中 1 個 test 會實質執行、另 1 個直接 return）

- [ ] **Step 5.5：Commit**

```bash
git add src/Specurai.Infrastructure/Services/UpdateServiceFactory.cs \
        tests/Specurai.Infrastructure.Tests/Services/UpdateServiceFactoryTests.cs
git commit -m "feat(infrastructure): 新增 UpdateServiceFactory

依 RuntimeInformation 判斷作業系統，macOS 使用 GitHubReleaseUpdateService，
其他平台使用 VelopackUpdateService。"
```

---

## Task 6：`UpdateNotificationViewModel`（主視窗徽章）

**Files:**
- Create: `src/Specurai.Desktop/ViewModels/UpdateNotificationViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/UpdateNotificationViewModelTests.cs`

- [ ] **Step 6.1：撰寫失敗測試**

Create `tests/Specurai.Desktop.Tests/ViewModels/UpdateNotificationViewModelTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

public class UpdateNotificationViewModelTests
{
    private readonly IUpdateService _updateService = Substitute.For<IUpdateService>();

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new UpdateNotificationViewModel();

        vm.Should().NotBeNull();
        vm.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_有新版本_HasUpdate應為True且NewVersion正確()
    {
        // Arrange
        _updateService.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult
            {
                NewVersion = "1.7.0",
                ReleaseNotes = "修正",
                ReleaseUrl = "https://...",
                CanAutoApply = true,
            });
        var vm = new UpdateNotificationViewModel(_updateService);

        // Act
        await vm.CheckAsync();

        // Assert
        vm.HasUpdate.Should().BeTrue();
        vm.NewVersion.Should().Be("1.7.0");
        vm.LatestResult.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_無新版本_HasUpdate保持False()
    {
        _updateService.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns((UpdateCheckResult?)null);
        var vm = new UpdateNotificationViewModel(_updateService);

        await vm.CheckAsync();

        vm.HasUpdate.Should().BeFalse();
        vm.LatestResult.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_併發呼叫_僅觸發單次底層檢查()
    {
        _updateService.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(50);
            return (UpdateCheckResult?)null;
        });
        var vm = new UpdateNotificationViewModel(_updateService);

        await Task.WhenAll(vm.CheckAsync(), vm.CheckAsync(), vm.CheckAsync());

        await _updateService.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 6.2：驗證測試失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~UpdateNotificationViewModel"`
Expected: FAIL — 型別不存在

- [ ] **Step 6.3：建立 ViewModel**

Create `src/Specurai.Desktop/ViewModels/UpdateNotificationViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 主視窗的更新通知 ViewModel，控制右上角「⬆ 有新版本」徽章顯示。
/// </summary>
public partial class UpdateNotificationViewModel : ViewModelBase
{
    private readonly IUpdateService? _updateService;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    private UpdateCheckResult? _latestResult;

    /// <summary>設計時建構函式。</summary>
    public UpdateNotificationViewModel()
    {
    }

    public UpdateNotificationViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    /// <summary>
    /// 以非阻擋方式檢查更新，併發呼叫會被去重為單次。
    /// </summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_updateService is null) return;
        if (!await _checkGate.WaitAsync(0, ct)) return;

        try
        {
            var result = await _updateService.CheckForUpdateAsync(ct);
            LatestResult = result;
            HasUpdate = result is not null;
            NewVersion = result?.NewVersion ?? string.Empty;
        }
        finally
        {
            _checkGate.Release();
        }
    }
}
```

- [ ] **Step 6.4：驗證測試通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~UpdateNotificationViewModel"`
Expected: PASS（4 passed）

- [ ] **Step 6.5：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/UpdateNotificationViewModel.cs \
        tests/Specurai.Desktop.Tests/ViewModels/UpdateNotificationViewModelTests.cs
git commit -m "feat(desktop): 新增 UpdateNotificationViewModel

控制主視窗右上角「有新版本」徽章顯示，支援非阻擋檢查
與併發呼叫去重。"
```

---

## Task 7：`UpdateDialogViewModel` + `UpdateDialog.axaml`（Win/Linux 對話框）

**Files:**
- Create: `src/Specurai.Desktop/ViewModels/UpdateDialogViewModel.cs`
- Create: `src/Specurai.Desktop/Views/UpdateDialog.axaml`
- Create: `src/Specurai.Desktop/Views/UpdateDialog.axaml.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/UpdateDialogViewModelTests.cs`

- [ ] **Step 7.1：撰寫失敗測試**

Create `tests/Specurai.Desktop.Tests/ViewModels/UpdateDialogViewModelTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

public class UpdateDialogViewModelTests
{
    private readonly IUpdateService _updateService = Substitute.For<IUpdateService>();

    private static UpdateCheckResult MakeResult() => new()
    {
        NewVersion = "1.7.0",
        ReleaseNotes = "修正若干問題",
        ReleaseUrl = "https://github.com/o/r/releases/tag/v1.7.0",
        CanAutoApply = true,
    };

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new UpdateDialogViewModel();
        vm.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_帶入Result_應正確顯示版本與ReleaseNotes()
    {
        var vm = new UpdateDialogViewModel(_updateService, MakeResult());

        vm.NewVersion.Should().Be("1.7.0");
        vm.ReleaseNotes.Should().Be("修正若干問題");
        vm.Progress.Should().Be(0);
        vm.CanConfirm.Should().BeTrue();
        vm.CanRestart.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmCommand_下載成功_CanRestart應為True()
    {
        _updateService.DownloadAsync(Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var vm = new UpdateDialogViewModel(_updateService, MakeResult());

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.CanConfirm.Should().BeFalse();
        vm.CanRestart.Should().BeTrue();
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmCommand_下載失敗_顯示錯誤並保留確認按鈕()
    {
        _updateService.DownloadAsync(Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("boom")));
        var vm = new UpdateDialogViewModel(_updateService, MakeResult());

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.CanRestart.Should().BeFalse();
        vm.CanConfirm.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("boom");
    }
}
```

- [ ] **Step 7.2：驗證測試失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~UpdateDialogViewModel"`
Expected: FAIL — 型別不存在

- [ ] **Step 7.3：建立 ViewModel**

Create `src/Specurai.Desktop/ViewModels/UpdateDialogViewModel.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 更新對話框 ViewModel：顯示版本資訊、Release Notes、下載進度，並提供重啟按鈕。
/// </summary>
public partial class UpdateDialogViewModel : ViewModelBase
{
    private readonly IUpdateService? _updateService;

    [ObservableProperty]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _canConfirm = true;

    [ObservableProperty]
    private bool _canRestart;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>設計時建構函式。</summary>
    public UpdateDialogViewModel()
    {
    }

    public UpdateDialogViewModel(IUpdateService updateService, UpdateCheckResult result)
    {
        _updateService = updateService;
        NewVersion = result.NewVersion;
        ReleaseNotes = result.ReleaseNotes;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_updateService is null) return;

        CanConfirm = false;
        ErrorMessage = string.Empty;
        var progress = new Progress<int>(p => Progress = p);

        try
        {
            await _updateService.DownloadAsync(progress);
            CanRestart = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CanConfirm = true;
        }
    }

    [RelayCommand]
    private void Restart()
    {
        _updateService?.ApplyAndRestart();
    }
}
```

- [ ] **Step 7.4：驗證測試通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~UpdateDialogViewModel"`
Expected: PASS（4 passed）

- [ ] **Step 7.5：建立 View**

Create `src/Specurai.Desktop/Views/UpdateDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Specurai.Desktop.ViewModels"
        x:Class="Specurai.Desktop.Views.UpdateDialog"
        x:DataType="vm:UpdateDialogViewModel"
        Title="有新版本"
        Width="560"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False">
    <Design.DataContext>
        <vm:UpdateDialogViewModel/>
    </Design.DataContext>
    <StackPanel Margin="24" Spacing="12">
        <TextBlock FontSize="18" FontWeight="Bold">
            <Run Text="新版本 v"/><Run Text="{Binding NewVersion}"/>
        </TextBlock>

        <Border BorderBrush="Gray" BorderThickness="1" CornerRadius="4" Padding="12" MaxHeight="260">
            <ScrollViewer>
                <TextBlock Text="{Binding ReleaseNotes}" TextWrapping="Wrap" FontFamily="Consolas"/>
            </ScrollViewer>
        </Border>

        <ProgressBar Minimum="0" Maximum="100" Value="{Binding Progress}" Height="6"/>

        <TextBlock Foreground="Red" Text="{Binding ErrorMessage}" TextWrapping="Wrap"
                   IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10">
            <Button Content="稍後" Click="OnCloseClick"/>
            <Button Content="確認更新" Command="{Binding ConfirmCommand}" IsEnabled="{Binding CanConfirm}"/>
            <Button Content="立即重啟" Command="{Binding RestartCommand}" IsEnabled="{Binding CanRestart}"/>
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/Specurai.Desktop/Views/UpdateDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 7.6：驗證編譯**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded

- [ ] **Step 7.7：Commit**

```bash
git add src/Specurai.Desktop/ViewModels/UpdateDialogViewModel.cs \
        src/Specurai.Desktop/Views/UpdateDialog.axaml \
        src/Specurai.Desktop/Views/UpdateDialog.axaml.cs \
        tests/Specurai.Desktop.Tests/ViewModels/UpdateDialogViewModelTests.cs
git commit -m "feat(desktop): 新增 UpdateDialog 與 ViewModel

顯示版本資訊、Release Notes、下載進度，提供確認更新與立即
重啟按鈕，錯誤時保留確認按鈕讓使用者重試。"
```

---

## Task 8：`MacOsUpdateInstructionsDialog`（macOS 手動安裝指引）

**Files:**
- Create: `src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml`
- Create: `src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml.cs`

此對話框無 ViewModel（僅顯示資料、兩個固定動作），資料由外部透過 DataContext 注入 `UpdateCheckResult`。

- [ ] **Step 8.1：建立 View**

Create `src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:domain="using:Specurai.Domain.Entities"
        x:Class="Specurai.Desktop.Views.MacOsUpdateInstructionsDialog"
        x:DataType="domain:UpdateCheckResult"
        Title="有新版本（macOS 手動安裝）"
        Width="620"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False">
    <StackPanel Margin="24" Spacing="12">
        <TextBlock FontSize="18" FontWeight="Bold">
            <Run Text="新版本 v"/><Run Text="{Binding NewVersion}"/>
        </TextBlock>

        <Border BorderBrush="Gray" BorderThickness="1" CornerRadius="4" Padding="12" MaxHeight="200">
            <ScrollViewer>
                <TextBlock Text="{Binding ReleaseNotes}" TextWrapping="Wrap" FontFamily="Consolas"/>
            </ScrollViewer>
        </Border>

        <TextBlock FontWeight="Bold" Text="安裝步驟"/>
        <StackPanel Spacing="6">
            <TextBlock TextWrapping="Wrap" Text="1. 點擊「前往下載」，從 GitHub Release 頁面下載對應架構的 .dmg"/>
            <TextBlock TextWrapping="Wrap" Text="2. 點兩下 .dmg，將 Specurai 拖曳到 Applications"/>
            <TextBlock TextWrapping="Wrap" Text="3. 於終端機執行下列指令解除 Quarantine，然後重新開啟應用程式："/>
        </StackPanel>

        <Border Background="#F4F4F4" CornerRadius="4" Padding="10">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" FontFamily="Consolas"
                           Text="xattr -cr /Applications/Specurai.app"/>
                <Button Grid.Column="1" Content="複製" Click="OnCopyCommandClick"/>
            </Grid>
        </Border>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10">
            <Button Content="關閉" Click="OnCloseClick"/>
            <Button Content="前往下載" Click="OnOpenReleaseClick" Classes="accent"/>
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml.cs`:

```csharp
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Views;

public partial class MacOsUpdateInstructionsDialog : Window
{
    public MacOsUpdateInstructionsDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyCommandClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
            await Clipboard.SetTextAsync("xattr -cr /Applications/Specurai.app");
    }

    private void OnOpenReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateCheckResult result && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = result.ReleaseUrl,
                UseShellExecute = true,
            });
        }
    }
}
```

- [ ] **Step 8.2：驗證編譯**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: Build succeeded

- [ ] **Step 8.3：Commit**

```bash
git add src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml \
        src/Specurai.Desktop/Views/MacOsUpdateInstructionsDialog.axaml.cs
git commit -m "feat(desktop): 新增 MacOsUpdateInstructionsDialog

macOS 降級方案的對話框，顯示 Release Notes、提供可複製的
xattr 指令與前往 GitHub Release 的按鈕。"
```

---

## Task 9：整合到 MainWindow 與 DI

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml`
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml.cs`
- Modify: `src/Specurai.Desktop/Program.cs`

- [ ] **Step 9.1：Program.cs 加入 HttpClient 與 IUpdateService 註冊**

先查閱 `Program.cs:35-47` 區塊確認注入位置，於 `ConfigureServices()` 開頭附近（`services.AddSpecuraiCore();` 之後）加入：

```csharp
// Auto-Update：HttpClient + 平台分派
services.AddSingleton<HttpClient>();
services.AddSingleton<IUpdateService>(sp =>
{
    var currentVersion = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    return UpdateServiceFactory.Create(
        sp.GetRequiredService<HttpClient>(),
        owner: "kerryhuang317",
        repo: "DatabaseDescriptionApp",
        currentVersion: currentVersion);
});
services.AddTransient<UpdateNotificationViewModel>(sp =>
    new UpdateNotificationViewModel(sp.GetRequiredService<IUpdateService>()));
```

頂部 `using` 增加：

```csharp
using System.Net.Http;
using Specurai.Infrastructure.Services;
```

- [ ] **Step 9.2：MainWindowViewModel 加入 UpdateNotification 屬性與命令**

於 `MainWindowViewModel` 建構函式參數末尾加入 `UpdateNotificationViewModel updateNotification`（選擇性參數）並保存為 `UpdateNotification`（`public partial` property），同時加入 `[RelayCommand] CheckForUpdatesAsync` 手動檢查。

修改 `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` 中的 constructor 與屬性：

```csharp
[ObservableProperty]
private UpdateNotificationViewModel? _updateNotification;

public MainWindowViewModel(
    IConnectionManager connectionManager,
    IExportService exportService,
    ITableQueryService tableQueryService,
    ISqlQueryRepository sqlQueryRepository,
    IColumnTypeRepository columnTypeRepository,
    ObjectTreeViewModel objectTree,
    UpdateNotificationViewModel updateNotification)
{
    // ...保留原本的 field assignments...
    _updateNotification = updateNotification;
}

[RelayCommand]
private async Task CheckForUpdatesAsync()
{
    if (_updateNotification is null) return;
    await _updateNotification.CheckAsync();
    // 若已有結果則開對話框；由 View code-behind 實際開啟
    OpenUpdateDialogRequested?.Invoke(_updateNotification.LatestResult);
}

public event Action<UpdateCheckResult?>? OpenUpdateDialogRequested;
```

同時更新 `Program.cs` 中 `MainWindowViewModel` 的 DI 註冊，於建構參數末尾加入 `sp.GetRequiredService<UpdateNotificationViewModel>()`。

- [ ] **Step 9.3：MainWindow.axaml 新增徽章按鈕與選單**

於 `MainWindow.axaml` 頂端 Grid（ColumnDefinitions="*,Auto"）的第二欄 Column 1 內（主題切換按鈕旁）加入：

```xml
<StackPanel Grid.Column="1" Orientation="Horizontal">
    <Button Content="⬆ 有新版本"
            Command="{Binding CheckForUpdatesCommand}"
            IsVisible="{Binding UpdateNotification.HasUpdate, FallbackValue=False}"
            Margin="0,0,8,0"
            ToolTip.Tip="點擊查看新版本資訊"/>
    <!-- 原有主題切換按鈕保留 -->
</StackPanel>
```

於「說明」選單加入「檢查更新」選項（若目前無「說明」選單則新增）：

```xml
<MenuItem Header="說明(_H)">
    <MenuItem Header="檢查更新(_U)" Command="{Binding CheckForUpdatesCommand}"/>
    <!-- 其他項目 -->
</MenuItem>
```

- [ ] **Step 9.4：MainWindow.axaml.cs 連接開啟對話框事件**

於 `MainWindow.axaml.cs` 的 `DataContextChanged` handler 內，新增 `OpenUpdateDialogRequested` 訂閱：

```csharp
DataContextChanged += (_, _) =>
{
    if (DataContext is MainWindowViewModel vm)
    {
        vm.ConfirmSaveCallback = ShowConfirmSaveDialogAsync;
        vm.OpenUpdateDialogRequested += OnOpenUpdateDialogRequested;
    }
};

private void OnOpenUpdateDialogRequested(UpdateCheckResult? result)
{
    if (DataContext is not MainWindowViewModel vm) return;

    if (result is null)
    {
        var current = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        vm.StatusMessage = $"目前已是最新版本（v{current}）";
        return;
    }

    var services = App.Services!;
    var updateService = services.GetRequiredService<IUpdateService>();

    if (result.CanAutoApply)
    {
        var dialogVm = new UpdateDialogViewModel(updateService, result);
        new UpdateDialog { DataContext = dialogVm }.ShowDialog(this);
    }
    else
    {
        new MacOsUpdateInstructionsDialog { DataContext = result }.ShowDialog(this);
    }
}
```

- [ ] **Step 9.5：MainWindow.axaml.cs 啟動時觸發檢查**

於 `MainWindow` 建構函式或 `Loaded` 事件中呼叫：

```csharp
this.Opened += async (_, _) =>
{
    if (DataContext is MainWindowViewModel vm && vm.UpdateNotification is not null)
        await vm.UpdateNotification.CheckAsync();
};
```

- [ ] **Step 9.6：修正既有測試**

`tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs` 所有建構函式呼叫需補上 `UpdateNotificationViewModel` 參數。若該檔案使用設計時建構（無參）則無需修改。

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: PASS（原先通過的案例仍全數通過）

- [ ] **Step 9.7：完整 build 驗證**

Run: `dotnet build`
Expected: Build succeeded，0 Error、≤ 原有 Warning 數

- [ ] **Step 9.8：Commit**

```bash
git add src/Specurai.Desktop/Program.cs \
        src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs \
        src/Specurai.Desktop/Views/MainWindow.axaml \
        src/Specurai.Desktop/Views/MainWindow.axaml.cs \
        tests/Specurai.Desktop.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(desktop): 整合自動更新徽章與選單入口

MainWindow 啟動時背景檢查新版本，右上角顯示徽章，
「說明 → 檢查更新」可手動觸發。依平台開啟 UpdateDialog
（Win/Linux）或 MacOsUpdateInstructionsDialog（macOS）。"
```

---

## Task 10：CI 版本號注入修正

**Files:**
- Modify: `.github/workflows/release.yml`

Velopack runtime 版本由 `vpk pack -v` 注入 assembly metadata，但為避免開發環境或非 Velopack 路徑（macOS）讀到過時的 `1.0.0`，於 `dotnet publish` 時同步注入版本。

- [ ] **Step 10.1：修改三個 publish 步驟**

於 `build-windows`、`build-macos`、`build-linux` 三個 job 的「發布應用程式」步驟分別加 `-p:Version=${{ steps.get-version.outputs.version }}`：

```yaml
# build-windows
- name: 發布應用程式
  run: dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained
       -p:Version=${{ steps.get-version.outputs.version }} -o publish

# build-macos
- name: 發布應用程式
  run: dotnet publish src/Specurai.Desktop -c Release -r ${{ matrix.runtime }} --self-contained
       -p:Version=${{ steps.get-version.outputs.version }} -o publish

# build-linux
- name: 發布應用程式
  run: dotnet publish src/Specurai.Desktop -c Release -r linux-x64 --self-contained
       -p:Version=${{ steps.get-version.outputs.version }} -o publish
```

- [ ] **Step 10.2：Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: publish 時注入 tag 版本至 Desktop assembly

讓 Assembly.GetName().Version 讀到實際發布版本，避免 macOS
更新檢查以過時的 csproj 預設值比對。"
```

---

## Task 11：手動煙霧測試（Velopack 實機驗證）

`VelopackUpdateService` 無法單元測試，此任務手動驗證真實行為。

- [ ] **Step 11.1：準備兩個版本**

本機切到專案根目錄，跑以下指令建立兩個安裝包：

```bash
# 1.0.0-test
dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained -p:Version=1.0.0 -o publish-v1
dotnet tool install -g vpk
vpk pack -u Specurai -v 1.0.0-test -p publish-v1 -e Specurai.Desktop.exe --packTitle "Specurai" -i src/Specurai.Desktop/Assets/Specurai.ico -o Releases-smoke

# 1.1.0-test
dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained -p:Version=1.1.0 -o publish-v2
vpk pack -u Specurai -v 1.1.0-test -p publish-v2 -e Specurai.Desktop.exe --packTitle "Specurai" -i src/Specurai.Desktop/Assets/Specurai.ico -o Releases-smoke
```

- [ ] **Step 11.2：安裝舊版並啟動**

執行 `Releases-smoke/Specurai-Setup.exe`（1.0.0-test）完成安裝，啟動 Specurai。驗證：

- [ ] 右上角未出現「有新版本」徽章（因為 1.0.0 比對 GitHub 最新正式版可能更舊，記錄實際結果）
- [ ] 「說明 → 檢查更新」點擊後顯示「已是最新版本」或開啟對話框

- [ ] **Step 11.3：把 `Releases-smoke` 目錄設為本地 feed（替換 GithubSource）**

臨時修改 `VelopackUpdateService` 將 `new GithubSource(...)` 改為 `new SimpleFileSource(new DirectoryInfo("Releases-smoke"))`，重新安裝 1.0.0-test 並啟動，驗證：

- [ ] 徽章「⬆ 有新版本 v1.1.0-test」出現
- [ ] 點擊後 UpdateDialog 顯示正確版本號與 Release Notes
- [ ] 「確認更新」後進度條由 0 跑到 100，「立即重啟」按鈕啟用
- [ ] 「立即重啟」後 App 關閉並以 1.1.0-test 重啟

還原本地 feed 測試用的程式碼（**不 commit 這個臨時修改**）。

- [ ] **Step 11.4：離線測試**

切換網路為飛航模式，啟動 App。驗證：

- [ ] App 正常啟動、無錯誤對話框
- [ ] 無「有新版本」徽章
- [ ] 主視窗可正常操作

---

## Task 12：文件更新

**Files:**
- Modify: `README.md`
- Modify: `docs/UserGuide.md`（若存在相關章節）

- [ ] **Step 12.1：README.md 新增章節**

於 README 的「功能特色」區塊末尾加入：

```markdown
### 自動更新

- **Windows/Linux**：啟動時自動檢查，偵測到新版本後於主視窗右上角顯示「⬆ 有新版本」徽章；點擊後顯示 Release Notes、下載進度與立即重啟按鈕
- **macOS**：偵測到新版本後顯示對話框，提供下載連結與 `xattr` 解除 Quarantine 指令（macOS 版本未經 Apple 公證，維持手動安裝流程）
- **手動檢查**：「說明 → 檢查更新」可隨時觸發
```

- [ ] **Step 12.2：Commit**

```bash
git add README.md docs/UserGuide.md
git commit -m "docs: 補充自動更新功能使用說明

說明 Windows/Linux 自動更新流程與 macOS 手動安裝降級方案。"
```

---

## 完成驗收

執行全量測試確認通過：

```bash
dotnet test
```

預期新增測試統計：
- Domain：+1 測試（`UpdateCheckResult`）
- Infrastructure：+8 測試（`GitHubReleaseUpdateService` × 7、`UpdateServiceFactory` × 2，但依 OS 僅其中一個平台分支會實跑）
- Desktop：+7 測試（`UpdateNotificationViewModel` × 3、`UpdateDialogViewModel` × 4）

原測試總數 604 → 新總數 ≥ 620。

核對 §8 驗收標準（Spec §8）每一項：
- [ ] Windows 版啟動後右上角正確顯示徽章
- [ ] Linux 版相同行為（煙霧測試需於 Linux 環境或 CI 驗證）
- [ ] macOS 版啟動後偵測新版並顯示指引對話框
- [ ] 離線狀態啟動不跳錯誤、不拖慢 UI
- [ ] 「說明 → 檢查更新」可手動觸發
- [ ] UpdateDialog 可看到 GitHub Release body
- [ ] 「立即重啟」正確套用新版本
- [ ] 所有 ViewModel 皆提供設計時建構函式
- [ ] 新增單元測試全數通過
