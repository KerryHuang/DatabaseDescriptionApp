using System.Net.Http;
using System.Runtime.InteropServices;
using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class UpdateServiceFactoryTests
{
    [Fact]
    public void Create_Windows或Linux_回傳VelopackUpdateService()
    {
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
