using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Application.Tests.Services;

public class DdlExecutionServiceTests
{
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();
    private readonly ISqlDdlExecuteRepository _repository = Substitute.For<ISqlDdlExecuteRepository>();

    private DdlExecutionService CreateService() => new(_connectionManager, _repository);

    private static ConnectionProfile Profile(
        DatabaseEnvironment environment = DatabaseEnvironment.Staging, string name = "測試連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Environment = environment
    };

    private const string Ddl = "CREATE TABLE dbo.T1 (Id INT)";

    [Fact(DisplayName = "ExecuteAsync_目前連線為正式環境_應拒絕且不呼叫Repository")]
    public async Task ExecuteAsync_目前連線為正式環境_應拒絕且不呼叫Repository()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Production, "正式庫"));

        var result = await CreateService().ExecuteAsync(Ddl, confirm: true);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
        result.RejectReason.Should().Contain("正式庫");
        await _repository.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_未設定目前連線_應拒絕")]
    public async Task ExecuteAsync_未設定目前連線_應拒絕()
    {
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未設定目前連線");
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId不存在_應拒絕不落回目前連線")]
    public async Task ExecuteAsync_指定profileId不存在_應拒絕不落回目前連線()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetEnabledProfiles().Returns([]);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false, profileId: Guid.NewGuid());

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("找不到指定的連線設定");
        await _repository.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_連線字串為空_應拒絕")]
    public async Task ExecuteAsync_連線字串為空_應拒絕()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetCurrentConnectionString().Returns((string?)null);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("連線字串");
    }

    [Theory(DisplayName = "ExecuteAsync_confirm旗標_應轉為commit傳遞")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_confirm旗標_應轉為commit傳遞(bool confirm)
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _repository.ExecuteAsync(Ddl, "conn", confirm, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Committed = confirm });

        var result = await CreateService().ExecuteAsync(Ddl, confirm);

        result.IsValid.Should().BeTrue();
        await _repository.Received(1).ExecuteAsync(Ddl, "conn", confirm, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId_應使用該連線字串")]
    public async Task ExecuteAsync_指定profileId_應使用該連線字串()
    {
        var target = Profile(name: "目標連線");
        _connectionManager.GetEnabledProfiles().Returns([target]);
        _connectionManager.GetConnectionString(target.Id).Returns("target-conn");
        _repository.ExecuteAsync(Ddl, "target-conn", false, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true });

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false, profileId: target.Id);

        result.IsValid.Should().BeTrue();
        await _repository.Received(1).ExecuteAsync(Ddl, "target-conn", false, Arg.Any<CancellationToken>());
    }
}
