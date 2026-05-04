using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class RecoveryModelDocumentView : UserControl
{
    public RecoveryModelDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is RecoveryModelDocumentViewModel vm)
            vm.Rows.CollectionChanged += (_, _) => SubscribeRowDirtyEvents(vm);
    }

    private void SubscribeRowDirtyEvents(RecoveryModelDocumentViewModel vm)
    {
        foreach (var row in vm.Rows)
            row.DirtyChanged -= OnRowDirtyChanged;
        foreach (var row in vm.Rows)
            row.DirtyChanged += OnRowDirtyChanged;
    }

    private void OnRowDirtyChanged(object? sender, System.EventArgs e)
        => RefreshAllRowColors();

    private void RefreshAllRowColors()
    {
        foreach (var row in RecoveryModelGrid.GetVisualDescendants().OfType<DataGridRow>())
            ApplyRowColor(row);
    }

    private void RecoveryModelGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        => ApplyRowColor(e.Row);

    private static void ApplyRowColor(DataGridRow row)
    {
        if (row.DataContext is RecoveryModelRowViewModel vm)
        {
            if (vm.IsDirty)
                row.Foreground = new SolidColorBrush(Color.Parse("#f38ba8"));
            else
                row.ClearValue(DataGridRow.ForegroundProperty);
        }
    }
}
