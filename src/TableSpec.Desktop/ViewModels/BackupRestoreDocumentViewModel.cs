using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 備份還原文件 ViewModel（MDI Document）
/// </summary>
public partial class BackupRestoreDocumentViewModel : DocumentViewModel
{
    private readonly IBackupService? _backupService;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "BackupRestore";
    public override string DocumentKey => DocumentType; // 只允許開啟一個實例

    #region 連線選擇

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _currentDatabaseName = string.Empty;

    [ObservableProperty]
    private string _currentServerName = string.Empty;

    [ObservableProperty]
    private string _lastBackupInfo = "無備份記錄";

    #endregion

    #region 備份設定

    [ObservableProperty]
    private BackupType _selectedBackupType = BackupType.Full;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private string _backupDescription = string.Empty;

    [ObservableProperty]
    private bool _verifyAfterBackup = true;

    public ObservableCollection<BackupTypeItem> BackupTypes { get; } =
    [
        new BackupTypeItem(BackupType.Full, "完整備份"),
        new BackupTypeItem(BackupType.Differential, "差異備份"),
        new BackupTypeItem(BackupType.TransactionLog, "交易記錄備份")
    ];

    #endregion

    #region 還原設定

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private string _restoreFilePath = string.Empty;

    [ObservableProperty]
    private BackupFileInfo? _restoreFileInfo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private RestoreMode _selectedRestoreMode = RestoreMode.OverwriteExisting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private string _newDatabaseName = string.Empty;

