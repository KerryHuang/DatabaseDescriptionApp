using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests.ViewModels;

public class SqlQueryDocumentViewModelDdlTests
{
    private static ConnectionProfile Profile(DatabaseEnvironment environment) => new()
    {
        Name = "測試連線",
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Environment = environment
    };

    private static SqlQueryDocumentViewModel CreateViewModel(
        DatabaseEnvironment environment,
        IDdlExecutionService? ddlService)
    {
        var profile = Profile(environment);
        var connectionManager = Substitute.For<IConnectionManager>();
        connectionManager.GetEnabledProfiles().Returns([profile]);
        connectionManager.GetCurrentProfile().Returns(profile);

        return new SqlQueryDocumentViewModel(
            Substitute.For<ISqlQueryRepository>(),
            connectionManager,
            ddlExecutionService: ddlService);
    }

    [Fact(DisplayName = "設計時建構_CanExecuteDdl_應為false")]
    public void 設計時建構_CanExecuteDdl_應為false()
    {
        var vm = new SqlQueryDocumentViewModel();

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "非正式環境且服務可用_CanExecuteDdl_應為true")]
    public void 非正式環境且服務可用_CanExecuteDdl_應為true()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Staging, Substitute.For<IDdlExecutionService>());

        vm.CanExecuteDdl.Should().BeTrue();
    }

    [Fact(DisplayName = "正式環境_CanExecuteDdl_應為false")]
    public void 正式環境_CanExecuteDdl_應為false()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Production, Substitute.For<IDdlExecutionService>());

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "未注入DDL服務_CanExecuteDdl_應為false")]
    public void 未注入DDL服務_CanExecuteDdl_應為false()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService: null);

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "執行DDL_使用者確認_應以confirm true執行")]
    public async Task 執行DDL_使用者確認_應以confirm執行()
    {
        var ddlService = Substitute.For<IDdlExecutionService>();
        var summary = new DdlStatementSummary
        {
            Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1
        };
        ddlService.ExecuteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DdlExecutionResult
            {
                IsValid = true,
                Statements = [summary],
                Committed = ci.ArgAt<bool>(1)
            });

        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService);
        vm.ConfirmExecuteCallback = _ => Task.FromResult(true);
        vm.SqlText = "CREATE TABLE dbo.T1 (Id INT)";

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        await ddlService.Received(1).ExecuteAsync(
            "CREATE TABLE dbo.T1 (Id INT)", false, null, Arg.Any<CancellationToken>());
        await ddlService.Received(1).ExecuteAsync(
            "CREATE TABLE dbo.T1 (Id INT)", true, null, Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已寫入資料庫");
    }

    [Fact(DisplayName = "執行DDL_使用者取消_不應以confirm true執行")]
    public async Task 執行DDL_使用者取消_不應實際執行()
    {
        var ddlService = Substitute.For<IDdlExecutionService>();
        ddlService.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true,
                Statements = [new DdlStatementSummary
                    { Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1 }]
            });

        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService);
        vm.ConfirmExecuteCallback = _ => Task.FromResult(false);
        vm.SqlText = "CREATE TABLE dbo.T1 (Id INT)";

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        await ddlService.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已取消");
    }

    [Fact(DisplayName = "執行DDL_預演即失敗_不應詢問確認")]
    public async Task 執行DDL_預演即失敗_不應詢問確認()
    {
        var ddlService = Substitute.For<IDdlExecutionService>();
        ddlService.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = false, RejectReason = "非白名單語句" });

        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService);
        var asked = false;
        vm.ConfirmExecuteCallback = _ => { asked = true; return Task.FromResult(true); };
        vm.SqlText = "TRUNCATE TABLE dbo.T1";

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        asked.Should().BeFalse();
        vm.StatusMessage.Should().Contain("非白名單語句");
    }

    [Fact(DisplayName = "執行DDL_已停用連線_不應呼叫服務")]
    public async Task 執行DDL_已停用連線_不應呼叫服務()
    {
        var current = Profile(DatabaseEnvironment.Staging);
        var other = Profile(DatabaseEnvironment.Staging);
        var connectionManager = Substitute.For<IConnectionManager>();
        connectionManager.GetEnabledProfiles().Returns([current, other]);
        connectionManager.GetCurrentProfile().Returns(current);
        connectionManager.GetConnectionString(other.Id).Returns((string?)null);

        var ddlService = Substitute.For<IDdlExecutionService>();
        var vm = new SqlQueryDocumentViewModel(
            Substitute.For<ISqlQueryRepository>(),
            connectionManager,
            ddlExecutionService: ddlService)
        {
            SelectedProfile = other,
            SqlText = "CREATE TABLE dbo.T1 (Id INT)"
        };

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        await ddlService.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已停用");
    }
}
