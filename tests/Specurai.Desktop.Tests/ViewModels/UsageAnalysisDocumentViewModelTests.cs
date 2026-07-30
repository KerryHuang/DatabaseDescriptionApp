using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// UsageAnalysisDocumentViewModel 測試
/// </summary>
public class UsageAnalysisDocumentViewModelTests
{
    private readonly IUsageAnalysisService _service;
    private readonly IConnectionManager _connectionManager;

    public UsageAnalysisDocumentViewModelTests()
    {
        _service = Substitute.For<IUsageAnalysisService>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.Should().NotBeNull();
        vm.Title.Should().Be("使用狀態分析");
    }

    [Fact]
    public void DocumentType_應為UsageAnalysis()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.DocumentType.Should().Be("UsageAnalysis");
    }

    [Fact]
    public void 初始狀態_YearsThreshold應為2()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.YearsThreshold.Should().Be(2);
    }

    [Fact]
    public void 初始狀態_IsCompareMode應為False()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.IsCompareMode.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_IsScanning應為False()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.IsScanning.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_StatusMessage應包含掃描提示()
    {
        var vm = new UsageAnalysisDocumentViewModel();
        vm.StatusMessage.Should().Contain("掃描");
    }

    [Fact]
    public void SelectedBaseProfile變更_目標連線清單只列啟用連線且排除自身()
    {
        // 對應審查補測試項目：OnSelectedBaseProfileChanged 目前完全沒有觸及。
        // TargetProfileItems 應只來自 GetEnabledProfiles()，且排除 base profile 自身。
        var basis = new ConnectionProfile { Name = "基準", Server = "s0", Database = "db0" };
        var target1 = new ConnectionProfile { Name = "目標1", Server = "s1", Database = "db1" };
        var target2 = new ConnectionProfile { Name = "目標2", Server = "s2", Database = "db2" };
        _connectionManager.GetEnabledProfiles().Returns(new List<ConnectionProfile> { basis, target1, target2 });

        var vm = new UsageAnalysisDocumentViewModel(_service, _connectionManager);

        vm.SelectedBaseProfile = basis;

        vm.TargetProfileItems.Select(item => item.Profile.Name)
            .Should().BeEquivalentTo(new[] { "目標1", "目標2" });
    }
}
