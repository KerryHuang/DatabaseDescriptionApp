using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Domain;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 伺服器端資料夾瀏覽對話框 ViewModel
/// </summary>
public partial class ServerFolderBrowserViewModel : ObservableObject
{
    private readonly IBackupService? _backupService;
    private readonly string _connectionString;

    public ObservableCollection<ServerFolderNode> RootNodes { get; } = [];

    [ObservableProperty]
    private ServerFolderNode? _selectedNode;

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>確定後的完整路徑（資料夾 + 檔名）</summary>
    public string? ResultPath { get; private set; }

    /// <summary>要求關閉視窗：true = 確定、false = 取消</summary>
    public event Action<bool>? RequestClose;

    /// <summary>設計時建構函式</summary>
    public ServerFolderBrowserViewModel()
    {
        _connectionString = string.Empty;
    }

    /// <summary>執行時建構函式</summary>
    public ServerFolderBrowserViewModel(IBackupService backupService, string connectionString, string initialFileName)
    {
        _backupService = backupService;
        _connectionString = connectionString;
        _fileName = initialFileName;
    }

    /// <summary>載入根節點（各磁碟）</summary>
    public async Task LoadRootAsync()
    {
        if (_backupService is null) return;
        try
        {
            var roots = await _backupService.ListServerDirectoryAsync(_connectionString, string.Empty);
            RootNodes.Clear();
            foreach (var r in roots)
                RootNodes.Add(new ServerFolderNode(r, LoadChildrenAsync));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽伺服器目錄：{ex.Message}";
        }
    }

    private async Task<IReadOnlyList<ServerDirectoryEntry>> LoadChildrenAsync(string path)
    {
        if (_backupService is null) return [];
        try
        {
            return await _backupService.ListServerDirectoryAsync(_connectionString, path);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"無法瀏覽「{path}」：{ex.Message}";
            return [];
        }
    }

    partial void OnSelectedNodeChanged(ServerFolderNode? value)
    {
        if (value is null || value.IsPlaceholder) return;

        if (value.IsDirectory)
        {
            SelectedPath = value.FullPath;
        }
        else
        {
            // 選到現有備份檔：帶入其所在資料夾與檔名
            SelectedPath = ParentOf(value.FullPath);
            FileName = value.Name;
        }
    }

    private static string ParentOf(string fullPath)
    {
        var sep = ServerPathHelper.GetSeparator(fullPath);
        var trimmed = fullPath.TrimEnd(sep);
        var idx = trimmed.LastIndexOf(sep);
        if (idx < 0) return fullPath;

        var parent = trimmed[..idx];
        // 磁碟根目錄（例如 "D:"）補回分隔字元 → "D:\"
        if (parent.Length == 2 && char.IsLetter(parent[0]) && parent[1] == ':')
            return parent + sep;
        // Unix 根目錄
        if (parent.Length == 0) return sep.ToString();
        return parent;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath) || string.IsNullOrWhiteSpace(FileName))
        {
            ErrorMessage = "請選擇資料夾並輸入檔案名稱";
            return;
        }
        ResultPath = ServerPathHelper.Combine(SelectedPath, FileName);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
