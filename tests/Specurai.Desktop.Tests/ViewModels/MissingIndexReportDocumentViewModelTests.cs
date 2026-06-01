using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// MissingIndexReportDocumentViewModel 測試
/// </summary>
public class MissingIndexReportDocumentViewModelTests
{
    [Fact]
    public void 設計時建構_DatabaseOptions應只有全部且預設選取全部()
    {
        var vm = new MissingIndexReportDocumentViewModel();

        vm.DatabaseOptions.Should().ContainSingle().Which.Should().Be("全部");
        vm.DatabaseFilter.Should().Be("全部");
    }

    [Fact]
    public async Task LoadDatabaseOptionsAsync_無服務_應維持只有全部()
    {
        var vm = new MissingIndexReportDocumentViewModel();

        await vm.LoadDatabaseOptionsAsync();

        vm.DatabaseOptions.Should().ContainSingle().Which.Should().Be("全部");
    }

    [Fact]
    public async Task LoadDatabaseOptionsAsync_有服務_應以伺服器資料庫填充選項並選取全部()
    {
        var service = Substitute.For<IPerformanceDiagnosticsService>();
        service.GetUserDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Beta", "Alpha" });
        var vm = new MissingIndexReportDocumentViewModel(service);

        await vm.LoadDatabaseOptionsAsync();

        // 「全部」在最前，其餘依名稱排序
        vm.DatabaseOptions.Should().Equal("全部", "Alpha", "Beta");
        vm.DatabaseFilter.Should().Be("全部");
    }

    [Fact]
    public async Task LoadDatabaseOptionsAsync_既有選取仍存在_應保留使用者選取()
    {
        var service = Substitute.For<IPerformanceDiagnosticsService>();
        service.GetUserDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Alpha", "Beta" });
        var vm = new MissingIndexReportDocumentViewModel(service);
        vm.DatabaseFilter = "Alpha"; // 模擬使用者已選取特定資料庫

        await vm.LoadDatabaseOptionsAsync();

        vm.DatabaseFilter.Should().Be("Alpha");
    }

    [Fact]
    public async Task LoadDatabaseOptionsAsync_服務拋例外_應靜默且維持只有全部()
    {
        var service = Substitute.For<IPerformanceDiagnosticsService>();
        service.GetUserDatabasesAsync(Arg.Any<CancellationToken>())
            .Throws(new Exception("boom"));
        var vm = new MissingIndexReportDocumentViewModel(service);

        var act = async () => await vm.LoadDatabaseOptionsAsync();

        await act.Should().NotThrowAsync();
        vm.DatabaseOptions.Should().ContainSingle().Which.Should().Be("全部");
    }
}
