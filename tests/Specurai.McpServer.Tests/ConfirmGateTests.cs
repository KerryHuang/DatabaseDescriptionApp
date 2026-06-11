using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConfirmGateTests
{
    private static ConnectionProfile SampleProfile(string name = "目前連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p"
    };

    [Fact(DisplayName = "set_recovery_model: confirm=false 不應呼叫服務並回摘要")]
    public async Task SetRecoveryModel_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var service = Substitute.For<IDatabaseRecoveryModelService>();

        var result = await RecoveryModelTools.SetRecoveryModel(service, "DBA", "simple", confirm: false);

        await service.DidNotReceive().SaveChangesAsync(
            Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
        result.Should().Contain("SIMPLE");
    }

    [Fact(DisplayName = "set_recovery_model: confirm=true 應呼叫服務")]
    public async Task SetRecoveryModel_ConfirmTrue_ShouldExecute()
    {
        var service = Substitute.For<IDatabaseRecoveryModelService>();

        var result = await RecoveryModelTools.SetRecoveryModel(service, "DBA", "simple", confirm: true);

        await service.Received(1).SaveChangesAsync(
            Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("已設定");
    }

    [Fact(DisplayName = "restore_run: confirm=false 不應還原並回摘要")]
    public async Task RestoreRun_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var backup = Substitute.For<IBackupService>();

        var result = await RestoreTools.RestoreRun(cm, backup, "/x.bak", "overwrite", confirm: false);

        await backup.DidNotReceive().RestoreDatabaseAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RestoreOptions>(),
            Arg.Any<IProgress<RestoreProgress>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
    }

    [Fact(DisplayName = "restore_run: confirm=true 應執行還原")]
    public async Task RestoreRun_ConfirmTrue_ShouldExecute()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetCurrentProfile().Returns(SampleProfile());
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var backup = Substitute.For<IBackupService>();

        var result = await RestoreTools.RestoreRun(cm, backup, "/x.bak", "overwrite", confirm: true);

        await backup.Received(1).RestoreDatabaseAsync(
            Arg.Any<string>(), "/x.bak", Arg.Any<RestoreOptions>(),
            Arg.Any<IProgress<RestoreProgress>>(), Arg.Any<CancellationToken>());
        result.Should().Contain("還原完成");
    }

    [Fact(DisplayName = "migration_log_resize: confirm=false 不應調整並回摘要")]
    public async Task MigrationLogResize_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { SampleProfile("Target") });
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var executor = Substitute.For<ISchemaMigrationExecutor>();

        var result = await MigrationTools.MigrationLogResize(cm, executor, "Target", 1024, confirm: false);

        await executor.DidNotReceive().ResizeLogAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
        result.Should().Contain("1024");
    }

    [Fact(DisplayName = "migration_log_resize: confirm=true 應執行調整")]
    public async Task MigrationLogResize_ConfirmTrue_ShouldExecute()
    {
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { SampleProfile("Target") });
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var executor = Substitute.For<ISchemaMigrationExecutor>();
        executor.ResizeLogAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ResizeLogResult { IsSuccess = true });

        await MigrationTools.MigrationLogResize(cm, executor, "Target", 1024, confirm: true);

        await executor.Received(1).ResizeLogAsync("conn", 1024, Arg.Any<CancellationToken>());
    }
}
