using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelBrowseTests
{
    private static MaintenancePlanDocumentViewModel Build(IConnectionManager conn)
    {
        var job = Substitute.For<IAgentJobService>();
        var plan = Substitute.For<IMaintenancePlanService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        var backup = Substitute.For<IBackupService>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task BrowseBackupPath_無目前連線_設定狀態訊息()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = Build(conn);

        await vm.BrowseBackupPathCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("請先選擇連線");
    }

    [Fact]
    public async Task BrowseRestorePath_無目前連線_設定狀態訊息()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var vm = Build(conn);

        await vm.BrowseRestorePathCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("請先選擇連線");
    }
}
