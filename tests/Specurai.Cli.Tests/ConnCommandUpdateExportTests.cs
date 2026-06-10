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
