using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// AgentJobService 測試
/// </summary>
public class AgentJobServiceTests
{
    private readonly IAgentJobRepository _repository;
    private readonly AgentJobService _sut;

    public AgentJobServiceTests()
    {
        _repository = Substitute.For<IAgentJobRepository>();
        _sut = new AgentJobService(_repository);
    }

    [Fact]
    public async Task GetJobsAsync_應委派至Repository()
    {
        // Arrange
        var expected = new List<AgentJobInfo>
        {
            new() { JobId = Guid.NewGuid(), Name = "TestJob", IsEnabled = true }
        };
        _repository.GetTableSpecJobsAsync(Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = await _sut.GetJobsAsync();

        // Assert
        result.Should().BeEquivalentTo(expected);
        await _repository.Received(1).GetTableSpecJobsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetJobEnabledAsync_應委派至Repository()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        await _sut.SetJobEnabledAsync(jobId, true);

        // Assert
        await _repository.Received(1).SetJobEnabledAsync(jobId, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartJobAsync_應委派至Repository()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        await _sut.StartJobAsync(jobId);

        // Assert
        await _repository.Received(1).StartJobAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJobAsync_應委派至Repository()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        await _sut.DeleteJobAsync(jobId);

        // Assert
        await _repository.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateScheduleAsync_應委派至Repository()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        await _sut.UpdateScheduleAsync(jobId, 4, 1, 230000);

        // Assert
        await _repository.Received(1).UpdateJobScheduleAsync(jobId, 4, 1, 230000, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJobHistoryAsync_應委派至Repository()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var expected = new List<AgentJobHistory>
        {
            new() { JobId = jobId, StepName = "步驟1", RunDate = DateTime.Now, RunStatus = 1, DurationSeconds = 10 }
        };
        _repository.GetJobHistoryAsync(jobId, 20, Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = await _sut.GetJobHistoryAsync(jobId);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }
}
