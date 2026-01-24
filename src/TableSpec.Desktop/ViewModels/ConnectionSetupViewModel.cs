using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

public partial class ConnectionSetupViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;

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
    private bool _isTesting;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ConnectionSetupViewModel()
    {
        // Design-time constructor
    }

    public ConnectionSetupViewModel(IConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        LoadProfiles();
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

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null)
        {
            Name = value.Name;
            Server = value.Server;
            Database = value.Database;
            UseWindowsAuth = value.AuthType == AuthenticationType.WindowsAuthentication;
            Username = value.Username ?? string.Empty;
            Password = value.Password ?? string.Empty;
            IsDefault = value.IsDefault;
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
        if (_connectionManager == null || SelectedProfile == null) return;

        _connectionManager.SetCurrentProfile(SelectedProfile.Id);
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
            IsDefault = IsDefault
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
        TestResult = string.Empty;
    }
}
