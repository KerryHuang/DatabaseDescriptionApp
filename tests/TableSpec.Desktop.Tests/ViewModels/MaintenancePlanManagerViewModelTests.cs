using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// MaintenancePlanManagerViewModel 測試
/// </summary>
public class MaintenancePlanManagerViewModelTests
{
    private readonly IAgentJobService _jobService;
    private readonly IMaintenancePlanService _planService;

    public MaintenancePlanManagerViewModelTests()
    {
        _jobService = Substitute.For<IAgentJobService>();
        _planService = Substitute.For<IMaintenancePlanService>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new MaintenancePlanManagerViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Jobs.Should().BeEmpty();
        vm.SelectedJob.Should().BeNull();
        vm.StatusMessage.Should().BeEmpty();
        vm.IsLoading.Should().BeFalse();
    }

    #endregion

    #region LoadJobsAsync 測試

    [Fact]
    public async Task LoadJobsAsync_應載入Job清單()
    {
        // Arrange
        var jobs = new List<AgentJobInfo>
        {
            new() { JobId = Guid.NewGuid(), Name = "測試Job1", IsEnabled = true },
            new() { JobId = Guid.NewGuid(), Name = "測試Job2", IsEnabled = false }
        };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(jobs);
        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService);

        // Act
        await vm.LoadJobsCommand.ExecuteAsync(null);

        // Assert
        vm.Jobs.Should().HaveCount(2);
        vm.Jobs[0].Name.Should().Be("測試Job1");
        vm.Jobs[1].Name.Should().Be("測試Job2");
        vm.StatusMessage.Should().Contain("2");
    }

    #endregion

    #region DeleteJobAsync 測試

    [Fact]
    public async Task DeleteJobAsync_應呼叫Service並重新載入()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = new AgentJobInfo { JobId = jobId, Name = "待刪除Job", IsEnabled = true };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(new List<AgentJobInfo>());
        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService)
        {
            SelectedJob = job,
            ConfirmDeleteCallback = () => Task.FromResult(true)
        };

        // Act
        await vm.DeleteJobCommand.ExecuteAsync(null);

        // Assert
        await _jobService.Received(1).DeleteJobAsync(jobId, Arg.Any<CancellationToken>());
        vm.Jobs.Should().BeEmpty();
    }

    #endregion

    #region ToggleJobAsync 測試

    [Fact]
    public async Task ToggleJobAsync_啟用變停用_應呼叫Service()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = new AgentJobInfo { JobId = jobId, Name = "啟用Job", IsEnabled = true };
        _jobService.GetJobsAsync(Arg.Any<CancellationToken>()).Returns(new List<AgentJobInfo> { job });
        var vm = new MaintenancePlanManagerViewModel(_jobService, _planService)
        {
            SelectedJob = job
        };

        // Act
        await vm.ToggleJobCommand.ExecuteAsync(null);

        // Assert
        await _jobService.Received(1).SetJobEnabledAsync(jobId, false, Arg.Any<CancellationToken>());
    }

    #endregion
}
