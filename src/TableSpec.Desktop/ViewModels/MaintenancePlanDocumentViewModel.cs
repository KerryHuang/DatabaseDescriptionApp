using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Models;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 維護計劃文件 ViewModel（MDI Document），整合管理面板與精靈
/// </summary>
public partial class MaintenancePlanDocumentViewModel : DocumentViewModel
{
    private readonly IAgentJobService? _jobService;
    private readonly IMaintenancePlanService? _planService;
    private readonly IMaintenancePlanSqlGenerator? _sqlGenerator;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _executionCts;

    public override string DocumentType => "MaintenancePlan";
    public override string DocumentKey => DocumentType;

    #region 模式切換

    /// <summary>是否為精靈模式（false = 管理模式）</summary>
    [ObservableProperty]
    private bool _isWizardMode;

    #endregion

    #region 管理面板

    [ObservableProperty]
    private AgentJobInfo? _selectedJob;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Agent Job 清單</summary>
    public ObservableCollection<AgentJobInfo> Jobs { get; } = [];

    /// <summary>執行歷史</summary>
    public ObservableCollection<AgentJobHistory> JobHistory { get; } = [];

    /// <summary>選取 Job 時自動載入執行歷史</summary>
    async partial void OnSelectedJobChanged(AgentJobInfo? value)
    {
        JobHistory.Clear();
        if (value == null || _jobService == null) return;

        try
        {
            var history = await _jobService.GetJobHistoryAsync(value.JobId);
            foreach (var h in history)
                JobHistory.Add(h);
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入執行歷史失敗：{ex.Message}";
        }
    }

    /// <summary>刪除確認回呼</summary>
    public Func<Task<bool>>? ConfirmDeleteCallback { get; set; }

    /// <summary>編輯排程回呼</summary>
    public Func<AgentJobInfo, Task>? EditScheduleCallback { get; set; }

    #endregion

    #region 精靈 - 步驟1 基本設定

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _restorePath = string.Empty;

    /// <summary>平台選項</summary>
    [ObservableProperty]
    private string _selectedPlatform = "Windows";

    /// <summary>路徑是否為自訂</summary>
    public bool IsPathCustom => SelectedPlatform == "其他";

    /// <summary>平台選項清單</summary>
    public IReadOnlyList<string> PlatformOptions { get; } = ["Windows", "Linux", "其他"];

    partial void OnSelectedPlatformChanged(string value)
    {
        switch (value)
        {
            case "Windows":
                BackupPath = @"D:\SQLBackup\";
                RestorePath = @"D:\sql_data\";
                break;
            case "Linux":
                BackupPath = "/var/opt/mssql/backup/";
                RestorePath = "/var/opt/mssql/data/";
                break;
        }
        OnPropertyChanged(nameof(IsPathCustom));
    }

    [ObservableProperty]
    private string _testDatabaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _loginName = "mis";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private TimeSpan _backupTime = new(2, 0, 0);

    [ObservableProperty]
    private TimeSpan _restoreTime = new(3, 0, 0);

    /// <summary>備份保留天數</summary>
    [ObservableProperty]
    private int _retentionDays = 7;

    #endregion

    #region 精靈 - 步驟2 選擇步驟

    [ObservableProperty]
    private bool _isSetRecoveryModelSelected = true;

    [ObservableProperty]
    private bool _isRenameLogicalFilesSelected = true;

    [ObservableProperty]
    private bool _isCreateLoginAndUserSelected = true;

    [ObservableProperty]
    private bool _isAddToDbOwnerSelected = true;

    [ObservableProperty]
    private bool _isCreateBackupJobSelected = true;

    [ObservableProperty]
    private bool _isCreateRestoreJobSelected;

    /// <summary>步驟2的檢查狀態文字</summary>
    [ObservableProperty]
    private string _step2CheckStatus = string.Empty;

    [ObservableProperty]
    private string _recoveryModelStatus = string.Empty;

    [ObservableProperty]
    private string _logicalFilesStatus = string.Empty;

