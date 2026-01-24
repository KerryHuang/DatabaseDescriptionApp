using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

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
