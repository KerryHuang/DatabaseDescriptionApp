using System.Net.Http;
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
