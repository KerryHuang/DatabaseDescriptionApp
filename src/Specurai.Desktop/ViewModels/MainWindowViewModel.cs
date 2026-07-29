using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Desktop.Services;
using Specurai.Desktop.Views;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionManager? _connectionManager;
    private readonly IExportService? _exportService;
    private readonly ITableQueryService? _tableQueryService;
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly ISqlDryRunRepository? _sqlDryRunRepository;
    private readonly IUpdateSqlGenerator? _updateSqlGenerator;
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
    private bool _isSidebarOpen = UserPreferences.IsSidebarOpen;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private string _themeIcon = "☀️";

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

    [ObservableProperty]
    private UpdateNotificationViewModel? _updateNotification;

    /// <summary>
    /// 更新對話框開啟請求事件（由 View 訂閱後實際顯示 Dialog）。
    /// </summary>
    public event Action<UpdateCheckResult?>? OpenUpdateDialogRequested;

    /// <summary>
    /// 要求 View 清除 TreeView 視覺選取狀態（例如關閉資料表結構分頁後）。
    /// </summary>
    public event Action? ClearTreeSelectionRequested;

    [ObservableProperty]
    private string _profileFilterText = string.Empty;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];

    public ObservableCollection<ConnectionProfile> FilteredConnectionProfiles { get; } = [];

    partial void OnProfileFilterTextChanged(string value)
    {
        RefreshFilteredProfiles();
    }

    /// <summary>
    /// MDI 文件集合
    /// </summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>
    /// 確認儲存的回調函數（由 View 設定）
    /// </summary>
    public Func<string, Task<bool>>? ConfirmSaveCallback { get; set; }

    /// <summary>
    /// 目前連線是否為正式環境（Production），供破壞性操作防呆使用。
    /// 本屬性不觸發 PropertyChanged，每次存取即時讀取目前連線；呼叫方應在需要時（例如開啟確認對話框時）主動讀取。
    /// </summary>
    public bool IsCurrentProfileProduction =>
        _connectionManager?.GetCurrentProfile()?.Environment == DatabaseEnvironment.Production;

    /// <summary>
    /// 目前連線的資料庫名稱（供 Production 警告橫幅顯示）。
    /// 本屬性不觸發 PropertyChanged，每次存取即時讀取目前連線；呼叫方應在需要時主動讀取。
    /// </summary>
    public string? CurrentEnvironmentDatabase =>
        _connectionManager?.GetCurrentDatabase();

    public MainWindowViewModel()
    {
        // Design-time constructor
        ShowAbout();
    }

    public MainWindowViewModel(
        IConnectionManager connectionManager,
        IExportService exportService,
        ITableQueryService tableQueryService,
        ISqlQueryRepository sqlQueryRepository,
        ISqlDryRunRepository sqlDryRunRepository,
        IUpdateSqlGenerator updateSqlGenerator,
        IColumnTypeRepository columnTypeRepository,
        ObjectTreeViewModel objectTree,
        UpdateNotificationViewModel updateNotification)
    {
        _connectionManager = connectionManager;
        _exportService = exportService;
        _tableQueryService = tableQueryService;
        _sqlQueryRepository = sqlQueryRepository;
        _sqlDryRunRepository = sqlDryRunRepository;
        _updateSqlGenerator = updateSqlGenerator;
        _columnTypeRepository = columnTypeRepository;
        ObjectTree = objectTree;
        UpdateNotification = updateNotification;

        // 訂閱連線變更事件
        _connectionManager.CurrentProfileChanged += OnCurrentProfileChanged;

        // 訂閱資料庫切換事件（側邊欄點選資料庫節點時觸發）
        _connectionManager.CurrentDatabaseChanged += OnCurrentDatabaseChanged;

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

        // 預設開啟「關於」分頁
        ShowAbout();

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
        var profiles = _connectionManager?.GetEnabledProfiles() ?? [];
        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
        }

        RefreshFilteredProfiles();

        // 預設選取：優先使用標記為預設（IsDefault）的連線，
        // 若無則退回使用上次使用的連線（GetCurrentProfile）
        var defaultProfile = ConnectionProfiles.FirstOrDefault(p => p.IsDefault)
                           ?? _connectionManager?.GetCurrentProfile();
        if (defaultProfile != null)
        {
            SelectedProfile = FilteredConnectionProfiles.FirstOrDefault(p => p.Id == defaultProfile.Id)
                           ?? ConnectionProfiles.FirstOrDefault(p => p.Id == defaultProfile.Id);
            IsConnected = true;
        }
        else
        {
            SelectedProfile = null;
            IsConnected = false;
        }
    }

    private void RefreshFilteredProfiles()
    {
        FilteredConnectionProfiles.Clear();
        var filter = ProfileFilterText?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(filter)
            ? ConnectionProfiles
            : ConnectionProfiles.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var profile in filtered)
            FilteredConnectionProfiles.Add(profile);
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
                var databaseName = _connectionManager?.GetCurrentDatabase();
                StatusMessage = databaseName != null
                    ? $"已載入 {databaseName}，共 {totalCount} 個物件"
                    : $"已載入 {totalCount} 個物件";
            }
        }
    }

    private async void OnCurrentProfileChanged(object? sender, ConnectionProfile? profile)
    {
        IsConnected = profile != null;
        await LoadObjectsAsync();
    }

    private async void OnCurrentDatabaseChanged(object? sender, string? databaseName)
    {
        await LoadObjectsAsync();
    }

    /// <summary>
    /// 當選擇資料表時，在 Documents 中開啟或切換到對應的 Tab
    /// </summary>
    private void OnTableSelected(TableInfo? table)
    {
        if (table == null || _tableQueryService == null) return;

        var databaseName = _connectionManager?.GetCurrentDatabase();
        var tableKey = databaseName != null
            ? $"TableDetail:{databaseName}.{table.Schema}.{table.Name}"
            : $"TableDetail:{table.Schema}.{table.Name}";

        // 檢查是否已開啟
        var existing = Documents.OfType<TableDetailDocumentViewModel>()
            .FirstOrDefault(d => d.DocumentKey == tableKey);

        if (existing != null)
        {
            SelectedDocument = existing;
        }
        else
        {
            var doc = new TableDetailDocumentViewModel(_tableQueryService, table, databaseName);
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
                var databaseName = _connectionManager?.GetCurrentDatabase() ?? "Specurai";
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
    private async Task ExportConnectionsAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var exportService = App.Services?.GetRequiredService<IConnectionExportService>();
            if (_connectionManager == null || exportService == null) return;

            var viewModel = new ExportConnectionsViewModel(_connectionManager, exportService);
            var window = new ExportConnectionsWindow(viewModel);
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    [RelayCommand]
    private async Task ImportConnectionsAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var exportService = App.Services?.GetRequiredService<IConnectionExportService>();
            if (_connectionManager == null || exportService == null) return;

            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "選擇連線設定檔",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("連線設定檔") { Patterns = ["*.json", "*.tsjson"] },
                    new FilePickerFileType("所有檔案") { Patterns = ["*"] }
                ]
            });

            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            var data = ms.ToArray();

            var viewModel = new ImportConnectionsViewModel(_connectionManager, exportService);
            viewModel.LoadImportData(data);

            var window = new ImportConnectionsWindow(viewModel);
            await window.ShowDialog(desktop.MainWindow!);

            // 重新載入連線清單
            LoadConnectionProfiles();
        }
    }

    [RelayCommand]
    private void OpenSqlQuery()
    {
        if (_sqlQueryRepository == null || _connectionManager == null) return;

        var doc = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository, _updateSqlGenerator);
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    /// <summary>
    /// 為指定資料表/檢視表開啟新的 SQL 查詢分頁，並自動執行 SELECT TOP 200。
    /// </summary>
    public void OpenSqlQueryForTable(TableInfo table)
    {
        if (_sqlQueryRepository == null || _connectionManager == null) return;
        if (table.Type != "BASE TABLE" && table.Type != "VIEW") return;

        var sql = $"SELECT TOP 200 * FROM [{table.Schema}].[{table.Name}]";
        var doc = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository, _updateSqlGenerator);
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;

        // 延後設定 SqlText，等待 View 載入完成後再寫入並執行，
        // 以避免 QueryHistory ComboBox 的 OneWayToSource 綁定在初始化時把 SqlText 推回 null。
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            doc.SqlText = sql;
            if (doc.ExecuteQueryCommand.CanExecute(null))
                doc.ExecuteQueryCommand.Execute(null);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    [RelayCommand]
    private void OpenColumnSearch()
    {
        var doc = App.Services?.GetRequiredService<ColumnSearchDocumentViewModel>()
            ?? new ColumnSearchDocumentViewModel();
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenMissingIndexReport()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<MissingIndexReportDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<MissingIndexReportDocumentViewModel>()
            ?? new MissingIndexReportDocumentViewModel();
        doc.ConfirmExecuteCallback = ConfirmSaveCallback;
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;

        // 開啟時即載入資料庫篩選選項（不等待使用者按「載入報表」）
        _ = doc.LoadDatabaseOptionsAsync();
    }

    [RelayCommand]
    private void OpenUnusedIndexReport()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<UnusedIndexReportDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<UnusedIndexReportDocumentViewModel>()
            ?? new UnusedIndexReportDocumentViewModel();
        doc.ConfirmExecuteCallback = ConfirmSaveCallback;
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;

        // 開啟時即載入資料庫篩選選項（不等待使用者按「載入報表」）
        _ = doc.LoadDatabaseOptionsAsync();
    }

    [RelayCommand]
    private void OpenUsageAnalysis()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<UsageAnalysisDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<UsageAnalysisDocumentViewModel>()
            ?? new UsageAnalysisDocumentViewModel();
        doc.ConfirmExecuteCallback = ConfirmSaveCallback;
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private void OpenMaintenancePlan()
    {
        // 檢查是否已開啟
        var existing = Documents.OfType<MaintenancePlanDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<MaintenancePlanDocumentViewModel>()
            ?? new MaintenancePlanDocumentViewModel();

        // 連結排程編輯與匯入 Job 回呼
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            doc.EditScheduleCallback = async (job) =>
            {
                var scheduleVm = new ScheduleEditViewModel(
                    App.Services?.GetRequiredService<IAgentJobService>()!,
                    job.JobId,
                    job.ScheduleTime ?? 0,
                    job.ScheduleFreqType ?? 4);
                var scheduleWindow = new ScheduleEditWindow(scheduleVm);
                await scheduleWindow.ShowDialog(desktop.MainWindow!);
            };

            doc.ImportJobCallback = async () =>
            {
                var importVm = new ImportJobWindowViewModel(
                    App.Services?.GetRequiredService<IAgentJobService>()!);
                var importWindow = new ImportJobWindow(importVm);
                await importWindow.ShowDialog(desktop.MainWindow!);
            };

            doc.SaveFileCallback = async (content) =>
            {
                if (desktop.MainWindow?.StorageProvider is not { } storageProvider)
                    return null;

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "儲存 SQL 腳本",
                    DefaultExtension = "sql",
                    SuggestedFileName = "MaintenancePlan.sql",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("SQL 腳本") { Patterns = ["*.sql"] }
                    }
                });

                if (file == null) return null;

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
                await writer.WriteAsync(content);
                return file.Name;
            };
        }

        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    [RelayCommand]
    private async Task OpenRecoveryModel()
    {
        var existing = Documents.OfType<RecoveryModelDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<RecoveryModelDocumentViewModel>()
            ?? new RecoveryModelDocumentViewModel();
        doc.ConfirmCallback = ConfirmSaveCallback;
        doc.CloseRequested += OnDocumentCloseRequested;
        Documents.Add(doc);
        SelectedDocument = doc;
        await doc.LoadCommand.ExecuteAsync(null);
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
    private void OpenSchemaMigration()
    {
        var existing = Documents.OfType<SchemaMigrationDocumentViewModel>().FirstOrDefault();
        if (existing != null)
        {
            SelectedDocument = existing;
            return;
        }

        var doc = App.Services?.GetRequiredService<SchemaMigrationDocumentViewModel>()
            ?? new SchemaMigrationDocumentViewModel();
        doc.ConfirmExecuteCallback = ConfirmSaveCallback;
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

        // 連結匯出 SQL 腳本回呼
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            doc.SaveFileCallback = async (content) =>
            {
                var topLevel = TopLevel.GetTopLevel(desktopLifetime.MainWindow);
                if (topLevel?.StorageProvider is not { } storageProvider) return null;

                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "匯出 SQL 腳本",
                    DefaultExtension = "sql",
                    SuggestedFileName = "HealthMonitoring.sql",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("SQL 腳本") { Patterns = ["*.sql"] }
                    }
                });

                if (file == null) return null;

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
                await writer.WriteAsync(content);
                return file.Name;
            };
        }

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

        // 若關閉的是 TableDetail，清除 ObjectTree 的選取狀態，
        // 讓使用者再次點選同一資料表時能重新觸發 SelectionChanged。
        if (doc is TableDetailDocumentViewModel && ObjectTree != null)
        {
            ObjectTree.SelectedTable = null;
            ClearTreeSelectionRequested?.Invoke();
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
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    partial void OnIsSidebarOpenChanged(bool value)
    {
        UserPreferences.IsSidebarOpen = value;
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

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (UpdateNotification is null) return;
        await UpdateNotification.CheckAsync();
        OpenUpdateDialogRequested?.Invoke(UpdateNotification.LatestResult);
    }
}
