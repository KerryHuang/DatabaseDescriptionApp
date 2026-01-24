using FluentAssertions;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// BackupInfo 實體測試
/// </summary>
public class BackupInfoTests
{
    #region 基本屬性測試

    [Fact]
    public void BackupInfo_建立時_Id應自動產生()
    {
        // Arrange & Act
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            ConnectionName = "開發環境",
            DatabaseName = "TestDb",
            ServerName = "localhost",
            BackupFilePath = @"C:\Backup\test.bak",
            BackupTime = DateTime.Now,
            BackupType = BackupType.Full
        };

        // Assert
        backup.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void BackupInfo_兩個實例_Id應不同()
    {
        // Arrange & Act
        var backup1 = new BackupInfo { ConnectionId = Guid.NewGuid() };
        var backup2 = new BackupInfo { ConnectionId = Guid.NewGuid() };

        // Assert
        backup1.Id.Should().NotBe(backup2.Id);
    }

    [Fact]
    public void BackupInfo_完整備份_屬性應正確設定()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var backupTime = new DateTime(2026, 1, 24, 10, 30, 0);

        // Act
        var backup = new BackupInfo
        {
            ConnectionId = connectionId,
            ConnectionName = "生產環境",
            DatabaseName = "Production",
            ServerName = "sql-prod",
            BackupFilePath = @"D:\Backups\Production_20260124.bak",
            BackupTime = backupTime,
            BackupType = BackupType.Full,
            FileSizeBytes = 1024 * 1024 * 500, // 500 MB
            IsVerified = true,
            Description = "每日完整備份",
            SqlServerVersion = "16.0.1000.6"
        };

        // Assert
        backup.ConnectionId.Should().Be(connectionId);
        backup.ConnectionName.Should().Be("生產環境");
        backup.DatabaseName.Should().Be("Production");
        backup.ServerName.Should().Be("sql-prod");
        backup.BackupType.Should().Be(BackupType.Full);
        backup.IsVerified.Should().BeTrue();
        backup.Description.Should().Be("每日完整備份");
    }

    #endregion

    #region BackupType 測試

    [Fact]
    public void BackupInfo_完整備份類型_應為Full()
    {
        // Arrange & Act
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            BackupType = BackupType.Full
        };

        // Assert
        backup.BackupType.Should().Be(BackupType.Full);
    }

    [Fact]
    public void BackupInfo_差異備份類型_應為Differential()
    {
        // Arrange & Act
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            BackupType = BackupType.Differential
        };

        // Assert
        backup.BackupType.Should().Be(BackupType.Differential);
    }

    [Fact]
    public void BackupInfo_交易記錄備份類型_應為TransactionLog()
    {
        // Arrange & Act
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            BackupType = BackupType.TransactionLog
        };

        // Assert
        backup.BackupType.Should().Be(BackupType.TransactionLog);
    }

    #endregion

    #region FormattedFileSize 測試

    [Fact]
    public void FormattedFileSize_小於1KB_應顯示Bytes()
    {
        // Arrange
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            FileSizeBytes = 500
        };

        // Assert
        backup.FormattedFileSize.Should().Be("500 B");
    }

    [Fact]
    public void FormattedFileSize_小於1MB_應顯示KB()
    {
        // Arrange
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            FileSizeBytes = 1024 * 512 // 512 KB
        };

        // Assert
        backup.FormattedFileSize.Should().Be("512.00 KB");
    }

    [Fact]
    public void FormattedFileSize_小於1GB_應顯示MB()
    {
        // Arrange
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            FileSizeBytes = 1024 * 1024 * 256 // 256 MB
        };

        // Assert
        backup.FormattedFileSize.Should().Be("256.00 MB");
    }

    [Fact]
    public void FormattedFileSize_大於等於1GB_應顯示GB()
    {
        // Arrange
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            FileSizeBytes = (long)(1024 * 1024 * 1024 * 2.5) // 2.5 GB
        };

        // Assert
        backup.FormattedFileSize.Should().Be("2.50 GB");
    }

    [Fact]
    public void FormattedFileSize_剛好1KB_應正確計算()
    {
        // Arrange
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            FileSizeBytes = 1024
        };

        // Assert
        backup.FormattedFileSize.Should().Be("1.00 KB");
    }

    #endregion

    #region Description 測試

    [Fact]
    public void BackupInfo_Description可為Null()
    {
        // Arrange & Act
        var backup = new BackupInfo
        {
            ConnectionId = Guid.NewGuid(),
            Description = null
        };

        // Assert
        backup.Description.Should().BeNull();
    }

    #endregion

    #region BackupType 枚舉測試

    [Fact]
    public void BackupType_Full_值應為0()
    {
        ((int)BackupType.Full).Should().Be(0);
    }

    [Fact]
    public void BackupType_Differential_值應為1()
    {
        ((int)BackupType.Differential).Should().Be(1);
    }

    [Fact]
    public void BackupType_TransactionLog_值應為2()
    {
        ((int)BackupType.TransactionLog).Should().Be(2);
    }

    #endregion
}
