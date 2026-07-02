using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelPlatformTests
{
    private static ConnectionProfile Profile() => new()
    {
        Id = Guid.NewGuid(),
        Name = "測試連線",
        Server = "localhost",
        Database = "TestDb"
    };

    private static MaintenancePlanDocumentViewModel Build(IConnectionManager conn, IBackupService backup)
    {
        var job = Substitute.For<IAgentJobService>();
        var plan = Substitute.For<IMaintenancePlanService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task DetectServerPlatform_回傳Linux_設定平台與Linux預設路徑()
    {
        var profile = Profile();
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("cs");
        var backup = Substitute.For<IBackupService>();
        backup.GetServerPlatformAsync("cs", Arg.Any<CancellationToken>()).Returns("Linux");

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Linux");
        vm.BackupPath.Should().Be("/var/opt/mssql/backup/");
        vm.RestorePath.Should().Be("/var/opt/mssql/data/");
    }

    [Fact]
    public async Task DetectServerPlatform_回傳null_維持Windows預設()
    {
        var profile = Profile();
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("cs");
        var backup = Substitute.For<IBackupService>();
        backup.GetServerPlatformAsync("cs", Arg.Any<CancellationToken>()).Returns((string?)null);

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Windows");
    }

    [Fact]
    public async Task DetectServerPlatform_無目前連線_維持預設且不丟例外()
    {
        var conn = Substitute.For<IConnectionManager>();
        conn.GetCurrentProfile().Returns((ConnectionProfile?)null);
        var backup = Substitute.For<IBackupService>();

        var vm = Build(conn, backup);
        await vm.DetectServerPlatformAsync();

        vm.SelectedPlatform.Should().Be("Windows");
    }
}
