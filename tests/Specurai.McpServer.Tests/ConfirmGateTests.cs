using System.Reflection;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class ConfirmGateTests
{
    [Theory(DisplayName = "破壞性工具最後一個參數應為 confirm（bool，預設 false）")]
    [InlineData(typeof(RecoveryModelTools), nameof(RecoveryModelTools.SetRecoveryModel))]
    [InlineData(typeof(RestoreTools), nameof(RestoreTools.RestoreRun))]
    [InlineData(typeof(MigrationTools), nameof(MigrationTools.MigrationApply))]
    [InlineData(typeof(MigrationTools), nameof(MigrationTools.MigrationLogResize))]
    [InlineData(typeof(ConnectionCrudTools), nameof(ConnectionCrudTools.DeleteConnection))]
    [InlineData(typeof(AgentJobTools), nameof(AgentJobTools.DeleteAgentJob))]
    [InlineData(typeof(HealthInstallerTools), nameof(HealthInstallerTools.UninstallHealthMonitoring))]
    [InlineData(typeof(MaintenancePlanTools), nameof(MaintenancePlanTools.ExecuteMaintenancePlan))]
    public void DestructiveTool_LastParam_ShouldBeConfirmBoolDefaultFalse(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var last = method!.GetParameters().Last();
        last.Name.Should().Be("confirm");
        last.ParameterType.Should().Be(typeof(bool));
        last.HasDefaultValue.Should().BeTrue();
        last.DefaultValue.Should().Be(false);
    }

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
        var target = SampleProfile("Target");
        cm.GetAllProfiles().Returns(new[] { target });
        cm.GetEnabledProfiles().Returns(new[] { target });
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
        var target = SampleProfile("Target");
        cm.GetAllProfiles().Returns(new[] { target });
        cm.GetEnabledProfiles().Returns(new[] { target });
        cm.BuildConnectionString(Arg.Any<ConnectionProfile>()).Returns("conn");
        var executor = Substitute.For<ISchemaMigrationExecutor>();
        executor.ResizeLogAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ResizeLogResult { IsSuccess = true });

        await MigrationTools.MigrationLogResize(cm, executor, "Target", 1024, confirm: true);

        await executor.Received(1).ResizeLogAsync("conn", 1024, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "delete_connection: confirm=false 不應刪除並回摘要")]
    public void DeleteConnection_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var cm = Substitute.For<IConnectionManager>();
        var toDelete = SampleProfile("MyConn");
        cm.GetAllProfiles().Returns(new[] { toDelete });
        cm.GetEnabledProfiles().Returns(new[] { toDelete });

        var result = ConnectionCrudTools.DeleteConnection(cm, "MyConn", confirm: false);

        cm.DidNotReceive().DeleteProfile(Arg.Any<Guid>());
        result.Should().Contain("confirm:true");
        result.Should().Contain("MyConn");
    }

    [Fact(DisplayName = "delete_connection: confirm=true 應刪除")]
    public void DeleteConnection_ConfirmTrue_ShouldExecute()
    {
        var profile = SampleProfile("MyConn");
        var cm = Substitute.For<IConnectionManager>();
        cm.GetAllProfiles().Returns(new[] { profile });
        cm.GetEnabledProfiles().Returns(new[] { profile });

        var result = ConnectionCrudTools.DeleteConnection(cm, "MyConn", confirm: true);

        cm.Received(1).DeleteProfile(profile.Id);
        result.Should().Contain("已刪除");
    }

    [Fact(DisplayName = "delete_agent_job: confirm=false 不應刪除並回摘要")]
    public async Task DeleteAgentJob_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var service = Substitute.For<IAgentJobService>();
        var jobId = Guid.NewGuid().ToString();

        var result = await AgentJobTools.DeleteAgentJob(service, jobId, confirm: false);

        await service.DidNotReceive().DeleteJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        result.Should().Contain("confirm:true");
    }

    [Fact(DisplayName = "delete_agent_job: confirm=true 應刪除")]
    public async Task DeleteAgentJob_ConfirmTrue_ShouldExecute()
    {
        var service = Substitute.For<IAgentJobService>();
        var jobId = Guid.NewGuid();

        var result = await AgentJobTools.DeleteAgentJob(service, jobId.ToString(), confirm: true);

        await service.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
        result.Should().Contain("已刪除");
    }

    [Theory(DisplayName = "uninstall_health_monitoring: confirm=false 摘要依範圍顯示且不執行")]
    [InlineData(false, false, "含歷史資料")]
    [InlineData(true, false, "保留歷史資料")]
    [InlineData(false, true, "僅移除排程 Job")]
    public async Task UninstallHealth_ConfirmFalse_SummaryReflectsScope(
        bool keepHistory, bool removeJobsOnly, string expected)
    {
        var service = Substitute.For<IHealthMonitoringService>();

        var result = await HealthInstallerTools.UninstallHealthMonitoring(
            service, keepHistory, removeJobsOnly, confirm: false);

        result.Should().Contain("confirm:true");
        result.Should().Contain(expected);
        await service.DidNotReceive().UninstallAsync(
            Arg.Any<UninstallOptions>(), Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "uninstall_health_monitoring: confirm=true 應移除")]
    public async Task UninstallHealth_ConfirmTrue_ShouldExecute()
    {
        var service = Substitute.For<IHealthMonitoringService>();
        service.UninstallAsync(Arg.Any<UninstallOptions>(), Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>())
            .Returns(new UninstallResult(true));

        await HealthInstallerTools.UninstallHealthMonitoring(service, confirm: true);

        await service.Received(1).UninstallAsync(
            Arg.Any<UninstallOptions>(), Arg.Any<IProgress<InstallProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "execute_maintenance_plan: confirm=false 不應呼叫任何服務並回摘要")]
    public async Task ExecuteMaintenancePlan_ConfirmFalse_ShouldReturnSummaryWithoutExecuting()
    {
        var service = Substitute.For<IMaintenancePlanService>();

        var result = await MaintenancePlanTools.ExecuteMaintenancePlan(
            service, "AppDb", "/bak", "/restore", "AppDb_Test", "sa", "pwd", confirm: false);

        service.ReceivedCalls().Should().BeEmpty();
        result.Should().Contain("confirm:true");
        result.Should().Contain("AppDb");
    }

    [Fact(DisplayName = "execute_maintenance_plan: confirm=true 應執行計劃")]
    public async Task ExecuteMaintenancePlan_ConfirmTrue_ShouldExecute()
    {
        var service = Substitute.For<IMaintenancePlanService>();

        await MaintenancePlanTools.ExecuteMaintenancePlan(
            service, "AppDb", "/bak", "/restore", "AppDb_Test", "sa", "pwd", confirm: true);

        service.ReceivedCalls().Should().NotBeEmpty();
    }
}
