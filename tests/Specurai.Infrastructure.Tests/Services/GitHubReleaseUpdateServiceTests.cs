using System.Net;
using System.Net.Http;
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

    private sealed class StubHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
            });
    }
}
