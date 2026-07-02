using FluentAssertions;
using Specurai.Domain;
using Xunit;

namespace Specurai.Domain.Tests;

public class ServerPathHelperTests
{
    [Theory]
    [InlineData("C:\\", "a.bak", "C:\\a.bak")]
    [InlineData("D:\\SQLBackup", "a.bak", "D:\\SQLBackup\\a.bak")]
    [InlineData("/var/opt/mssql", "a.bak", "/var/opt/mssql/a.bak")]
    [InlineData("/var/opt/mssql/", "a.bak", "/var/opt/mssql/a.bak")]
    public void Combine_依平台分隔字元組合(string parent, string name, string expected)
    {
        ServerPathHelper.Combine(parent, name).Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Backup\\a.bak", "a.bak")]
    [InlineData("/var/opt/mssql/a.trn", "a.trn")]
    public void GetFileName_取最後一段(string path, string expected)
    {
        ServerPathHelper.GetFileName(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("a.bak", true)]
    [InlineData("A.BAK", true)]
    [InlineData("a.trn", true)]
    [InlineData("a.txt", false)]
    [InlineData("folder", false)]
    public void IsBackupFile_辨識副檔名(string name, bool expected)
    {
        ServerPathHelper.IsBackupFile(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("D:\\SQLBackup", "D:\\SQLBackup\\")]
    [InlineData("D:\\SQLBackup\\", "D:\\SQLBackup\\")]
    [InlineData("/var/opt/mssql/backup", "/var/opt/mssql/backup/")]
    [InlineData("/var/opt/mssql/backup/", "/var/opt/mssql/backup/")]
    public void EnsureTrailingSeparator_補上或維持結尾分隔字元(string path, string expected)
    {
        ServerPathHelper.EnsureTrailingSeparator(path).Should().Be(expected);
    }
}
