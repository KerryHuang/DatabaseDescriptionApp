using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests;

public class DmlExecutionServiceTests
{
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();
    private readonly ISqlDryRunRepository _dryRunRepo = Substitute.For<ISqlDryRunRepository>();
    private readonly ISqlDmlExecuteRepository _executeRepo = Substitute.For<ISqlDmlExecuteRepository>();

    private DmlExecutionService CreateService()
        => new(_connectionManager, _dryRunRepo, _executeRepo);

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

    [Fact(DisplayName = "ExecuteAsync_正式環境_應拒絕且不呼叫任何Repository")]
    public async Task ExecuteAsync_Production_ShouldRejectWithoutCallingRepositories()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Production, "正式庫"));

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: true);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
        result.Committed.Should().BeFalse();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
        await _executeRepo.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Theory(DisplayName = "ExecuteAsync_非正式環境未confirm_應走DryRun")]
    [InlineData(DatabaseEnvironment.Development)]
    [InlineData(DatabaseEnvironment.Testing)]
    [InlineData(DatabaseEnvironment.Staging)]
    public async Task ExecuteAsync_NonProductionWithoutConfirm_ShouldDryRun(DatabaseEnvironment env)
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(env));
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _dryRunRepo.DryRunAsync("UPDATE T SET A = 1", "conn", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, AffectedRowCount = 3 });

        var result = await CreateService().ExecuteAsync("UPDATE T SET A = 1", confirm: false);

        result.AffectedRowCount.Should().Be(3);
        await _executeRepo.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_非正式環境confirm_應走Execute")]
    public async Task ExecuteAsync_NonProductionWithConfirm_ShouldExecute()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Staging));
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _executeRepo.ExecuteAsync("DELETE FROM T WHERE Id = 1", "conn", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, AffectedRowCount = 1, Committed = true });

        var result = await CreateService().ExecuteAsync("DELETE FROM T WHERE Id = 1", confirm: true);

        result.Committed.Should().BeTrue();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId_應以該連線環境與連線字串為準")]
    public async Task ExecuteAsync_WithProfileId_ShouldUseThatProfile()
    {
        var profile = Profile(DatabaseEnvironment.Testing, "測試2");
        _connectionManager.GetEnabledProfiles().Returns([profile]);
        _connectionManager.GetConnectionString(profile.Id).Returns("conn2");
        _dryRunRepo.DryRunAsync("DELETE FROM T", "conn2", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true });

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false, profile.Id);

        result.IsValid.Should().BeTrue();
        await _dryRunRepo.Received(1).DryRunAsync("DELETE FROM T", "conn2", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId為正式環境_應拒絕")]
    public async Task ExecuteAsync_WithProductionProfileId_ShouldReject()
    {
        var profile = Profile(DatabaseEnvironment.Production, "正式庫");
        _connectionManager.GetEnabledProfiles().Returns([profile]);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: true, profile.Id);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
    }

    [Fact(DisplayName = "ExecuteAsync_找不到profile_應拒絕不靜默落回目前連線")]
    public async Task ExecuteAsync_ProfileNotFound_ShouldRejectWithoutFallback()
    {
        _connectionManager.GetEnabledProfiles().Returns([]);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false, Guid.NewGuid());

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrEmpty();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_無目前連線_應拒絕")]
    public async Task ExecuteAsync_NoCurrentProfile_ShouldReject()
    {
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false);

        result.IsValid.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_連線字串取不到_應拒絕")]
    public async Task ExecuteAsync_NoConnectionString_ShouldReject()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Development));
        _connectionManager.GetCurrentConnectionString().Returns((string?)null);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false);

        result.IsValid.Should().BeFalse();
    }
}
