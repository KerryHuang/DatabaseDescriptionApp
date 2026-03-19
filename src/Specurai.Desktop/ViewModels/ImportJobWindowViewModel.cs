using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 匯入現有 Job 對話框的 ViewModel
/// </summary>
public partial class ImportJobWindowViewModel : ViewModelBase
{
    private readonly IAgentJobService? _jobService;

    /// <summary>未標記 [Specurai] 的 Job 清單</summary>
    public ObservableCollection<AgentJobInfo> AvailableJobs { get; } = [];

    /// <summary>使用者勾選的 Job</summary>
    public ObservableCollection<AgentJobInfo> SelectedJobs { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>匯入是否完成</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>匯入的 Job 數量</summary>
    [ObservableProperty]
    private int _importedCount;

    /// <summary>設計時建構函式</summary>
    public ImportJobWindowViewModel() { }

    /// <summary>DI 建構函式</summary>
    public ImportJobWindowViewModel(IAgentJobService jobService)
    {
        _jobService = jobService;
    }

    /// <summary>載入未標記的 Job 清單</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_jobService is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在載入 Job 清單...";

            var jobs = await _jobService.GetNonSpecuraiJobsAsync();
            AvailableJobs.Clear();
            foreach (var job in jobs)
                AvailableJobs.Add(job);

            StatusMessage = jobs.Count > 0
                ? $"找到 {jobs.Count} 個可匯入的 Job"
                : "沒有找到可匯入的 Job";
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

    /// <summary>匯入選取的 Job</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_jobService is null || SelectedJobs.Count == 0) return;

        try
        {
            IsLoading = true;
            var count = 0;

            foreach (var job in SelectedJobs.ToList())
            {
                StatusMessage = $"正在匯入：{job.Name}...";
                await _jobService.ImportJobAsync(job.JobId);
                count++;
            }

            ImportedCount = count;
            StatusMessage = $"已成功匯入 {count} 個 Job";
            IsCompleted = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯入失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
