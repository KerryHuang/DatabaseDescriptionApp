using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class DatabaseFileInfoTests
{
    [Fact]
    public void 建立_應正確設定所有屬性()
    {
        var info = new DatabaseFileInfo
        {
            LogicalName = "MyDb",
            PhysicalName = @"D:\Data\MyDb.mdf",
            FileType = DatabaseFileType.Data,
            SizeMB = 25600,
            FreeMB = 1280,
            IsPercentGrowth = false,
            GrowthMB = 256,
            VolumeMountPoint = @"D:\",
            VolumeFreeGB = 50
        };

        info.LogicalName.Should().Be("MyDb");
        info.FileType.Should().Be(DatabaseFileType.Data);
        info.FreePercent.Should().BeApproximately(5.0m, 0.01m);
    }

    [Fact]
    public void FreePercent_當SizeMB為零_應回傳零()
    {
        var info = new DatabaseFileInfo
        {
            LogicalName = "L",
            PhysicalName = "P",
            FileType = DatabaseFileType.Log,
            SizeMB = 0,
            FreeMB = 0,
            IsPercentGrowth = false,
            GrowthMB = 0,
            VolumeMountPoint = "X",
            VolumeFreeGB = null
        };

        info.FreePercent.Should().Be(0);
    }
}
