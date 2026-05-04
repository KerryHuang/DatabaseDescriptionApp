using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Recovery Model 清單中的單一資料庫列
/// </summary>
public partial class RecoveryModelRowViewModel : ViewModelBase
{
    public string DatabaseName { get; }
    public string OriginalRecoveryModel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedRecoveryModel;

    public bool IsDirty => SelectedRecoveryModel != OriginalRecoveryModel;

    public event EventHandler? DirtyChanged;

    public RecoveryModelRowViewModel(string databaseName, string recoveryModel)
    {
        DatabaseName = databaseName;
        OriginalRecoveryModel = recoveryModel;
        _selectedRecoveryModel = recoveryModel;
    }

    partial void OnSelectedRecoveryModelChanged(string value)
    {
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }
}
