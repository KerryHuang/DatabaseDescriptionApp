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
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IExportService? _exportService;
    private readonly ITableQueryService? _tableQueryService;
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly IColumnTypeRepository? _columnTypeRepository;

    [ObservableProperty]
    private ObjectTreeViewModel? _objectTree;

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

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];

    /// <summary>
    /// MDI 文件集合
    /// </summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>
    /// 確認儲存的回調函數（由 View 設定）
    /// </summary>
    public Func<string, Task<bool>>? ConfirmSaveCallback { get; set; }

    public MainWindowViewModel()
    {
        // Design-time constructor
    }

    public MainWindowViewModel(
        IConnectionManager connectionManager,
        IExportService exportService,
        ITableQueryService tableQueryService,
        ISqlQueryRepository sqlQueryRepository,
        IColumnTypeRepository columnTypeRepository,
        ObjectTreeViewModel objectTree)
    {
        _connectionManager = connectionManager;
        _exportService = exportService;
        _tableQueryService = tableQueryService;
        _sqlQueryRepository = sqlQueryRepository;
        _columnTypeRepository = columnTypeRepository;
        ObjectTree = objectTree;

        // 訂閱連線變更事件
        _connectionManager.CurrentProfileChanged += OnCurrentProfileChanged;

        // 訂閱選擇變更事件
        if (ObjectTree != null)
        {
            ObjectTree.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ObjectTreeViewModel.SelectedTable))
                {
                    OnTableSelected(ObjectTree.SelectedTable);
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

    /// <summary>
    /// 當選擇資料表時，在 Documents 中開啟或切換到對應的 Tab
    /// </summary>
    private void OnTableSelected(TableInfo? table)
    {
        if (table == null || _tableQueryService == null) return;

        var tableKey = $"TableDetail:{table.Schema}.{table.Name}";

        // 檢查是否已開啟
        var existing = Documents.OfType<TableDetailDocumentViewModel>()
            .FirstOrDefault(d => d.DocumentKey == tableKey);

        if (existing != null)
        {
            SelectedDocument = existing;
        }
        else
        {
            var doc = new TableDetailDocumentViewModel(_tableQueryService, table);
            doc.ConfirmSaveCallback = ConfirmSaveCallback;
            doc.CloseRequested += OnDocumentCloseRequested;
            Documents.Add(doc);
            SelectedDocument = doc;
        }
    }

    private void OnDocumentCloseRequested(object? sender, EventArgs e)
    {
        if (sender is DocumentViewModel doc)
        {
            CloseDocument(doc);
        }
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
    private void OpenSqlQuery()
    {
        if (_sqlQueryRepository == null || _connectionManager == null) return;

        var doc = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager);
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenColumnSearch()
    {
        if (_sqlQueryRepository == null || _columnTypeRepository == null || _connectionManager == null || _tableQueryService == null) return;

        var doc = new ColumnSearchDocumentViewModel(_sqlQueryRepository, _columnTypeRepository, _connectionManager, _tableQueryService);
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenBackupRestore()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<BackupRestoreDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<BackupRestoreDocumentViewModel>()
            ?? new BackupRestoreDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenSchemaCompare()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<SchemaCompareDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<SchemaCompareDocumentViewModel>()
            ?? new SchemaCompareDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenHealthMonitoring()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<HealthMonitoringDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<HealthMonitoringDocumentViewModel>()
            ?? new HealthMonitoringDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private async Task OpenPerformanceDiagnosticsAsync()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<PerformanceDiagnosticsDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<PerformanceDiagnosticsDocumentViewModel>()
            ?? new PerformanceDiagnosticsDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;

        // 初始化載入資料
        await doc.InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenColumnUsageAsync()
    {
        var doc = App.Services?.GetRequiredService<ColumnUsageDocumentViewModel>()
            ?? new ColumnUsageDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;

        // 初始化載入資料
        await doc.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task OpenTableStatisticsAsync()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<TableStatisticsDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<TableStatisticsDocumentViewModel>()
            ?? new TableStatisticsDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void CloseDocument(DocumentViewModel? doc)
    {
        if (doc == null || !doc.CanClose) return;

        doc.CloseRequested -= OnDocumentCloseRequested;
        Documents.Remove(doc);

        // 選擇下一個文件
        if (SelectedDocument == doc)
        {
            SelectedDocument = Documents.LastOrDefault();
        }
    }

    [RelayCommand]
    private void CloseCurrentDocument()
    {
        if (SelectedDocument != null && SelectedDocument.CanClose)
        {
            CloseDocument(SelectedDocument);
        }
    }

    [RelayCommand]
    private void CloseAllDocuments()
    {
        var closableDocuments = Documents.Where(d => d.CanClose).ToList();
        foreach (var doc in closableDocuments)
        {
            doc.CloseRequested -= OnDocumentCloseRequested;
            Documents.Remove(doc);
        }
        SelectedDocument = Documents.LastOrDefault();
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

    [RelayCommand]
    private void ShowAbout()
    {
        var existing = Documents.FirstOrDefault(d => d.DocumentKey == "About");
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var aboutVm = new AboutDocumentViewModel();
        Documents.Add(aboutVm);
        SelectedDocument = aboutVm;
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
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
