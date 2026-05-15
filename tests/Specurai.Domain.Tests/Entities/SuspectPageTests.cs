using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class SuspectPageTests
{
    [Theory]
    [InlineData(1, "824 錯誤")]
    [InlineData(2, "不正常 shutdown")]
    [InlineData(3, "校驗失敗")]
    [InlineData(4, "已從備份還原")]
    [InlineData(5, "已修復")]
    [InlineData(7, "已 deallocate")]
    [InlineData(99, "未知 (99)")]
    public void EventTypeText_應依raw值正確解碼(int raw, string expected)
    {
        var p = new SuspectPage
        {
            DatabaseName = "DB", FileId = 1, PageId = 100,
            EventTypeRaw = raw, ErrorCount = 1, LastUpdateDate = DateTime.UtcNow
        };
        p.EventTypeText.Should().Be(expected);
    }
}
