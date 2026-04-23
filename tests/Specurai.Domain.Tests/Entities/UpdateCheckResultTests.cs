using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class UpdateCheckResultTests
{
    [Fact]
    public void 可透過Init建立實例並保留所有欄位()
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            NewVersion = "1.7.0",
            ReleaseNotes = "修正若干問題",
            ReleaseUrl = "https://github.com/example/repo/releases/tag/v1.7.0",
            CanAutoApply = true,
        };

        // Assert
        result.NewVersion.Should().Be("1.7.0");
        result.ReleaseNotes.Should().Be("修正若干問題");
        result.ReleaseUrl.Should().Be("https://github.com/example/repo/releases/tag/v1.7.0");
        result.CanAutoApply.Should().BeTrue();
    }
}
