using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 連線設定匯出項目選擇
/// </summary>
public partial class ProfileSelectionItem : ObservableObject
{
    public ConnectionProfile Profile { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    public ProfileSelectionItem(ConnectionProfile profile)
    {
        Profile = profile;
    }
}

/// <summary>
/// 匯出連線設定 ViewModel
/// </summary>
public partial class ExportConnectionsViewModel : ViewModelBase
{
    private readonly IConnectionExportService? _exportService;

    [ObservableProperty]
    private bool _useEncryption;

    [ObservableProperty]
    private bool _includePasswords;

    [ObservableProperty]
    private string _encryptionPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    public ObservableCollection<ProfileSelectionItem> ProfileSelections { get; } = [];

    /// <summary>
    /// 根據加密選項決定的預設副檔名
    /// </summary>
    public string DefaultExtension => UseEncryption ? ".tsjson" : ".json";

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public ExportConnectionsViewModel()
    {
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public ExportConnectionsViewModel(IConnectionManager connectionManager, IConnectionExportService exportService)
    {
        _exportService = exportService;
        var profiles = connectionManager.GetAllProfiles();
        foreach (var profile in profiles)
        {
            ProfileSelections.Add(new ProfileSelectionItem(profile));
        }
    }

    partial void OnUseEncryptionChanged(bool value)
    {
        IncludePasswords = value;
        OnPropertyChanged(nameof(DefaultExtension));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in ProfileSelections)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in ProfileSelections)
            item.IsSelected = false;
    }

    /// <summary>
    /// 產生匯出資料
    /// </summary>
    public byte[] GetExportData()
    {
        var selectedProfiles = ProfileSelections
            .Where(p => p.IsSelected)
            .Select(p => p.Profile)
            .ToList();

        if (_exportService == null)
            return [];

        if (UseEncryption)
        {
            return _exportService.ExportToEncryptedJson(selectedProfiles, EncryptionPassword, IncludePasswords);
        }

        return _exportService.ExportToJson(selectedProfiles, IncludePasswords);
    }
}