    [ObservableProperty]
    private string _loginAndUserStatus = string.Empty;

    [ObservableProperty]
    private string _dbOwnerStatus = string.Empty;

    [ObservableProperty]
    private string _backupJobStatus = string.Empty;

    [ObservableProperty]
    private string _restoreJobStatus = string.Empty;

    /// <summary>是否正在檢查步驟狀態</summary>
    [ObservableProperty]
    private bool _isCheckingSteps;

    #endregion

    #region 精靈 - 步驟3 確認與執行

    [ObservableProperty]
    private string _previewSql = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    #endregion

    #region 精靈 - 步驟狀態

    [ObservableProperty]
    private int _currentStep = 1;

    public bool IsStep1Visible => CurrentStep == 1;
    public bool IsStep2Visible => CurrentStep == 2;
    public bool IsStep3Visible => CurrentStep == 3;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        NextStepCommand.NotifyCanExecuteChanged();
        PreviousStepCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region 精靈集合

    /// <summary>步驟檢查結果</summary>
    public ObservableCollection<StepCheckResult> CheckResults { get; } = [];

    /// <summary>執行日誌</summary>
    public ObservableCollection<string> ExecutionLog { get; } = [];

    #endregion

    #region 建構函式

    /// <summary>設計時建構函式</summary>
    public MaintenancePlanDocumentViewModel()
    {
        Title = "維護計劃";
        Icon = "🔧";
        CanClose = true;
        BackupPath = @"D:\SQLBackup\";
        RestorePath = @"D:\sql_data\";
    }

    /// <summary>DI 建構函式</summary>
    public MaintenancePlanDocumentViewModel(
        IAgentJobService jobService,
        IMaintenancePlanService planService,
        IMaintenancePlanSqlGenerator sqlGenerator,
        IConnectionManager connectionManager)
    {
        _jobService = jobService;
        _planService = planService;
        _sqlGenerator = sqlGenerator;
        _connectionManager = connectionManager;

        Title = "維護計劃";
        Icon = "🔧";
        CanClose = true;

        // 預設路徑
        BackupPath = @"D:\SQLBackup\";
        RestorePath = @"D:\sql_data\";

        // 從目前連線取得資料庫名稱，若為測試庫則自動取主庫名稱
        var currentProfile = connectionManager.GetCurrentProfile();
        if (currentProfile != null)
        {
            var dbName = currentProfile.Database;
            // 移除常見的測試庫後綴（-Test、-test、_Test、_test）
            foreach (var suffix in new[] { "-Test", "-test", "_Test", "_test" })
            {
                if (dbName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    dbName = dbName[..^suffix.Length];
                    break;
                }
            }
            DatabaseName = dbName;
        }

        // 進入頁面時自動載入 Job 清單
        _ = LoadJobsAsync();
    }

    #endregion

    #region 自動帶入

