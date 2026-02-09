using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// ColumnSearchService 測試
/// </summary>
public class ColumnSearchServiceTests
{
    private readonly IConnectionManager _connectionManager;

    public ColumnSearchServiceTests()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 單資料庫搜尋

    [Fact]
    public async Task SearchColumnsMultiAsync_單資料庫_應填入DatabaseName()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var profile = new ConnectionProfile
        {
            Id = profileId,
            Name = "開發環境",
            Server = "localhost",
            Database = "DevDb"
        };

        _connectionManager.GetConnectionString(profileId).Returns("Server=localhost;Database=DevDb");
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var repo = Substitute.For<ISqlQueryRepository>();
        repo.SearchColumnsAsync("UserId", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ColumnSearchResult>
        {
            new() { ColumnName = "UserId", ObjectName = "Users", SchemaName = "dbo", ObjectType = "TABLE", DataType = "int" }
        });

        ISqlQueryRepository RepoFactory(string? connStr) => repo;
        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        // Act
        var results = await service.SearchColumnsMultiAsync("UserId", new[] { profileId });

        // Assert
        results.Should().HaveCount(1);
        results[0].DatabaseName.Should().Be("DevDb");
        results[0].ColumnName.Should().Be("UserId");
    }

    #endregion

    #region 多資料庫搜尋

    [Fact]
    public async Task SearchColumnsMultiAsync_多資料庫_應合併結果並按資料庫名稱排序()
    {
        // Arrange
        var profileId1 = Guid.NewGuid();
        var profileId2 = Guid.NewGuid();
        var profile1 = new ConnectionProfile
        {
            Id = profileId1, Name = "環境A", Server = "server1", Database = "AlphaDb"
        };
        var profile2 = new ConnectionProfile
        {
            Id = profileId2, Name = "環境B", Server = "server2", Database = "BetaDb"
        };

        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile1, profile2 });
        _connectionManager.GetConnectionString(profileId1).Returns("Server=server1;Database=AlphaDb");
        _connectionManager.GetConnectionString(profileId2).Returns("Server=server2;Database=BetaDb");

        var repo1 = Substitute.For<ISqlQueryRepository>();
        var repo2 = Substitute.For<ISqlQueryRepository>();

        repo1.SearchColumnsAsync("Id", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ColumnSearchResult>
        {
            new() { ColumnName = "Id", ObjectName = "Orders", SchemaName = "dbo", ObjectType = "TABLE", DataType = "int" }
        });
        repo2.SearchColumnsAsync("Id", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ColumnSearchResult>
        {
            new() { ColumnName = "Id", ObjectName = "Users", SchemaName = "dbo", ObjectType = "TABLE", DataType = "int" }
        });

        ISqlQueryRepository RepoFactory(string? connStr) =>
            connStr == "Server=server1;Database=AlphaDb" ? repo1 : repo2;

        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        // Act
        var results = await service.SearchColumnsMultiAsync("Id", new[] { profileId1, profileId2 });

        // Assert
        results.Should().HaveCount(2);
        results[0].DatabaseName.Should().Be("AlphaDb");
        results[1].DatabaseName.Should().Be("BetaDb");
    }

    #endregion

    #region 空連線字串

    [Fact]
    public async Task SearchColumnsMultiAsync_空連線字串_應跳過該資料庫()
    {
        // Arrange
        var profileId1 = Guid.NewGuid();
        var profileId2 = Guid.NewGuid();
        var profile2 = new ConnectionProfile
        {
            Id = profileId2, Name = "正常環境", Server = "server", Database = "NormalDb"
        };

        _connectionManager.GetConnectionString(profileId1).Returns((string?)null);
        _connectionManager.GetConnectionString(profileId2).Returns("Server=server;Database=NormalDb");
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile2 });

        var repo = Substitute.For<ISqlQueryRepository>();
        repo.SearchColumnsAsync("Name", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ColumnSearchResult>
        {
            new() { ColumnName = "Name", ObjectName = "Users", SchemaName = "dbo", ObjectType = "TABLE", DataType = "nvarchar(50)" }
        });

        ISqlQueryRepository RepoFactory(string? connStr) => repo;
        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        // Act
        var results = await service.SearchColumnsMultiAsync("Name", new[] { profileId1, profileId2 });

        // Assert
        results.Should().HaveCount(1);
        results[0].DatabaseName.Should().Be("NormalDb");
    }

    #endregion

    #region 部分失敗

    [Fact]
    public async Task SearchColumnsMultiAsync_部分資料庫失敗_不影響其他結果()
    {
        // Arrange
        var profileId1 = Guid.NewGuid();
        var profileId2 = Guid.NewGuid();
        var profile1 = new ConnectionProfile
        {
            Id = profileId1, Name = "故障環境", Server = "down-server", Database = "FailDb"
        };
        var profile2 = new ConnectionProfile
        {
            Id = profileId2, Name = "正常環境", Server = "server", Database = "OkDb"
        };

        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile1, profile2 });
        _connectionManager.GetConnectionString(profileId1).Returns("Server=down-server;Database=FailDb");
        _connectionManager.GetConnectionString(profileId2).Returns("Server=server;Database=OkDb");

        var failRepo = Substitute.For<ISqlQueryRepository>();
        var okRepo = Substitute.For<ISqlQueryRepository>();

        failRepo.SearchColumnsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("連線逾時"));
        okRepo.SearchColumnsAsync("Id", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ColumnSearchResult>
        {
            new() { ColumnName = "Id", ObjectName = "Users", SchemaName = "dbo", ObjectType = "TABLE", DataType = "int" }
        });

        ISqlQueryRepository RepoFactory(string? connStr) =>
            connStr == "Server=down-server;Database=FailDb" ? failRepo : okRepo;

        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        // Act
        var results = await service.SearchColumnsMultiAsync("Id", new[] { profileId1, profileId2 });

        // Assert
        results.Should().HaveCount(1);
        results[0].DatabaseName.Should().Be("OkDb");
    }

    [Fact]
    public async Task SearchColumnsMultiAsync_部分失敗_應透過Progress回報錯誤()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var profile = new ConnectionProfile
        {
            Id = profileId, Name = "故障環境", Server = "down", Database = "FailDb"
        };

        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        _connectionManager.GetConnectionString(profileId).Returns("Server=down;Database=FailDb");

        var repo = Substitute.For<ISqlQueryRepository>();
        repo.SearchColumnsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("連線逾時"));

        ISqlQueryRepository RepoFactory(string? connStr) => repo;
        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        var progressMessages = new List<string>();
        var progress = Substitute.For<IProgress<string>>();
        progress.When(p => p.Report(Arg.Any<string>())).Do(ci => progressMessages.Add(ci.Arg<string>()));

        // Act
        var results = await service.SearchColumnsMultiAsync("Id", new[] { profileId }, false, progress);

        // Assert
        results.Should().BeEmpty();
        progressMessages.Should().Contain(m => m.Contains("失敗") && m.Contains("FailDb"));
    }

    #endregion

    #region 取消操作

    [Fact]
    public async Task SearchColumnsMultiAsync_取消操作_應拋出OperationCanceledException()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        _connectionManager.GetConnectionString(profileId).Returns("Server=server;Database=Db");
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>
        {
            new() { Id = profileId, Name = "測試", Server = "server", Database = "Db" }
        });

        var repo = Substitute.For<ISqlQueryRepository>();
        repo.SearchColumnsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        ISqlQueryRepository RepoFactory(string? connStr) => repo;
        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchColumnsMultiAsync("Id", new[] { profileId }, ct: cts.Token));
    }

    #endregion

    #region 空 profileIds

    [Fact]
    public async Task SearchColumnsMultiAsync_空ProfileIds_應返回空結果()
    {
        // Arrange
        ISqlQueryRepository RepoFactory(string? connStr) => Substitute.For<ISqlQueryRepository>();
        var service = new ColumnSearchService(_connectionManager, RepoFactory);

        // Act
        var results = await service.SearchColumnsMultiAsync("Id", Array.Empty<Guid>());

        // Assert
        results.Should().BeEmpty();
    }

    #endregion
}
