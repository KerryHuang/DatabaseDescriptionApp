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
/// 維護計劃精靈 ViewModel
/// </summary>
public partial class MaintenancePlanWizardViewModel : ViewModelBase
{
    private readonly IMaintenancePlanService? _planService;
    private readonly IMaintenancePlanSqlGenerator? _sqlGenerator;
    private CancellationTokenSource? _executionCts;

    #region 步驟1 - 基本設定

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _databaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _restorePath = string.Empty;

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

    #endregion

    #region 步驟2 - 選擇步驟

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

    #endregion

    #region 步驟3 - 確認與執行

    [ObservableProperty]
    private string _previewSql = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    #endregion

    #region 步驟狀態

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

    #region 集合

    /// <summary>可選資料庫清單</summary>
    public ObservableCollection<string> Databases { get; } = [];

    /// <summary>步驟檢查結果</summary>
    public ObservableCollection<StepCheckResult> CheckResults { get; } = [];

    /// <summary>執行日誌</summary>
    public ObservableCollection<string> ExecutionLog { get; } = [];

    #endregion

    #region 建構函式

    /// <summary>設計時建構函式</summary>
    public MaintenancePlanWizardViewModel()
    {
    }

    /// <summary>DI 建構函式</summary>
    public MaintenancePlanWizardViewModel(IMaintenancePlanService planService, IMaintenancePlanSqlGenerator sqlGenerator)
    {
        _planService = planService;
        _sqlGenerator = sqlGenerator;
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

    #region 命令

    [RelayCommand(CanExecute = nameof(CanNextStep))]
    private async Task NextStep()
    {
        if (CurrentStep == 2 && _planService is not null)
        {
            // 從步驟2到步驟3時執行前置檢查
            var config = BuildConfig();
            var results = await _planService.CheckStepsAsync(config);
            CheckResults.Clear();
            foreach (var r in results)
                CheckResults.Add(r);
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

    [RelayCommand]
    private async Task Execute()
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
