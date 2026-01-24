using FluentAssertions;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Tests.Entities;

/// <summary>
/// BackupHistory 實體測試
/// </summary>
public class BackupHistoryTests
{
    #region 基本功能測試

    [Fact]
    public void BackupHistory_初始化_Backups應為空集合()
    {
        // Arrange & Act
        var history = new BackupHistory();

        // Assert
        history.Backups.Should().BeEmpty();
    }

    [Fact]
    public void Add_新增備份_應加入集合()
    {
        // Arrange
        var history = new BackupHistory();
        var backup = CreateTestBackup(Guid.NewGuid());

        // Act
        history.Add(backup);

        // Assert
        history.Backups.Should().ContainSingle();
        history.Backups.Should().Contain(backup);
    }

    [Fact]
    public void Add_新增多個備份_應全部加入()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId = Guid.NewGuid();
        var backup1 = CreateTestBackup(connectionId);
        var backup2 = CreateTestBackup(connectionId);

        // Act
        history.Add(backup1);
        history.Add(backup2);

        // Assert
        history.Backups.Should().HaveCount(2);
    }

    #endregion

    #region Remove 測試

    [Fact]
    public void Remove_存在的備份_應回傳True並移除()
    {
        // Arrange
        var history = new BackupHistory();
        var backup = CreateTestBackup(Guid.NewGuid());
        history.Add(backup);

        // Act
        var result = history.Remove(backup.Id);

        // Assert
        result.Should().BeTrue();
        history.Backups.Should().BeEmpty();
    }

    [Fact]
    public void Remove_不存在的備份_應回傳False()
    {
        // Arrange
        var history = new BackupHistory();
        var backup = CreateTestBackup(Guid.NewGuid());
        history.Add(backup);

        // Act
        var result = history.Remove(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
        history.Backups.Should().ContainSingle();
    }

    #endregion

    #region GetByConnection 測試

    [Fact]
    public void GetByConnection_應回傳指定連線的備份()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId1 = Guid.NewGuid();
        var connectionId2 = Guid.NewGuid();

        history.Add(CreateTestBackup(connectionId1, DateTime.Now.AddDays(-1)));
        history.Add(CreateTestBackup(connectionId1, DateTime.Now));
        history.Add(CreateTestBackup(connectionId2, DateTime.Now));

        // Act
        var result = history.GetByConnection(connectionId1).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(b => b.ConnectionId == connectionId1);
    }

    [Fact]
    public void GetByConnection_應按時間降序排列()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId = Guid.NewGuid();

        var oldBackup = CreateTestBackup(connectionId, DateTime.Now.AddDays(-2));
        var newBackup = CreateTestBackup(connectionId, DateTime.Now);
        var midBackup = CreateTestBackup(connectionId, DateTime.Now.AddDays(-1));

        history.Add(oldBackup);
        history.Add(newBackup);
        history.Add(midBackup);

        // Act
        var result = history.GetByConnection(connectionId).ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].BackupTime.Should().BeAfter(result[1].BackupTime);
        result[1].BackupTime.Should().BeAfter(result[2].BackupTime);
    }

    [Fact]
    public void GetByConnection_無備份_應回傳空集合()
    {
        // Arrange
        var history = new BackupHistory();

        // Act
        var result = history.GetByConnection(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetLatestBackup 測試

    [Fact]
    public void GetLatestBackup_應回傳最新備份()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId = Guid.NewGuid();

        var oldBackup = CreateTestBackup(connectionId, DateTime.Now.AddDays(-1));
        var newBackup = CreateTestBackup(connectionId, DateTime.Now);

        history.Add(oldBackup);
        history.Add(newBackup);

        // Act
        var result = history.GetLatestBackup(connectionId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(newBackup.Id);
    }

    [Fact]
    public void GetLatestBackup_無備份_應回傳Null()
    {
        // Arrange
        var history = new BackupHistory();

        // Act
        var result = history.GetLatestBackup(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region HasRecentBackup 測試

    [Fact]
    public void HasRecentBackup_有最近備份_應回傳True()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId = Guid.NewGuid();
        var recentBackup = CreateTestBackup(connectionId, DateTime.Now.AddHours(-1));
        history.Add(recentBackup);

        // Act
        var result = history.HasRecentBackup(connectionId, TimeSpan.FromDays(1));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasRecentBackup_無最近備份_應回傳False()
    {
        // Arrange
        var history = new BackupHistory();
        var connectionId = Guid.NewGuid();
        var oldBackup = CreateTestBackup(connectionId, DateTime.Now.AddDays(-2));
        history.Add(oldBackup);

        // Act
        var result = history.HasRecentBackup(connectionId, TimeSpan.FromDays(1));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasRecentBackup_無任何備份_應回傳False()
    {
        // Arrange
        var history = new BackupHistory();

        // Act
        var result = history.HasRecentBackup(Guid.NewGuid(), TimeSpan.FromDays(1));

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static BackupInfo CreateTestBackup(Guid connectionId, DateTime? backupTime = null)
    {
        return new BackupInfo
        {
            ConnectionId = connectionId,
            ConnectionName = "測試連線",
            DatabaseName = "TestDb",
            ServerName = "localhost",
            BackupFilePath = @"C:\Backup\test.bak",
            BackupTime = backupTime ?? DateTime.Now,
            BackupType = BackupType.Full,
            FileSizeBytes = 1024 * 1024
        };
    }

    #endregion
}
