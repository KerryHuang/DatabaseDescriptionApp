using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 維護計劃管理面板 ViewModel
/// </summary>
public partial class MaintenancePlanManagerViewModel : ViewModelBase
{
    private readonly IAgentJobService? _jobService;
    private readonly IMaintenancePlanService? _planService;

    [ObservableProperty]
    private AgentJobInfo? _selectedJob;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Agent Job 清單</summary>
    public ObservableCollection<AgentJobInfo> Jobs { get; } = [];

    /// <summary>刪除確認回呼</summary>
    public Func<Task<bool>>? ConfirmDeleteCallback { get; set; }

    /// <summary>開啟精靈回呼</summary>
    public Func<Task>? OpenWizardCallback { get; set; }

    /// <summary>編輯排程回呼</summary>
    public Func<AgentJobInfo, Task>? EditScheduleCallback { get; set; }

    /// <summary>設計時建構函式</summary>
    public MaintenancePlanManagerViewModel() { }

    /// <summary>DI 建構函式</summary>
    public MaintenancePlanManagerViewModel(IAgentJobService jobService, IMaintenancePlanService planService)
    {
        _jobService = jobService;
        _planService = planService;
    }

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

    /// <summary>開啟精靈</summary>
    [RelayCommand]
    private async Task OpenWizardAsync()
    {
        if (OpenWizardCallback is null) return;

        await OpenWizardCallback();
        await LoadJobsAsync();
    }
}
