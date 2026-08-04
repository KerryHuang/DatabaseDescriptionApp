using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Tests;

public class SqlQueryDocumentViewModelDmlTests
{
    private static ConnectionProfile Profile(DatabaseEnvironment env, string name = "測試連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p",
        Environment = env
    };

    private static SqlQueryDocumentViewModel CreateVm(
        DatabaseEnvironment env, IDmlExecutionService? dmlService = null)
    {
        var profile = Profile(env);
        var cm = Substitute.For<IConnectionManager>();
        cm.GetEnabledProfiles().Returns([profile]);
        cm.GetCurrentProfile().Returns(profile);
        var queryRepo = Substitute.For<ISqlQueryRepository>();

        return new SqlQueryDocumentViewModel(
            queryRepo, cm,
            Substitute.For<ISqlDryRunRepository>(),
            updateSqlGenerator: null,
            dmlExecutionService: dmlService ?? Substitute.For<IDmlExecutionService>());
    }

    [Fact(DisplayName = "CanExecuteDml_正式環境連線_應為false")]
    public void CanExecuteDml_Production_ShouldBeFalse()
    {
        var vm = CreateVm(DatabaseEnvironment.Production);
        vm.CanExecuteDml.Should().BeFalse();
    }

    [Fact(DisplayName = "CanExecuteDml_非正式環境連線_應為true")]
    public void CanExecuteDml_NonProduction_ShouldBeTrue()
    {
        var vm = CreateVm(DatabaseEnvironment.Testing);
        vm.CanExecuteDml.Should().BeTrue();
    }

    [Fact(DisplayName = "ExecuteDml_確認回呼拒絕_不應confirm執行")]
    public async Task ExecuteDml_ConfirmDeclined_ShouldNotCommit()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Delete, AffectedRowCount = 5 });
        var vm = CreateVm(DatabaseEnvironment.Testing, service);
        vm.SqlText = "DELETE FROM T";
        vm.ConfirmExecuteCallback = _ => Task.FromResult(false);

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        await service.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("取消");
    }

    [Fact(DisplayName = "ExecuteDml_確認後_應confirm執行並回報已寫入")]
    public async Task ExecuteDml_Confirmed_ShouldCommit()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Update, AffectedRowCount = 2 });
        service.ExecuteAsync(Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Update, AffectedRowCount = 2, Committed = true });
        var vm = CreateVm(DatabaseEnvironment.Staging, service);
        vm.SqlText = "UPDATE T SET A = 1 WHERE Id = 9";
        vm.ConfirmExecuteCallback = _ => Task.FromResult(true);

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        await service.Received(1).ExecuteAsync(
            "UPDATE T SET A = 1 WHERE Id = 9", true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已寫入");
    }

    [Fact(DisplayName = "ExecuteDml_預演即失敗_不應詢問確認")]
    public async Task ExecuteDml_PreviewInvalid_ShouldNotAskConfirm()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = false, RejectReason = "偵測到 2 個陳述式" });
        var vm = CreateVm(DatabaseEnvironment.Testing, service);
        vm.SqlText = "DELETE FROM A; DELETE FROM B";
        var asked = false;
        vm.ConfirmExecuteCallback = _ => { asked = true; return Task.FromResult(true); };

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        asked.Should().BeFalse();
        vm.StatusMessage.Should().Contain("陳述式");
    }
}
