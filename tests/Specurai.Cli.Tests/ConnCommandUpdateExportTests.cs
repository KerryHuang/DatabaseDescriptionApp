using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Cli.Commands;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Tests;

public class ConnCommandUpdateExportTests
{
    private static ConnectionProfile NewProfile() => new()
    {
        Name = "原名",
        Server = "old-server",
        Database = "old-db",
        AuthType = AuthenticationType.WindowsAuthentication,
        Username = "old-user",
        Password = "old-pass"
    };

    [Fact(DisplayName = "ApplyProfileUpdates: 只提供 server 應只更新 server")]
    public void ApplyProfileUpdates_OnlyServerProvided_ShouldUpdateServerOnly()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile, newServer: "new-server");

        profile.Server.Should().Be("new-server");
        profile.Name.Should().Be("原名");
        profile.Database.Should().Be("old-db");
        profile.Username.Should().Be("old-user");
    }

    [Fact(DisplayName = "ApplyProfileUpdates: 全部為 null 應保持不變")]
    public void ApplyProfileUpdates_AllNull_ShouldLeaveUnchanged()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile);

        profile.Name.Should().Be("原名");
        profile.Server.Should().Be("old-server");
        profile.Database.Should().Be("old-db");
        profile.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
        profile.Username.Should().Be("old-user");
        profile.Password.Should().Be("old-pass");
    }

    [Fact(DisplayName = "ApplyProfileUpdates: auth=SqlServer 應設為 SQL 認證")]
    public void ApplyProfileUpdates_AuthSqlServer_ShouldSetSqlAuth()
    {
        var profile = NewProfile();

        ConnCommand.ApplyProfileUpdates(profile, newAuthType: "SqlServer");

        profile.AuthType.Should().Be(AuthenticationType.SqlServerAuthentication);
    }

    [Fact(DisplayName = "ApplyProfileUpdates: auth 非 SqlServer 應設為 Windows 認證")]
    public void ApplyProfileUpdates_AuthOther_ShouldSetWindowsAuth()
    {
        var profile = NewProfile();
        profile.AuthType = AuthenticationType.SqlServerAuthentication;

        ConnCommand.ApplyProfileUpdates(profile, newAuthType: "Windows");

        profile.AuthType.Should().Be(AuthenticationType.WindowsAuthentication);
    }

    [Fact(DisplayName = "HasProfileUpdate: 全部為 null 應回傳 false")]
    public void HasProfileUpdate_AllNull_ShouldReturnFalse()
    {
        ConnCommand.HasProfileUpdate(null, null, null, null, null, null).Should().BeFalse();
    }

    [Fact(DisplayName = "HasProfileUpdate: 任一欄位有值應回傳 true")]
    public void HasProfileUpdate_AnyProvided_ShouldReturnTrue()
    {
        ConnCommand.HasProfileUpdate(null, "new-server", null, null, null, null).Should().BeTrue();
    }

    [Fact(DisplayName = "HasProfileUpdate: 空字串視為有值應回傳 true")]
    public void HasProfileUpdate_EmptyString_ShouldReturnTrue()
    {
        ConnCommand.HasProfileUpdate(null, null, null, null, "", null).Should().BeTrue();
    }

    [Fact(DisplayName = "ExportProfilesToFile: 應將服務輸出的位元組寫入指定路徑")]
    public void ExportProfilesToFile_ShouldWriteServiceBytesToPath()
    {
        var profiles = new List<ConnectionProfile> { NewProfile() };
        var exportService = Substitute.For<IConnectionExportService>();
        exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
            .Returns(new byte[] { 1, 2, 3 });
        var path = Path.Combine(Path.GetTempPath(), $"specurai-export-test-{Guid.NewGuid():N}.json");

        try
        {
            var count = ConnCommand.ExportProfilesToFile(exportService, profiles, path, includePasswords: false);

            count.Should().Be(1);
            File.ReadAllBytes(path).Should().Equal(1, 2, 3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact(DisplayName = "ResolveSwitchTarget: 目標連線已停用應回傳 null")]
    public void ResolveSwitchTarget_TargetDisabled_ShouldReturnNull()
    {
        var disabled = new ConnectionProfile
        {
            Name = "正式庫", Server = "s1", Database = "db1", IsEnabled = false
        };
        var cm = Substitute.For<IConnectionManager>();
        cm.GetEnabledProfiles().Returns(new List<ConnectionProfile>().AsReadOnly());
        cm.GetAllProfiles().Returns(new List<ConnectionProfile> { disabled }.AsReadOnly());

        var result = ConnCommand.ResolveSwitchTarget(cm, "正式庫");

        result.Should().BeNull();
    }

    [Fact(DisplayName = "ResolveSwitchTarget: 目標連線啟用應回傳該連線")]
    public void ResolveSwitchTarget_TargetEnabled_ShouldReturnProfile()
    {
        var enabled = new ConnectionProfile
        {
            Name = "測試庫", Server = "s1", Database = "db1", IsEnabled = true
        };
        var cm = Substitute.For<IConnectionManager>();
        cm.GetEnabledProfiles().Returns(new List<ConnectionProfile> { enabled }.AsReadOnly());

        var result = ConnCommand.ResolveSwitchTarget(cm, "測試庫");

        result.Should().Be(enabled);
    }

    [Fact(DisplayName = "ExportProfilesToFile: 應將 includePasswords 旗標傳給服務")]
    public void ExportProfilesToFile_ShouldPassIncludePasswordsFlag()
    {
        var profiles = new List<ConnectionProfile> { NewProfile() };
        var exportService = Substitute.For<IConnectionExportService>();
        exportService.ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), Arg.Any<bool>())
            .Returns(new byte[] { 9 });
        var path = Path.Combine(Path.GetTempPath(), $"specurai-export-test-{Guid.NewGuid():N}.json");

        try
        {
            ConnCommand.ExportProfilesToFile(exportService, profiles, path, includePasswords: true);

            exportService.Received(1).ExportToJson(Arg.Any<IReadOnlyList<ConnectionProfile>>(), true);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
