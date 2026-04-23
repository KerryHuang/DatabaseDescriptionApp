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
        _updateService.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult
            {
                NewVersion = "1.7.0",
                ReleaseNotes = "修正",
                ReleaseUrl = "https://...",
                CanAutoApply = true,
            });
        var vm = new UpdateNotificationViewModel(_updateService);

        await vm.CheckAsync();

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
