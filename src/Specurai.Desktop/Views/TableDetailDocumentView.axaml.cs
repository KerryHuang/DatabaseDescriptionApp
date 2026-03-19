using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class TableDetailDocumentView : UserControl
{
    public TableDetailDocumentView()
    {
        InitializeComponent();
    }

    private void OnColumnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (DataContext is TableDetailDocumentViewModel vm)
        {
            vm.CheckForChanges();
        }
    }
}
