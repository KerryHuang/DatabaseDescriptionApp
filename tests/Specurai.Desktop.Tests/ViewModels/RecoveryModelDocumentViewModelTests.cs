using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

public class RecoveryModelDocumentViewModelTests
{
    private readonly IDatabaseRecoveryModelService _service;

    public RecoveryModelDocumentViewModelTests()
    {
        _service = Substitute.For<IDatabaseRecoveryModelService>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new RecoveryModelDocumentViewModel();

        vm.Should().NotBeNull();
        vm.Title.Should().Be("Recovery Model 管理");
        vm.Icon.Should().Be("🔧");
        vm.DocumentType.Should().Be("RecoveryModel");
        vm.Rows.Should().BeEmpty();
        vm.IsLoading.Should().BeFalse();
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void HasChanges_無變更時_應為false()
    {
        var vm = new RecoveryModelDocumentViewModel();

        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCommand_確認回傳false_不呼叫SaveChangesAsync()
    {
        var vm = new RecoveryModelDocumentViewModel(_service);
        vm.ConfirmCallback = _ => Task.FromResult(false);
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<DatabaseRecoveryModel>
        {
            new() { DatabaseName = "leadtech", RecoveryModel = "FULL" }
        });
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Rows[0].SelectedRecoveryModel = "SIMPLE";

        await vm.SaveCommand.ExecuteAsync(null);

        await _service.DidNotReceive().SaveChangesAsync(Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_確認回傳true_呼叫SaveChangesAsync含有變更的資料庫()
    {
        var vm = new RecoveryModelDocumentViewModel(_service);
        vm.ConfirmCallback = _ => Task.FromResult(true);
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<DatabaseRecoveryModel>
        {
            new() { DatabaseName = "leadtech", RecoveryModel = "FULL" },
            new() { DatabaseName = "master", RecoveryModel = "SIMPLE" }
        });
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Rows[0].SelectedRecoveryModel = "SIMPLE"; // leadtech 變更

        await vm.SaveCommand.ExecuteAsync(null);

        await _service.Received(1).SaveChangesAsync(
            Arg.Is<IEnumerable<(string, string)>>(c => c.Single().Item1 == "leadtech" && c.Single().Item2 == "SIMPLE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadCommand_載入後_HasChanges應為false()
    {
        _service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<DatabaseRecoveryModel>
        {
            new() { DatabaseName = "master", RecoveryModel = "SIMPLE" },
            new() { DatabaseName = "leadtech", RecoveryModel = "FULL" }
        });
        var vm = new RecoveryModelDocumentViewModel(_service);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(2);
        vm.HasChanges.Should().BeFalse();
        vm.DirtyCount.Should().Be(0);
    }

    [Fact]
    public void HasChanges_修改列後_應為true且DirtyCount正確()
    {
        var vm = new RecoveryModelDocumentViewModel(_service);
        var row1 = new RecoveryModelRowViewModel("leadtech", "FULL");
        var row2 = new RecoveryModelRowViewModel("master", "SIMPLE");
        row1.DirtyChanged += (_, _) => vm.NotifyHasChanges();
        vm.Rows.Add(row1);
        vm.Rows.Add(row2);

        row1.SelectedRecoveryModel = "SIMPLE";

        vm.HasChanges.Should().BeTrue();
        vm.DirtyCount.Should().Be(1);
    }
}

public class RecoveryModelRowViewModelTests
{
    [Fact]
    public void IsDirty_未變更時_應為false()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");

        row.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsDirty_變更SelectedRecoveryModel後_應為true()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");

        row.SelectedRecoveryModel = "SIMPLE";

        row.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_變更後還原原值_應為false()
    {
        var row = new RecoveryModelRowViewModel("leadtech", "FULL");
        row.SelectedRecoveryModel = "SIMPLE";

        row.SelectedRecoveryModel = "FULL";

        row.IsDirty.Should().BeFalse();
    }
}
