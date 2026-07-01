using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 伺服器資料夾樹節點（惰性載入子項）
/// </summary>
public partial class ServerFolderNode : ObservableObject
{
    private readonly Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>>? _loadChildren;
    private bool _loaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    /// <summary>是否為「載入中…」佔位節點</summary>
    public bool IsPlaceholder { get; }

    public ObservableCollection<ServerFolderNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    // 佔位節點建構函式
    private ServerFolderNode(string name)
    {
        Name = name;
        FullPath = string.Empty;
        IsDirectory = false;
        IsPlaceholder = true;
    }

    public ServerFolderNode(
        ServerDirectoryEntry entry,
        Func<string, Task<IReadOnlyList<ServerDirectoryEntry>>> loadChildren)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        _loadChildren = loadChildren;

        // 資料夾預置佔位子節點，讓 TreeView 顯示展開箭頭
        if (IsDirectory)
            Children.Add(new ServerFolderNode("載入中…"));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && IsDirectory)
            _ = LoadChildrenAsync();
    }

    /// <summary>載入實際子項（首次展開時呼叫）</summary>
    public async Task LoadChildrenAsync()
    {
        if (_loaded || _loadChildren is null) return;
        _loaded = true;

        var children = await _loadChildren(FullPath);
        Children.Clear();
        foreach (var c in children)
            Children.Add(new ServerFolderNode(c, _loadChildren));
    }
}
