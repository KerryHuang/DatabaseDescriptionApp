using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 更新對話框 ViewModel：顯示版本資訊、Release Notes、下載進度，並提供重啟按鈕。
/// </summary>
public partial class UpdateDialogViewModel : ViewModelBase
{
    private readonly IUpdateService? _updateService;

    [ObservableProperty]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _canConfirm = true;

    [ObservableProperty]
    private bool _canRestart;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>設計時建構函式。</summary>
    public UpdateDialogViewModel()
    {
    }

    public UpdateDialogViewModel(IUpdateService updateService, UpdateCheckResult result)
    {
        _updateService = updateService;
        NewVersion = result.NewVersion;
        ReleaseNotes = result.ReleaseNotes;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_updateService is null) return;

        CanConfirm = false;
        ErrorMessage = string.Empty;
        var progress = new Progress<int>(p => Progress = p);

        try
        {
            await _updateService.DownloadAsync(progress);
            CanRestart = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CanConfirm = true;
        }
    }

    [RelayCommand]
    private void Restart()
    {
        _updateService?.ApplyAndRestart();
    }
}
