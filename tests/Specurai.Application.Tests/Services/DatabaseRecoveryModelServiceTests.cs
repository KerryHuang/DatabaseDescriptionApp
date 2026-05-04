using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

public class DatabaseRecoveryModelServiceTests
{
    private readonly IDatabaseRecoveryModelRepository _repository;
    private readonly DatabaseRecoveryModelService _sut;

    public DatabaseRecoveryModelServiceTests()
    {
        _repository = Substitute.For<IDatabaseRecoveryModelRepository>();
        _sut = new DatabaseRecoveryModelService(_repository);
    }

    [Fact]
    public async Task GetAllAsync_應委派至Repository()
    {
        var expected = new List<DatabaseRecoveryModel>
        {
            new() { DatabaseName = "master", RecoveryModel = "SIMPLE" },
            new() { DatabaseName = "leadtech", RecoveryModel = "FULL" }
        };
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.GetAllAsync();

        result.Should().BeEquivalentTo(expected);
        await _repository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_應對每筆變更呼叫SetRecoveryModelAsync()
    {
        var changes = new[]
        {
            ("leadtech", "SIMPLE"),
            ("moldplan", "FULL")
        };

        await _sut.SaveChangesAsync(changes);

        await _repository.Received(1).SetRecoveryModelAsync("leadtech", "SIMPLE", Arg.Any<CancellationToken>());
        await _repository.Received(1).SetRecoveryModelAsync("moldplan", "FULL", Arg.Any<CancellationToken>());
        await _repository.Received(2).SetRecoveryModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_空清單_不呼叫Repository()
    {
        await _sut.SaveChangesAsync([]);

        await _repository.DidNotReceive().SetRecoveryModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
