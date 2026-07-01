using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests;

public class ServerVolumeInfoTests
{
    [Fact]
    public void UsedPercent_有總量_回傳正確百分比()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 25, TotalBytes = 100 };
        v.UsedPercent.Should().BeApproximately(75, 0.001);
        v.UsedPercentValue.Should().BeApproximately(75, 0.001);
    }

    [Fact]
    public void UsedPercent_無總量_回傳null且值為0()
    {
        var v = new ServerVolumeInfo { Name = "D:\\", FreeBytes = 25, TotalBytes = null };
        v.UsedPercent.Should().BeNull();
        v.UsedPercentValue.Should().Be(0);
        v.FormattedTotal.Should().Be("—");
        v.UsedPercentText.Should().Be("—");
    }

    [Fact]
    public void IsLowSpace_可用低於一成_為真並於文字加註警示()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 5, TotalBytes = 100 };
        v.IsLowSpace.Should().BeTrue();
        v.UsedPercentText.Should().Contain("⚠");
    }

    [Fact]
    public void FormattedFree_大於1GB_以GB顯示()
    {
        var v = new ServerVolumeInfo { Name = "C:\\", FreeBytes = 2L * 1024 * 1024 * 1024, TotalBytes = 10L * 1024 * 1024 * 1024 };
        v.FormattedFree.Should().Be("2.0 GB");
    }
}
