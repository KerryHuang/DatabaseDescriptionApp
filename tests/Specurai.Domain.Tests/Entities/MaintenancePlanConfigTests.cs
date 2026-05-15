using System;
using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;

namespace Specurai.Domain.Tests.Entities;

/// <summary>
/// MaintenancePlanConfig 實體測試
/// </summary>
public class MaintenancePlanConfigTests
{
    private static MaintenancePlanConfig CreateConfig(
        string backupPath = @"D:\Backup\",
        string restorePath = @"D:\Restore\") =>
        new()
        {
            DatabaseName = "TestDB",
            BackupPath = backupPath,
            RestorePath = restorePath,
            TestDatabaseName = "TestDB_Test",
            LoginName = "testuser",
            LoginPassword = "password",
            BackupTime = 20000,
            RestoreTime = 60000,
            SelectedSteps = [MaintenancePlanStep.SetRecoveryModel]
        };

    [Theory]
    [InlineData(@"D:\Backup\", true)]
    [InlineData("D:/Backup/", true)]
    [InlineData(@"D:\Backup", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsBackupPathValid_各種路徑_回傳預期結果(string path, bool expected)
    {
        var config = CreateConfig(backupPath: path);
        config.IsBackupPathValid.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"D:\Restore\", true)]
    [InlineData("D:/Restore/", true)]
    [InlineData(@"D:\Restore", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsRestorePathValid_各種路徑_回傳預期結果(string path, bool expected)
    {
        var config = CreateConfig(restorePath: path);
        config.IsRestorePathValid.Should().Be(expected);
    }

    [Fact]
    public void RetentionDays_預設值為7()
    {
        var config = CreateConfig();
        config.RetentionDays.Should().Be(7);
    }

    [Fact]
    public void 預設值_AutoGrowthDataMB為256()
    {
        var config = CreateConfig();

        config.AutoGrowthDataMB.Should().Be(256);
        config.AutoGrowthLogMB.Should().Be(128);
        config.PreExpandBufferGB.Should().Be(5);
        config.CheckDbHour.Should().Be(3);
        config.CheckDbDayOfWeek.Should().Be(DayOfWeek.Sunday);
    }
}
