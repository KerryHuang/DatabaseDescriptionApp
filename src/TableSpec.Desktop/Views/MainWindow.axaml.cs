using Avalonia.Controls;
using Avalonia.Controls.Selection;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ObjectItemViewModel item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ObjectTree?.SelectObjectCommand.Execute(item);
            }
        }
    }
}