    /// <summary>
    /// 是否為覆蓋模式（用於 RadioButton 綁定）
    /// </summary>
    public bool IsOverwriteMode
    {
        get => SelectedRestoreMode == RestoreMode.OverwriteExisting;
        set
        {
            if (value)
            {
                SelectedRestoreMode = RestoreMode.OverwriteExisting;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCreateNewMode));
            }
        }
    }

    /// <summary>
    /// 是否為建立新資料庫模式（用於 RadioButton 綁定）
    /// </summary>
    public bool IsCreateNewMode
    {
        get => SelectedRestoreMode == RestoreMode.CreateNew;
        set
        {
            if (value)
            {
                SelectedRestoreMode = RestoreMode.CreateNew;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOverwriteMode));
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private ConnectionProfile? _restoreTargetProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isBackupValid;

    [ObservableProperty]
    private string _backupValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _withRecovery = true;

    #endregion

    #region 歷史記錄

    public ObservableCollection<BackupInfo> BackupHistoryList { get; } = [];

    [ObservableProperty]
    private BackupInfo? _selectedHistoryItem;

    [ObservableProperty]
    private string _historyFilterConnection = "全部";

    public ObservableCollection<string> HistoryFilterConnections { get; } = ["全部"];

    #endregion

    #region 狀態

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    [ObservableProperty]
    private int _selectedTabIndex;

    #endregion

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public BackupRestoreDocumentViewModel()
    {
        Title = "備份與還原";
        Icon = "💾";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public BackupRestoreDocumentViewModel(
        IBackupService backupService,
        IConnectionManager connectionManager)
    {
        _backupService = backupService;
        _connectionManager = connectionManager;

        Title = "備份與還原";
        Icon = "💾";
        CanClose = true;

        LoadConnectionProfiles();
        LoadBackupHistory();
    }

    #region 初始化

    private void LoadConnectionProfiles()
    {
        ConnectionProfiles.Clear();
        HistoryFilterConnections.Clear();
        HistoryFilterConnections.Add("全部");

        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
            HistoryFilterConnections.Add(profile.Name);
        }

        // 選擇目前的連線
        var currentProfile = _connectionManager?.GetCurrentProfile();
        if (currentProfile != null)
        {
            SelectedProfile = ConnectionProfiles.FirstOrDefault(p => p.Id == currentProfile.Id);
            RestoreTargetProfile = SelectedProfile;
        }
    }

    private void LoadBackupHistory()
    {
        BackupHistoryList.Clear();
        var history = _backupService?.GetBackupHistory();
        if (history == null) return;

        var filtered = HistoryFilterConnection == "全部"
            ? history.Backups.OrderByDescending(b => b.BackupTime)
            : history.Backups
                .Where(b => b.ConnectionName == HistoryFilterConnection)
                .OrderByDescending(b => b.BackupTime);

        foreach (var backup in filtered)
        {
            BackupHistoryList.Add(backup);
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null)
        {
            CurrentDatabaseName = value.Database;
            CurrentServerName = value.Server;
            UpdateLastBackupInfo(value.Id);
            GenerateDefaultBackupPath();
        }
        else
        {
            CurrentDatabaseName = string.Empty;
            CurrentServerName = string.Empty;
            LastBackupInfo = "無備份記錄";
        }
    }

    partial void OnHistoryFilterConnectionChanged(string value)
    {
        LoadBackupHistory();
    }

    private void UpdateLastBackupInfo(Guid connectionId)
    {
        var history = _backupService?.GetBackupHistory();
        var lastBackup = history?.GetLatestBackup(connectionId);

        if (lastBackup != null)
        {
            var age = DateTime.Now - lastBackup.BackupTime;
            var ageText = age.TotalDays >= 1
                ? $"{(int)age.TotalDays} 天前"
                : age.TotalHours >= 1
                    ? $"{(int)age.TotalHours} 小時前"
                    : $"{(int)age.TotalMinutes} 分鐘前";

            LastBackupInfo = $"{lastBackup.BackupTime:yyyy-MM-dd HH:mm} ({ageText})";
        }
        else
        {
            LastBackupInfo = "無備份記錄";
        }
    }

    private async void GenerateDefaultBackupPath()
    {
        if (SelectedProfile == null || _connectionManager == null || _backupService == null) return;

        var fileName = $"{SelectedProfile.Database}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";

        // 嘗試取得 SQL Server 的預設備份目錄
        var connectionString = _connectionManager.GetConnectionString(SelectedProfile.Id);
        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                var defaultPath = await GetSqlServerDefaultBackupPathAsync(connectionString);
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    // 使用伺服器端的預設備份目錄
                    BackupPath = Path.Combine(defaultPath, fileName);
                    return;
                }
            }
            catch
            {
                // 忽略錯誤，使用預設路徑
            }
        }

        // 如果無法取得伺服器路徑，使用常見的預設路徑格式
        BackupPath = $@"C:\Backup\{fileName}";
    }

    /// <summary>
    /// 取得 SQL Server 的預設備份目錄
    /// </summary>
    private static async Task<string?> GetSqlServerDefaultBackupPathAsync(string connectionString)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        // 查詢 SQL Server 的預設備份目錄
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(
            @"SELECT SERVERPROPERTY('InstanceDefaultBackupPath') AS DefaultBackupPath", connection);

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    #endregion

    #region 備份命令

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupAsync()
    {
        if (_backupService == null || _connectionManager == null || SelectedProfile == null)
            return;

        try
        {
            IsProcessing = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ProgressPercentage = 0;
            ProgressMessage = "準備備份...";
            StatusMessage = "備份中...";

            var connectionString = _connectionManager.GetConnectionString(SelectedProfile.Id);
            if (string.IsNullOrEmpty(connectionString))
            {
                StatusMessage = "無法取得連線字串";
                return;
            }

            var progress = new Progress<BackupProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = p.PercentComplete;
                    ProgressMessage = p.Message;
                });
            });

            var backupInfo = await _backupService.BackupDatabaseAsync(
                connectionString,
                SelectedProfile.Id,
                SelectedProfile.Name,
                BackupPath,
                SelectedBackupType,
                BackupDescription,
                progress,
                _cancellationTokenSource.Token);

            // 驗證備份
            if (VerifyAfterBackup)
            {
                ProgressMessage = "驗證備份中...";
                var verifyResult = await _backupService.VerifyBackupAsync(
                    connectionString,
                    BackupPath,
                    _cancellationTokenSource.Token);

                if (!verifyResult.IsValid)
                {
                    StatusMessage = $"備份完成但驗證失敗：{verifyResult.ErrorMessage}";
                    return;
                }
            }

            StatusMessage = $"備份完成：{backupInfo.FormattedFileSize}";
            UpdateLastBackupInfo(SelectedProfile.Id);
            LoadBackupHistory();

            // 重新產生預設路徑
            GenerateDefaultBackupPath();
            BackupDescription = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "備份已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"備份失敗：{ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanBackup() =>
        SelectedProfile != null &&
        !string.IsNullOrWhiteSpace(BackupPath) &&
        !IsProcessing;

    [RelayCommand]
    private async Task BrowseBackupPathAsync()
    {
        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "選擇備份儲存位置",
            SuggestedFileName = Path.GetFileName(BackupPath),
            FileTypeChoices =
            [
                new FilePickerFileType("SQL Server 備份檔") { Patterns = ["*.bak"] },
                new FilePickerFileType("所有檔案") { Patterns = ["*.*"] }
            ]
        });

        if (file != null)
        {
            BackupPath = file.Path.LocalPath;
        }
    }

    #endregion

    #region 還原命令

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (_backupService == null || _connectionManager == null || RestoreTargetProfile == null)
            return;

        try
        {
            IsProcessing = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ProgressPercentage = 0;
            ProgressMessage = "準備還原...";
            StatusMessage = "還原中...";

            var connectionString = _connectionManager.GetConnectionString(RestoreTargetProfile.Id);
            if (string.IsNullOrEmpty(connectionString))
            {
                StatusMessage = "無法取得連線字串";
                return;
            }

            var options = new RestoreOptions
            {
                Mode = SelectedRestoreMode,
                TargetDatabaseName = SelectedRestoreMode == RestoreMode.CreateNew ? NewDatabaseName : null,
                WithReplace = SelectedRestoreMode == RestoreMode.OverwriteExisting,
                WithRecovery = WithRecovery
            };

            var progress = new Progress<RestoreProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = p.PercentComplete;
                    ProgressMessage = p.Message;
                });
            });

            await _backupService.RestoreDatabaseAsync(
                connectionString,
                RestoreFilePath,
                options,
                progress,
                _cancellationTokenSource.Token);

            StatusMessage = "還原完成";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "還原已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"還原失敗：{ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanRestore() =>
        RestoreTargetProfile != null &&
        !string.IsNullOrWhiteSpace(RestoreFilePath) &&
        IsBackupValid &&
        !IsProcessing &&
        (SelectedRestoreMode != RestoreMode.CreateNew || !string.IsNullOrWhiteSpace(NewDatabaseName));

    [RelayCommand]
    private async Task BrowseRestoreFileAsync()
    {
        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇備份檔案",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQL Server 備份檔") { Patterns = ["*.bak"] },
                new FilePickerFileType("所有檔案") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0)
        {
            RestoreFilePath = files[0].Path.LocalPath;
            await VerifyRestoreBackupAsync();
        }
    }

    [RelayCommand]
    private async Task VerifyRestoreBackupAsync()
    {
        if (_backupService == null || _connectionManager == null || string.IsNullOrWhiteSpace(RestoreFilePath))
        {
            IsBackupValid = false;
            BackupValidationMessage = string.Empty;
            RestoreFileInfo = null;
            return;
        }

        try
        {
            StatusMessage = "驗證備份檔案中...";

            // 使用任一連線來驗證備份
            var profile = RestoreTargetProfile ?? ConnectionProfiles.FirstOrDefault();
            if (profile == null)
            {
                IsBackupValid = false;
                BackupValidationMessage = "請先選擇目標連線";
                return;
            }

            var connectionString = _connectionManager.GetConnectionString(profile.Id);
            if (string.IsNullOrEmpty(connectionString))
            {
                IsBackupValid = false;
                BackupValidationMessage = "無法取得連線字串";
                return;
            }

            var result = await _backupService.VerifyBackupAsync(connectionString, RestoreFilePath);

            IsBackupValid = result.IsValid;
            RestoreFileInfo = result.FileInfo;
            BackupValidationMessage = result.IsValid
                ? "備份有效"
                : $"備份無效：{result.ErrorMessage}";

            if (result.FileInfo != null)
            {
                NewDatabaseName = $"{result.FileInfo.DatabaseName}_Restored";
            }

            StatusMessage = BackupValidationMessage;
        }
        catch (Exception ex)
        {
            IsBackupValid = false;
            BackupValidationMessage = $"驗證失敗：{ex.Message}";
            RestoreFileInfo = null;
            StatusMessage = BackupValidationMessage;
        }
    }

    #endregion

    #region 歷史記錄命令

    [RelayCommand]
    private void RefreshHistory()
    {
        LoadBackupHistory();
        StatusMessage = "已重新整理歷史記錄";
    }

    [RelayCommand]
    private async Task RestoreFromHistoryAsync()
    {
        if (SelectedHistoryItem == null) return;

        RestoreFilePath = SelectedHistoryItem.BackupFilePath;
        SelectedTabIndex = 1; // 切換到還原分頁
        await VerifyRestoreBackupAsync();
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        if (SelectedHistoryItem == null) return;

        var directory = Path.GetDirectoryName(SelectedHistoryItem.BackupFilePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        else
        {
            StatusMessage = "資料夾不存在";
        }
    }

    [RelayCommand]
    private void RemoveFromHistory()
    {
        if (SelectedHistoryItem == null || _backupService == null) return;

        _backupService.RemoveFromHistory(SelectedHistoryItem.Id);
        LoadBackupHistory();
        StatusMessage = "已從歷史記錄移除";
    }

    [RelayCommand]
    private async Task VerifyHistoryItemAsync()
    {
        if (SelectedHistoryItem == null || _backupService == null || _connectionManager == null)
            return;

        try
        {
            StatusMessage = "驗證備份檔案中...";

            var connectionString = _connectionManager.GetConnectionString(SelectedHistoryItem.ConnectionId);
            if (string.IsNullOrEmpty(connectionString))
            {
                // 使用任一可用連線
                var profile = ConnectionProfiles.FirstOrDefault();
                if (profile != null)
                {
                    connectionString = _connectionManager.GetConnectionString(profile.Id);
                }
            }

            if (string.IsNullOrEmpty(connectionString))
            {
                StatusMessage = "無可用連線來驗證備份";
                return;
            }

            var result = await _backupService.VerifyBackupAsync(
                connectionString,
                SelectedHistoryItem.BackupFilePath);

            StatusMessage = result.IsValid
                ? "備份驗證成功"
                : $"備份驗證失敗：{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"驗證失敗：{ex.Message}";
        }
    }

    #endregion

    #region 取消命令

    [RelayCommand]
    private void CancelOperation()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "正在取消...";
    }

    #endregion
}

/// <summary>
/// 備份類型項目（用於 UI 綁定）
/// </summary>
public record BackupTypeItem(BackupType Type, string DisplayName);
