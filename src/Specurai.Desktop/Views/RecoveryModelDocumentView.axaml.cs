using Avalonia.Controls;
using Avalonia.Media;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class RecoveryModelDocumentView : UserControl
{
    public RecoveryModelDocumentView()
    {
        InitializeComponent();
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
