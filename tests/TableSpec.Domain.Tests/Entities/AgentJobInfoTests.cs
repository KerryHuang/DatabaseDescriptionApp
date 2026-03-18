using FluentAssertions;
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// AgentJobInfo 實體測試
/// </summary>
public class AgentJobInfoTests
{
    [Fact]
    public void IsTableSpecJob_描述包含TableSpec標記_回傳True()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            Description = "[TableSpec] 每日備份",
            IsEnabled = true
        };

        job.IsTableSpecJob.Should().BeTrue();
    }

    [Fact]
    public void IsTableSpecJob_描述不包含TableSpec標記_回傳False()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            Description = "一般排程",
            IsEnabled = true
        };

        job.IsTableSpecJob.Should().BeFalse();
    }

    [Fact]
    public void IsTableSpecJob_描述為Null_回傳False()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            Description = null,
            IsEnabled = true
        };

        job.IsTableSpecJob.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "失敗")]
    [InlineData(1, "成功")]
    [InlineData(3, "取消")]
    [InlineData(5, "未知")]
    [InlineData(null, "無記錄")]
    [InlineData(99, "無記錄")]
    public void LastRunOutcomeText_各種結果碼_回傳對應文字(int? outcome, string expected)
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            IsEnabled = true,
            LastRunOutcome = outcome
        };

        job.LastRunOutcomeText.Should().Be(expected);
    }

    [Fact]
    public void StatusText_啟用狀態_回傳啟用()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            IsEnabled = true
        };

        job.StatusText.Should().Be("啟用");
    }

    [Fact]
    public void StatusText_停用狀態_回傳停用()
    {
        var job = new AgentJobInfo
        {
            JobId = Guid.NewGuid(),
            Name = "測試排程",
            IsEnabled = false
        };

        job.StatusText.Should().Be("停用");
    }
}
