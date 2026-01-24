using FluentAssertions;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// RestoreOptions 實體測試
/// </summary>
public class RestoreOptionsTests
{
    #region 還原模式測試

    [Fact]
    public void RestoreOptions_覆蓋現有資料庫_Mode應為OverwriteExisting()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting,
            WithReplace = true
        };

        // Assert
        options.Mode.Should().Be(RestoreMode.OverwriteExisting);
        options.WithReplace.Should().BeTrue();
    }

    [Fact]
    public void RestoreOptions_還原到新資料庫_Mode應為CreateNew()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.CreateNew,
            TargetDatabaseName = "NewDatabase"
        };

        // Assert
        options.Mode.Should().Be(RestoreMode.CreateNew);
        options.TargetDatabaseName.Should().Be("NewDatabase");
    }

    #endregion

    #region 檔案路徑測試

    [Fact]
    public void RestoreOptions_可指定資料檔案路徑()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.CreateNew,
            TargetDatabaseName = "NewDb",
            DataFilePath = @"D:\Data\NewDb.mdf"
        };

        // Assert
        options.DataFilePath.Should().Be(@"D:\Data\NewDb.mdf");
    }

    [Fact]
    public void RestoreOptions_可指定日誌檔案路徑()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.CreateNew,
            TargetDatabaseName = "NewDb",
            LogFilePath = @"D:\Data\NewDb_log.ldf"
        };

        // Assert
        options.LogFilePath.Should().Be(@"D:\Data\NewDb_log.ldf");
    }

    [Fact]
    public void RestoreOptions_檔案路徑可為Null()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting
        };

        // Assert
        options.DataFilePath.Should().BeNull();
        options.LogFilePath.Should().BeNull();
    }

    #endregion

    #region 預設值測試

    [Fact]
    public void RestoreOptions_WithRecovery_預設應為True()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting
        };

        // Assert
        options.WithRecovery.Should().BeTrue();
    }

    [Fact]
    public void RestoreOptions_ShowProgress_預設應為True()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting
        };

        // Assert
        options.ShowProgress.Should().BeTrue();
    }

    [Fact]
    public void RestoreOptions_WithReplace_預設應為False()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting
        };

        // Assert
        options.WithReplace.Should().BeFalse();
    }

    #endregion

    #region 完整選項測試

    [Fact]
    public void RestoreOptions_完整設定_所有屬性應正確()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.CreateNew,
            TargetDatabaseName = "RestoredDb",
            DataFilePath = @"D:\Data\RestoredDb.mdf",
            LogFilePath = @"D:\Data\RestoredDb_log.ldf",
            WithReplace = false,
            WithRecovery = true,
            ShowProgress = true
        };

        // Assert
        options.Mode.Should().Be(RestoreMode.CreateNew);
        options.TargetDatabaseName.Should().Be("RestoredDb");
        options.DataFilePath.Should().Be(@"D:\Data\RestoredDb.mdf");
        options.LogFilePath.Should().Be(@"D:\Data\RestoredDb_log.ldf");
        options.WithReplace.Should().BeFalse();
        options.WithRecovery.Should().BeTrue();
        options.ShowProgress.Should().BeTrue();
    }

    [Fact]
    public void RestoreOptions_NoRecovery模式_WithRecovery應為False()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            Mode = RestoreMode.OverwriteExisting,
            WithRecovery = false // NORECOVERY 模式
        };

        // Assert
        options.WithRecovery.Should().BeFalse();
    }

    #endregion

    #region RestoreMode 枚舉測試

    [Fact]
    public void RestoreMode_OverwriteExisting_值應為0()
    {
        ((int)RestoreMode.OverwriteExisting).Should().Be(0);
    }

    [Fact]
    public void RestoreMode_CreateNew_值應為1()
    {
        ((int)RestoreMode.CreateNew).Should().Be(1);
    }

    #endregion
}
