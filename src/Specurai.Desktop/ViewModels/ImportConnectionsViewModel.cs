using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 衝突處理方式
/// </summary>
public enum ConflictAction
{
    /// <summary>覆蓋現有連線</summary>
    Overwrite,
    /// <summary>跳過，保留現有</summary>
    Skip
}

/// <summary>
/// 匯入結果
/// </summary>
public class ImportResult
{
    public int Added { get; init; }
    public int Skipped { get; init; }
    public int Overwritten { get; init; }
}

/// <summary>
/// 匯入連線預覽項目
/// </summary>
public partial class ImportPreviewItem : ObservableObject
{
    public ConnectionProfile Profile { get; }
    public bool HasConflict { get; }
    public ConnectionProfile? ExistingProfile { get; }

    [ObservableProperty]
    private ConflictAction _conflictAction = ConflictAction.Skip;

    public ImportPreviewItem(ConnectionProfile profile, bool hasConflict, ConnectionProfile? existingProfile = null)
    {
        Profile = profile;
        HasConflict = hasConflict;
        ExistingProfile = existingProfile;
    }
}

/// <summary>
/// 匯入連線設定 ViewModel
/// </summary>
public partial class ImportConnectionsViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IConnectionExportService? _exportService;
    private byte[] _rawData = [];

    [ObservableProperty]
    private bool _needsPassword;

    [ObservableProperty]
    private string _decryptPassword = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<ImportPreviewItem> ImportPreviews { get; } = [];

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public ImportConnectionsViewModel()
    {
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public ImportConnectionsViewModel(IConnectionManager connectionManager, IConnectionExportService exportService)
    {
        _connectionManager = connectionManager;
        _exportService = exportService;
    }

    /// <summary>
    /// 載入匯入資料
    /// </summary>
    public void LoadImportData(byte[] data)
    {
        _rawData = data;

        if (_exportService == null) return;

        if (_exportService.IsEncryptedFormat(data))
        {
            NeedsPassword = true;
            return;
        }

        var importData = _exportService.ImportFromJson(data);
        PopulatePreviews(importData);
    }

    /// <summary>
    /// 解密並載入資料
    /// </summary>
    public void DecryptAndLoad()
    {
        if (_exportService == null) return;

        try
        {
            var importData = _exportService.ImportFromEncryptedJson(_rawData, DecryptPassword);
            NeedsPassword = false;
            ErrorMessage = string.Empty;
            PopulatePreviews(importData);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// 執行匯入
    /// </summary>
    public ImportResult ExecuteImport()
    {
        if (_connectionManager == null)
            return new ImportResult();

        var added = 0;
        var skipped = 0;
        var overwritten = 0;

        foreach (var preview in ImportPreviews)
        {
            if (preview.HasConflict)
            {
                switch (preview.ConflictAction)
                {
                    case ConflictAction.Skip:
                        skipped++;
                        continue;
                    case ConflictAction.Overwrite:
                        // 用現有 Profile 的 Id 更新
                        preview.Profile.Name = preview.Profile.Name;
                        var existing = preview.ExistingProfile!;
                        existing.Server = preview.Profile.Server;
                        existing.Database = preview.Profile.Database;
                        existing.AuthType = preview.Profile.AuthType;
                        existing.Username = preview.Profile.Username;
                        existing.Password = preview.Profile.Password;
                        existing.IsDefault = preview.Profile.IsDefault;
                        _connectionManager.UpdateProfile(existing);
                        overwritten++;
                        break;
                }
            }
            else
            {
                _connectionManager.AddProfile(preview.Profile);
                added++;
            }
        }

        return new ImportResult
        {
            Added = added,
            Skipped = skipped,
            Overwritten = overwritten
        };
    }

    [RelayCommand]
    private void OverwriteAll()
    {
        foreach (var preview in ImportPreviews.Where(p => p.HasConflict))
            preview.ConflictAction = ConflictAction.Overwrite;
    }

    [RelayCommand]
    private void SkipAll()
    {
        foreach (var preview in ImportPreviews.Where(p => p.HasConflict))
            preview.ConflictAction = ConflictAction.Skip;
    }

    private void PopulatePreviews(ConnectionExportData importData)
    {
        ImportPreviews.Clear();

        var existingProfiles = _connectionManager?.GetAllProfiles() ?? [];

        foreach (var profile in importData.Profiles)
        {
            var existing = existingProfiles.FirstOrDefault(p =>
                string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            var hasConflict = existing != null;

            ImportPreviews.Add(new ImportPreviewItem(profile, hasConflict, existing));
        }
    }
}
