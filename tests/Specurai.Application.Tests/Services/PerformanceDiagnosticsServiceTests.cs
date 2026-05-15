using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

/// <summary>
/// PerformanceDiagnosticsService 測試
/// </summary>
public class PerformanceDiagnosticsServiceTests
{
    [Theory]
    [InlineData(null, IntegrityHealth.Critical)]
    [InlineData(0, IntegrityHealth.Healthy)]
    [InlineData(13, IntegrityHealth.Healthy)]
    [InlineData(14, IntegrityHealth.Warning)]
    [InlineData(29, IntegrityHealth.Warning)]
    [InlineData(30, IntegrityHealth.Critical)]
    [InlineData(100, IntegrityHealth.Critical)]
    public async Task GetIntegrityCheckStatus_應依距今天數正確分級(int? days, IntegrityHealth expected)
    {
        var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
        var rows = new List<LastCheckDbRow>
        {
            new()
            {
                DatabaseName = "DB",
                LastKnownGood = days.HasValue ? DateTime.Now.Date.AddDays(-days.Value) : null
            }
        };
        repo.GetLastCheckDbAsync(Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(rows);

        var svc = new PerformanceDiagnosticsService(repo);
        var results = await svc.GetIntegrityCheckStatusAsync();

        results.Single().Health.Should().Be(expected);
    }

    [Fact]
    public async Task GetSuspectPages_應直接轉發()
    {
        var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
        var data = new List<SuspectPage>
        {
            new() { DatabaseName = "X", FileId = 1, PageId = 100, EventTypeRaw = 3, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow }
        };
        repo.GetSuspectPagesAsync(Arg.Any<CancellationToken>()).Returns(data);

        var svc = new PerformanceDiagnosticsService(repo);
        var results = await svc.GetSuspectPagesAsync();

        results.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task GetCheckDbJobHistory_應傳遞top參數()
    {
        var repo = Substitute.For<IPerformanceDiagnosticsRepository>();
        repo.GetCheckDbJobHistoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<CheckDbJobHistory>());

        var svc = new PerformanceDiagnosticsService(repo);
        await svc.GetCheckDbJobHistoryAsync(20);

        await repo.Received(1).GetCheckDbJobHistoryAsync(20, Arg.Any<CancellationToken>());
    }
}
