using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Specurai.Desktop.ViewModels;

public partial class FilterOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Label { get; init; } = string.Empty;

    public event Action<FilterOptionViewModel>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);
}
