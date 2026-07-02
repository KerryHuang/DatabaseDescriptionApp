using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class ServerFolderBrowserViewModelTests
{
    private static IBackupService BuildService()
    {
        var svc = Substitute.For<IBackupService>();
        svc.ListServerDirectoryAsync("cs", "", Arg.Any<CancellationToken>())
            .Returns(new List<ServerDirectoryEntry>
            {
                new() { Name = "C:\\", FullPath = "C:\\", IsDirectory = true },
                new() { Name = "D:\\", FullPath = "D:\\", IsDirectory = true }
            });
        svc.ListServerDirectoryAsync("cs", "D:\\", Arg.Any<CancellationToken>())
            .Returns(new List<ServerDirectoryEntry>
            {
                new() { Name = "SQLBackup", FullPath = "D:\\SQLBackup", IsDirectory = true },
                new() { Name = "old.bak", FullPath = "D:\\old.bak", IsDirectory = false }
            });
        return svc;
    }

    [Fact]
    public async Task LoadRootAsync_填入磁碟根節點()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        await vm.LoadRootAsync();
        vm.RootNodes.Should().HaveCount(2);
        vm.RootNodes[0].FullPath.Should().Be("C:\\");
    }

    [Fact]
    public async Task LoadChildrenAsync_展開節點載入子項()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        await vm.LoadRootAsync();
        var dNode = vm.RootNodes[1]; // D:\
        await dNode.LoadChildrenAsync();
        dNode.Children.Should().HaveCount(2);
        dNode.Children[0].Name.Should().Be("SQLBackup");
    }

    [Fact]
    public void Confirm_組合資料夾與檔名並要求關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak")
        {
            SelectedPath = "D:\\SQLBackup"
        };
        bool? closedWith = null;
        vm.RequestClose += ok => closedWith = ok;

        vm.ConfirmCommand.Execute(null);

        vm.ResultPath.Should().Be("D:\\SQLBackup\\my.bak");
        closedWith.Should().BeTrue();
    }

    [Fact]
    public void Confirm_未選資料夾_顯示錯誤不關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        bool closed = false;
        vm.RequestClose += _ => closed = true;

        vm.ConfirmCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void SelectFileNode_帶入所在資料夾與檔名()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        var loader = new System.Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>>(
            _ => Task.FromResult<IReadOnlyList<ServerDirectoryEntry>>(new List<ServerDirectoryEntry>()));
        var fileNode = new ServerFolderNode(
            new ServerDirectoryEntry { Name = "old.bak", FullPath = "D:\\SQLBackup\\old.bak", IsDirectory = false },
            loader);

        vm.SelectedNode = fileNode;

        vm.SelectedPath.Should().Be("D:\\SQLBackup");
        vm.FileName.Should().Be("old.bak");
    }

    [Fact]
    public void SelectFileNode_磁碟根目錄檔案_保留分隔字元()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        var loader = new System.Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>>(
            _ => Task.FromResult<IReadOnlyList<ServerDirectoryEntry>>(new List<ServerDirectoryEntry>()));
        var fileNode = new ServerFolderNode(
            new ServerDirectoryEntry { Name = "old.bak", FullPath = "D:\\old.bak", IsDirectory = false },
            loader);

        vm.SelectedNode = fileNode;

        vm.SelectedPath.Should().Be("D:\\");
        vm.FileName.Should().Be("old.bak");
    }

    [Fact]
    public void FolderOnly_ShowFileName為false()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        vm.ShowFileName.Should().BeFalse();
        vm.Title.Should().Be("選擇伺服器資料夾");
    }

    [Fact]
    public void FileMode_ShowFileName為true()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", "my.bak");
        vm.ShowFileName.Should().BeTrue();
        vm.Title.Should().Be("尋找備份資料夾");
    }

    [Fact]
    public async Task FolderOnly_展開節點只保留資料夾()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        await vm.LoadRootAsync();
        var dNode = vm.RootNodes[1]; // D:\
        await dNode.LoadChildrenAsync();
        dNode.Children.Should().OnlyContain(c => c.IsDirectory);
        dNode.Children.Should().ContainSingle(c => c.Name == "SQLBackup");
    }

    [Fact]
    public void FolderOnly_Confirm回傳帶結尾分隔字元的資料夾()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true)
        {
            SelectedPath = "D:\\SQLBackup"
        };
        bool? closed = null;
        vm.RequestClose += ok => closed = ok;

        vm.ConfirmCommand.Execute(null);

        vm.ResultPath.Should().Be("D:\\SQLBackup\\");
        closed.Should().BeTrue();
    }

    [Fact]
    public void FolderOnly_未選資料夾_錯誤不關閉()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true);
        bool closed = false;
        vm.RequestClose += _ => closed = true;

        vm.ConfirmCommand.Execute(null);

        vm.ErrorMessage.Should().NotBeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void FolderOnly_initialFolder預帶SelectedPath且去尾分隔字元()
    {
        var vm = new ServerFolderBrowserViewModel(BuildService(), "cs", folderOnly: true, initialFolder: "D:\\SQLBackup\\");
        vm.SelectedPath.Should().Be("D:\\SQLBackup");
    }
}
