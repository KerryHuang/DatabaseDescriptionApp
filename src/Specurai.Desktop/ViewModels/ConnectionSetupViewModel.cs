using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

public partial class ConnectionSetupViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IExternalConnectionSource? _externalConnectionSource;
    private readonly IExternalSourceSettings? _externalSourceSettings;

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private bool _useWindowsAuth = true;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private DatabaseEnvironment _environment = DatabaseEnvironment.Staging;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private ConnectionProfile? _selectedExternalProfile;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncStatusMessage = string.Empty;

    [ObservableProperty]
    private string _externalSourceDirectory = string.Empty;

    [ObservableProperty]
    private string _externalKeyFilePath = string.Empty;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    public ObservableCollection<ConnectionProfile> ExternalProfiles { get; } = [];

    /// <summary>可選擇的資料庫環境清單（由 <see cref="DatabaseEnvironment"/> 列舉產生，供環境下拉選單綁定）。</summary>
    public static IReadOnlyList<DatabaseEnvironment> EnvironmentOptions { get; } =
        Enum.GetValues<DatabaseEnvironment>();

    public bool IsSyncButtonVisible =>
        !string.IsNullOrWhiteSpace(ExternalSourceDirectory);

    public bool IsExternalProfileSelected => SelectedExternalProfile != null;

    public bool CanConnect =>
        (SelectedProfile != null && SelectedProfile.IsEnabled) || SelectedExternalProfile != null;

    partial void OnSelectedExternalProfileChanged(ConnectionProfile? value)
    {
        OnPropertyChanged(nameof(IsExternalProfileSelected));
        OnPropertyChanged(nameof(CanConnect));
    }

    partial void OnExternalSourceDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsSyncButtonVisible));
    }

    public ConnectionSetupViewModel()
    {
        // Design-time constructor
    }

    public ConnectionSetupViewModel(IConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        LoadProfiles();
    }

    public ConnectionSetupViewModel(
        IConnectionManager connectionManager,
        IExternalConnectionSource externalConnectionSource,
        IExternalSourceSettings externalSourceSettings)
    {
        _connectionManager = connectionManager;
        _externalConnectionSource = externalConnectionSource;
        _externalSourceSettings = externalSourceSettings;
        LoadProfiles();
        LoadExternalSourceSettings();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
    }

    private void LoadExternalSourceSettings()
    {
        if (_externalSourceSettings == null) return;
        var config = _externalSourceSettings.Load();
        ExternalSourceDirectory = config.SourceDirectory;
        ExternalKeyFilePath = config.KeyFilePath;
    }

    public void SaveExternalSourceSettings()
    {
        _externalSourceSettings?.Save(
            new ExternalSourceConfig(ExternalSourceDirectory, ExternalKeyFilePath));
        OnPropertyChanged(nameof(IsSyncButtonVisible));
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        OnPropertyChanged(nameof(CanConnect));
        if (value != null)
        {
            Name = value.Name;
            Server = value.Server;
            Database = value.Database;
            UseWindowsAuth = value.AuthType == AuthenticationType.WindowsAuthentication;
            Username = value.Username ?? string.Empty;
            Password = value.Password ?? string.Empty;
            IsDefault = value.IsDefault;
            IsEnabled = value.IsEnabled;
            Environment = value.Environment;
            IsEditing = true;
        }
        else
        {
            ClearForm();
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ClearForm();
        IsEditing = false;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (_connectionManager == null) return;

        try
        {
            IsTesting = true;
            TestResult = "測試連線中...";

            var profile = CreateProfileFromForm();
            var success = await _connectionManager.TestConnectionAsync(profile);

            TestResult = success ? "連線成功!" : "連線失敗";
        }
        catch (Exception ex)
        {
            TestResult = $"錯誤: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_connectionManager == null) return;

        Guid profileId;

        if (IsEditing && SelectedProfile != null)
        {
            profileId = SelectedProfile.Id;
            var profile = CreateProfileFromForm(profileId);
            _connectionManager.UpdateProfile(profile);
        }
        else
        {
            profileId = Guid.NewGuid();
            var profile = CreateProfileFromForm(profileId);
            _connectionManager.AddProfile(profile);
        }

        // 重新載入並選擇剛儲存的設定檔
        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profileId);
        IsEditing = true;
    }

    [RelayCommand]
    private void Delete()
    {
        if (_connectionManager == null || SelectedProfile == null) return;

        _connectionManager.DeleteProfile(SelectedProfile.Id);
        LoadProfiles();
        ClearForm();
    }

    [RelayCommand]
    private void Connect()
    {
        if (_connectionManager == null) return;

        if (SelectedExternalProfile != null)
        {
            _connectionManager.RegisterTemporaryProfiles([SelectedExternalProfile]);
            _connectionManager.SetCurrentProfile(SelectedExternalProfile.Id);
            return;
        }

        if (SelectedProfile != null)
            _connectionManager.SetCurrentProfile(SelectedProfile.Id);
    }

    [RelayCommand]
    private async Task SyncExternalSourceAsync()
    {
        if (_externalConnectionSource == null) return;
        IsSyncing = true;
        SyncStatusMessage = "正在同步...";
        try
        {
            var result = await _externalConnectionSource.SyncAsync();

            // 與既有內部連線設定相同者不重複列出（含已停用的內部連線）
            var newProfiles = result.Profiles
                .Where(e => !Profiles.Any(e.HasSameConnectionSettings))
                .ToList();
            var duplicateCount = result.Profiles.Count - newProfiles.Count;

            SelectedExternalProfile = null;
            ExternalProfiles.Clear();
            foreach (var p in newProfiles)
                ExternalProfiles.Add(p);

            SyncStatusMessage = BuildSyncStatusMessage(
                newProfiles.Count, duplicateCount, result.FailedItems.Count);
        }
        catch (Exception ex)
        {
            SyncStatusMessage = $"同步失敗：{ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private static string BuildSyncStatusMessage(int newCount, int duplicateCount, int failedCount)
    {
        var message = $"已同步 {newCount} 個外部連線";
        if (duplicateCount > 0)
            message += $"（{duplicateCount} 個與現有連線重複已略過）";
        if (failedCount > 0)
            message += $"，{failedCount} 個失敗";
        return message;
    }

    private ConnectionProfile CreateProfileFromForm(Guid? id = null)
    {
        return new ConnectionProfile
        {
            Id = id ?? Guid.NewGuid(),
            Name = Name,
            Server = Server,
            Database = Database,
            AuthType = UseWindowsAuth
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = UseWindowsAuth ? null : Username,
            Password = UseWindowsAuth ? null : Password,
            IsDefault = IsDefault,
            Environment = Environment,
            IsEnabled = IsEnabled
        };
    }

    private void ClearForm()
    {
        Name = string.Empty;
        Server = string.Empty;
        Database = string.Empty;
        UseWindowsAuth = true;
        Username = string.Empty;
        Password = string.Empty;
        IsDefault = false;
        Environment = DatabaseEnvironment.Staging;
        IsEnabled = true;
        TestResult = string.Empty;
    }
}
