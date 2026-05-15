using System;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// PerformanceDiagnosticsDocumentViewModel 測試
/// </summary>
public class PerformanceDiagnosticsDocumentViewModelTests
{
    #region 建構函式測試

    [Fact]
    public void 設計時建構_應有完整性檢查空集合與HasSuspectPagesFalse()
    {
        var vm = new PerformanceDiagnosticsDocumentViewModel();
        vm.IntegrityChecks.Should().BeEmpty();
        vm.SuspectPages.Should().BeEmpty();
        vm.CheckDbJobHistories.Should().BeEmpty();
        vm.HasSuspectPages.Should().BeFalse();
        vm.IsLoadingIntegrity.Should().BeFalse();
    }

    [Fact]
    public void HasSuspectPages_當SuspectPages有值_應為True()
    {
        var vm = new PerformanceDiagnosticsDocumentViewModel();
        vm.SuspectPages.Add(new SuspectPage
        {
            DatabaseName = "DB", FileId = 1, PageId = 100,
            EventTypeRaw = 3, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow
        });
        vm.HasSuspectPages.Should().BeTrue();
    }

    #endregion
}
