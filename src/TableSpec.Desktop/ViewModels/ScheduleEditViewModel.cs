using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 排程編輯對話框的 ViewModel
/// </summary>
public partial class ScheduleEditViewModel : ViewModelBase
{
    private readonly IAgentJobService? _jobService;
    private Guid _jobId;

    [ObservableProperty] private TimeSpan _scheduleTime;
    [ObservableProperty] private int _selectedFreqType = 4; // 每日
    [ObservableProperty] private int _freqInterval = 1;
    [ObservableProperty] private bool _isSaved;

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public ScheduleEditViewModel() { }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public ScheduleEditViewModel(IAgentJobService jobService, Guid jobId, int currentTime, int freqType)
    {
        _jobService = jobService;
        _jobId = jobId;
        ScheduleTime = new TimeSpan(currentTime / 10000, (currentTime / 100) % 100, currentTime % 100);
        SelectedFreqType = freqType > 0 ? freqType : 4;
    }

    /// <summary>
    /// 儲存排程設定
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_jobService == null) return;
        var time = ScheduleTime.Hours * 10000 + ScheduleTime.Minutes * 100;
        await _jobService.UpdateScheduleAsync(_jobId, SelectedFreqType, FreqInterval, time);
        IsSaved = true;
    }
}
