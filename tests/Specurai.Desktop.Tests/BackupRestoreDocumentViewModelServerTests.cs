using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class BackupRestoreDocumentViewModelServerTests
{
    private static (BackupRestoreDocumentViewModel vm, IBackupService svc) Build()
    {
        var svc = Substitute.For<IBackupService>();
        var conn = Substitute.For<IConnectionManager>();

        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "測試連線",
            Server = "localhost",
            Database = "TestDb"
        };
        conn.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });
        conn.GetEnabledProfiles().Returns(new List<ConnectionProfile> { profile });
        conn.GetCurrentProfile().Returns(profile);
        conn.GetConnectionString(profile.Id).Returns("Server=localhost;Database=TestDb;");

        svc.GetServerVolumesAsync("Server=localhost;Database=TestDb;", Arg.Any<CancellationToken>())
            .Returns(new List<ServerVolumeInfo>
            {
                new() { Name = "C:\\", FreeBytes = 100, TotalBytes = 200 },
                new() { Name = "D:\\", FreeBytes = 50, TotalBytes = null }
            });

        var vm = new BackupRestoreDocumentViewModel(svc, conn);
        return (vm, svc);
    }

    [Fact]
    public async Task RefreshVolumes_填入磁碟清單()
    {
        var (vm, _) = Build();
        await vm.RefreshVolumesCommand.ExecuteAsync(null);
        vm.ServerVolumes.Should().HaveCount(2);
        vm.ServerVolumes[0].Name.Should().Be("C:\\");
    }

    [Fact]
    public async Task RefreshVolumes_查詢例外_設定訊息且不丟例外()
    {
        var (vm, svc) = Build();
        svc.GetServerVolumesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ServerVolumeInfo>>>(_ => throw new InvalidOperationException("boom"));

        await vm.RefreshVolumesCommand.ExecuteAsync(null);

        vm.ServerVolumes.Should().BeEmpty();
        vm.VolumesMessage.Should().Contain("無法取得磁碟資訊");
    }
}
