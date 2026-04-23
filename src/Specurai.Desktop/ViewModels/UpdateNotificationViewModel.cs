using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 主視窗的更新通知 ViewModel，控制右上角「⬆ 有新版本」徽章顯示。
/// </summary>
public partial class UpdateNotificationViewModel : ViewModelBase
{
    private readonly IUpdateService? _updateService;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    private UpdateCheckResult? _latestResult;

    /// <summary>設計時建構函式。</summary>
    public UpdateNotificationViewModel()
    {
    }

    public UpdateNotificationViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    /// <summary>
    /// 以非阻擋方式檢查更新，併發呼叫會被去重為單次。
    /// </summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_updateService is null) return;
        if (!await _checkGate.WaitAsync(0, ct)) return;

        try
        {
            var result = await _updateService.CheckForUpdateAsync(ct);
            LatestResult = result;
            HasUpdate = result is not null;
            NewVersion = result?.NewVersion ?? string.Empty;
        }
        finally
        {
            _checkGate.Release();
        }
    }
}
