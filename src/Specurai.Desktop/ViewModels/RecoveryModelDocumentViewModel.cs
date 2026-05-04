using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Recovery Model 管理文件 ViewModel
/// </summary>
public partial class RecoveryModelDocumentViewModel : DocumentViewModel
{
    private readonly IDatabaseRecoveryModelService? _service;

    public override string DocumentType => "RecoveryModel";
    public override string DocumentKey => DocumentType;

    public ObservableCollection<RecoveryModelRowViewModel> Rows { get; } = [];

    public bool HasChanges => Rows.Any(r => r.IsDirty);

    public int DirtyCount => Rows.Count(r => r.IsDirty);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>確認對話框回呼（由 MainWindowViewModel 設定）</summary>
    public Func<string, Task<bool>>? ConfirmCallback { get; set; }

    public RecoveryModelDocumentViewModel()
    {
        Title = "Recovery Model 管理";
        Icon = "🔧";
    }

    public RecoveryModelDocumentViewModel(IDatabaseRecoveryModelService service) : this()
    {
        _service = service;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_service == null) return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var items = await _service.GetAllAsync(ct);
            Rows.Clear();

            foreach (var item in items)
            {
                var row = new RecoveryModelRowViewModel(item.DatabaseName, item.RecoveryModel);
                row.DirtyChanged += (_, _) => NotifyHasChanges();
                Rows.Add(row);
            }

            NotifyHasChanges();
            StatusMessage = $"已載入 {Rows.Count} 個資料庫";
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

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_service == null) return;

        var dirty = Rows.Where(r => r.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var summary = string.Join("\n", dirty.Select(r =>
            $"  • {r.DatabaseName}：{r.OriginalRecoveryModel} → {r.SelectedRecoveryModel}"));
        var message = $"即將變更以下 {dirty.Count} 個資料庫的 Recovery Model：\n{summary}";

        if (ConfirmCallback != null)
        {
            var confirmed = await ConfirmCallback(message);
            if (!confirmed) return;
        }

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var dirtyCount = dirty.Count;
            var changes = dirty.Select(r => (r.DatabaseName, r.SelectedRecoveryModel));
            await _service.SaveChangesAsync(changes, ct);
            await LoadAsync(ct);
            StatusMessage = $"已成功變更 {dirtyCount} 個資料庫";
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void NotifyHasChanges()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(DirtyCount));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
