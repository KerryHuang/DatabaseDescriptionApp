using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Tests.ViewModels;

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
}
