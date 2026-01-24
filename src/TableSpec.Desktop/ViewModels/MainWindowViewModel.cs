using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TableSpec.Application.Services;
using TableSpec.Desktop.Views;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IExportService? _exportService;

    [ObservableProperty]
    private ObjectTreeViewModel? _objectTree;

    [ObservableProperty]
    private TableDetailViewModel? _tableDetail;

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private string _themeIcon = "☀️";

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];

    public MainWindowViewModel()
    {
        // Design-time constructor
    }

    public MainWindowViewModel(
        IConnectionManager connectionManager,
        IExportService exportService,
        ObjectTreeViewModel objectTree,
        TableDetailViewModel tableDetail)
    {
        _connectionManager = connectionManager;
        _exportService = exportService;
        ObjectTree = objectTree;
        TableDetail = tableDetail;

        // 訂閱連線變更事件
        _connectionManager.CurrentProfileChanged += OnCurrentProfileChanged;

        // 訂閱選擇變更事件
        if (ObjectTree != null)
        {
            ObjectTree.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ObjectTreeViewModel.SelectedTable) && TableDetail != null)
                {
                    TableDetail.LoadTableAsync(ObjectTree.SelectedTable).ConfigureAwait(false);
                }
            };
        }

        // 初始化主題
        InitializeTheme();

        // 初始化連線狀態並自動連線
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        LoadConnectionProfiles();

        // 如果有預設連線或已儲存的連線，自動連線
        if (SelectedProfile != null)
        {
            await LoadObjectsAsync();
        }
    }

    private void LoadConnectionProfiles()
    {
        ConnectionProfiles.Clear();
        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
        }

        // 選擇目前的連線
        var currentProfile = _connectionManager?.GetCurrentProfile();
        if (currentProfile != null)
        {
            SelectedProfile = ConnectionProfiles.FirstOrDefault(p => p.Id == currentProfile.Id);
            IsConnected = true;
        }
        else
        {
            SelectedProfile = null;
            IsConnected = false;
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null && _connectionManager != null)
        {
            _connectionManager.SetCurrentProfile(value.Id);
        }
    }

    private async Task LoadObjectsAsync()
    {
        if (ObjectTree != null)
        {
            StatusMessage = "正在載入物件清單...";
            await ObjectTree.RefreshCommand.ExecuteAsync(null);
            if (!string.IsNullOrEmpty(ObjectTree.LastError))
            {
                StatusMessage = $"錯誤: {ObjectTree.LastError}";
            }
            else
            {
                var totalCount = ObjectTree.Groups.Sum(g => g.Count);
                StatusMessage = $"已載入 {totalCount} 個物件";
            }
        }
    }

    private async void OnCurrentProfileChanged(object? sender, ConnectionProfile? profile)
    {
        IsConnected = profile != null;
        await LoadObjectsAsync();
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        if (_exportService == null || !IsConnected) return;

        try
        {
            IsExporting = true;
            StatusMessage = "正在匯出...";

            var bytes = await _exportService.ExportToExcelAsync();

            // 使用 StorageProvider API 儲存檔案
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow?.StorageProvider is { } storageProvider)
            {
                var databaseName = SelectedProfile?.Database ?? "TableSpec";
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "儲存 Excel 檔案",
                    DefaultExtension = "xlsx",
                    SuggestedFileName = $"{databaseName}_{DateTime.Now:yyyyMMdd}.xlsx",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("Excel 檔案") { Patterns = ["*.xlsx"] }
                    }
                });

                if (file != null)
                {
                    await using var stream = await file.OpenWriteAsync();
                    await stream.WriteAsync(bytes);
                    StatusMessage = $"已匯出至 {file.Name}";
                }
                else
                {
                    StatusMessage = "已取消匯出";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯出失敗: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task OpenConnectionSettingsAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = App.Services?.GetRequiredService<ConnectionSetupViewModel>()
                ?? new ConnectionSetupViewModel();
            var window = new ConnectionSetupWindow(viewModel);
            await window.ShowDialog(desktop.MainWindow!);

            // 重新載入連線清單
            LoadConnectionProfiles();
        }
    }

    [RelayCommand]
    private async Task OpenSqlQueryAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = App.Services?.GetRequiredService<SqlQueryViewModel>()
                ?? new SqlQueryViewModel();
            var window = new SqlQueryWindow(viewModel);
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Avalonia.Application.Current is { } app)
        {
            if (app.ActualThemeVariant == ThemeVariant.Dark)
            {
                app.RequestedThemeVariant = ThemeVariant.Light;
                IsDarkMode = false;
                ThemeIcon = "🌙";
            }
            else
            {
                app.RequestedThemeVariant = ThemeVariant.Dark;
                IsDarkMode = true;
                ThemeIcon = "☀️";
            }
        }
    }

    private void InitializeTheme()
    {
        if (Avalonia.Application.Current is { } app)
        {
            IsDarkMode = app.ActualThemeVariant == ThemeVariant.Dark;
            ThemeIcon = IsDarkMode ? "☀️" : "🌙";
        }
    }
}