    partial void OnDatabaseNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            TestDatabaseName = $"{value}-test";
        else
            TestDatabaseName = string.Empty;
    }

    #endregion

    #region 管理面板命令

    /// <summary>載入 Job 清單</summary>
    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        if (_jobService is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在載入 Job 清單...";

            var jobs = await _jobService.GetJobsAsync();
            Jobs.Clear();
            foreach (var job in jobs)
                Jobs.Add(job);

            StatusMessage = $"已載入 {Jobs.Count} 個 Job";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>切換 Job 啟用/停用</summary>
    [RelayCommand]
    private async Task ToggleJobAsync()
    {
        if (_jobService is null || SelectedJob is null) return;

        try
        {
            var newEnabled = !SelectedJob.IsEnabled;
            await _jobService.SetJobEnabledAsync(SelectedJob.JobId, newEnabled);
            await LoadJobsAsync();
            StatusMessage = $"已{(newEnabled ? "啟用" : "停用")} Job：{SelectedJob.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切換失敗：{ex.Message}";
        }
    }

    /// <summary>立即執行 Job</summary>
    [RelayCommand]
    private async Task StartJobAsync()
    {
        if (_jobService is null || SelectedJob is null) return;

        try
        {
            await _jobService.StartJobAsync(SelectedJob.JobId);
            StatusMessage = $"已啟動 Job：{SelectedJob.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"啟動失敗：{ex.Message}";
        }
    }

    /// <summary>刪除 Job</summary>
    [RelayCommand]
    private async Task DeleteJobAsync()
    {
        if (_jobService is null || SelectedJob is null) return;

        if (ConfirmDeleteCallback is not null)
        {
            var confirmed = await ConfirmDeleteCallback();
            if (!confirmed) return;
        }

        try
        {
            await _jobService.DeleteJobAsync(SelectedJob.JobId);
            await LoadJobsAsync();
            StatusMessage = "已刪除 Job";
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗：{ex.Message}";
        }
    }

    /// <summary>編輯排程</summary>
    [RelayCommand]
    private async Task EditScheduleAsync()
    {
        if (SelectedJob is null || EditScheduleCallback is null) return;

        await EditScheduleCallback(SelectedJob);
        await LoadJobsAsync();
    }

    /// <summary>開啟精靈模式</summary>
    [RelayCommand]
    private void OpenWizard()
    {
        CurrentStep = 1;
        IsWizardMode = true;
    }

    #endregion

    #region 精靈命令

    [RelayCommand(CanExecute = nameof(CanNextStep))]
    private async Task NextStep()
    {
        if (CurrentStep == 1 && _planService is not null)
        {
            // 從步驟1到步驟2時執行前置檢查，檢查所有步驟狀態
            CurrentStep++;
            await RunStep2ChecksAsync();
            return;
        }

        if (CurrentStep == 2 && _planService is not null)
        {
            // 從步驟2到步驟3時，根據勾選的步驟建立檢查結果
            var config = BuildConfig();
            var results = await _planService.CheckStepsAsync(config);
            CheckResults.Clear();
            foreach (var r in results)
            {
                // 根據步驟2的勾選預設 SelectedAction
                r.SelectedAction = r.AlreadyExists ? "跳過" : "執行";
                CheckResults.Add(r);
            }
        }

        if (CurrentStep < 3)
            CurrentStep++;
    }

    private bool CanNextStep()
    {
        return CurrentStep switch
        {
            1 => !string.IsNullOrEmpty(DatabaseName)
                 && !string.IsNullOrEmpty(BackupPath)
                 && !string.IsNullOrEmpty(RestorePath)
                 && !string.IsNullOrEmpty(LoginName)
                 && !string.IsNullOrEmpty(LoginPassword),
            2 => true,
            _ => false
        };
    }

    [RelayCommand(CanExecute = nameof(CanPreviousStep))]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
            CurrentStep--;
    }

    private bool CanPreviousStep() => CurrentStep > 1;

    /// <summary>進入步驟2時執行所有步驟的前置檢查</summary>
    private async Task RunStep2ChecksAsync()
    {
        if (_planService is null) return;

        IsCheckingSteps = true;
        Step2CheckStatus = "正在檢查...";

        try
        {
            // 建立包含所有步驟的 config 來檢查
            var allStepsConfig = new MaintenancePlanConfig
            {
                DatabaseName = DatabaseName,
                BackupPath = BackupPath,
                RestorePath = RestorePath,
                TestDatabaseName = TestDatabaseName,
                LoginName = LoginName,
                LoginPassword = LoginPassword,
                BackupTime = (int)(BackupTime.Hours * 10000 + BackupTime.Minutes * 100),
                RestoreTime = (int)(RestoreTime.Hours * 10000 + RestoreTime.Minutes * 100),
                SelectedSteps = Enum.GetValues<MaintenancePlanStep>().ToList()
            };

            var results = await _planService.CheckStepsAsync(allStepsConfig);

            foreach (var r in results)
            {
                switch (r.Step)
                {
                    case MaintenancePlanStep.SetRecoveryModel:
                        RecoveryModelStatus = r.CurrentStatus;
                        IsSetRecoveryModelSelected = !r.AlreadyExists;
                        break;
                    case MaintenancePlanStep.RenameLogicalFiles:
                        LogicalFilesStatus = r.CurrentStatus;
                        IsRenameLogicalFilesSelected = !r.AlreadyExists;
                        break;
                    case MaintenancePlanStep.CreateLoginAndUser:
                        LoginAndUserStatus = r.CurrentStatus;
                        IsCreateLoginAndUserSelected = !r.AlreadyExists;
                        break;
                    case MaintenancePlanStep.AddToDbOwner:
                        DbOwnerStatus = r.CurrentStatus;
                        IsAddToDbOwnerSelected = !r.AlreadyExists;
                        break;
                    case MaintenancePlanStep.CreateBackupJob:
                        BackupJobStatus = r.CurrentStatus;
                        IsCreateBackupJobSelected = !r.AlreadyExists;
                        break;
                    case MaintenancePlanStep.CreateRestoreJob:
                        RestoreJobStatus = r.CurrentStatus;
                        // 還原排程預設不勾選，除非已存在需要重建
                        IsCreateRestoreJobSelected = false;
                        break;
                }
            }

            Step2CheckStatus = "檢查完成";
        }
        catch (Exception ex)
        {
            Step2CheckStatus = $"檢查失敗：{ex.Message}";
        }
        finally
        {
            IsCheckingSteps = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (_planService is null) return;

        IsExecuting = true;
        ExecutionLog.Clear();
        _executionCts = new CancellationTokenSource();

        try
        {
            var config = BuildConfig();
            var progress = new Progress<string>(msg => ExecutionLog.Add(msg));
            await _planService.ExecutePlanAsync(config, CheckResults.ToList(), progress, _executionCts.Token);
            StatusMessage = "執行完成";

            // 執行完成後切回管理模式並重新載入
            IsWizardMode = false;
            await LoadJobsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消執行";
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            _executionCts?.Dispose();
            _executionCts = null;
        }
    }

    [RelayCommand]
    private void CancelExecution()
    {
        _executionCts?.Cancel();
    }

    [RelayCommand]
    private async Task GeneratePreview()
    {
        if (_planService is null) return;

        var config = BuildConfig();
        PreviewSql = await _planService.GeneratePreviewSqlAsync(config, CheckResults.ToList());
    }

    /// <summary>取消精靈，切回管理模式</summary>
    [RelayCommand]
    private void CancelWizard()
    {
        IsWizardMode = false;
    }

    #endregion

    #region 輔助方法

    /// <summary>從表單欄位建立設定物件</summary>
    private MaintenancePlanConfig BuildConfig()
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = DatabaseName,
            BackupPath = BackupPath,
            RestorePath = RestorePath,
            TestDatabaseName = TestDatabaseName,
            LoginName = LoginName,
            LoginPassword = LoginPassword,
            BackupTime = (int)BackupTime.TotalHours,
            RestoreTime = (int)RestoreTime.TotalHours,
            RetentionDays = RetentionDays,
            SelectedSteps = GetSelectedSteps()
        };
    }

    /// <summary>取得已勾選的步驟</summary>
    private List<MaintenancePlanStep> GetSelectedSteps()
    {
        var steps = new List<MaintenancePlanStep>();
        if (IsSetRecoveryModelSelected) steps.Add(MaintenancePlanStep.SetRecoveryModel);
        if (IsRenameLogicalFilesSelected) steps.Add(MaintenancePlanStep.RenameLogicalFiles);
        if (IsCreateLoginAndUserSelected) steps.Add(MaintenancePlanStep.CreateLoginAndUser);
        if (IsAddToDbOwnerSelected) steps.Add(MaintenancePlanStep.AddToDbOwner);
        if (IsCreateBackupJobSelected) steps.Add(MaintenancePlanStep.CreateBackupJob);
        if (IsCreateRestoreJobSelected) steps.Add(MaintenancePlanStep.CreateRestoreJob);
        return steps;
    }

    #endregion
}
