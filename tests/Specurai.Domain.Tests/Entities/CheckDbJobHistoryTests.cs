using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class CheckDbJobHistoryTests
{
    [Theory]
    [InlineData(1, "成功")]
    [InlineData(0, "失敗")]
    [InlineData(3, "取消")]
    [InlineData(4, "重試")]
    [InlineData(99, "其他")]
    public void StatusText_應依RunStatus正確解碼(int status, string expected)
    {
        var h = new CheckDbJobHistory
        {
            JobName = "DB_CheckDb",
            RunAt = new DateTime(2026, 5, 14, 3, 0, 0),
            Duration = TimeSpan.FromMinutes(2),
            RunStatus = status,
            Message = ""
        };
        h.StatusText.Should().Be(expected);
    }
}
