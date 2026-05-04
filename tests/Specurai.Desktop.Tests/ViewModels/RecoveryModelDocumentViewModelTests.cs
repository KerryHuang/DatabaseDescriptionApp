using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;

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
