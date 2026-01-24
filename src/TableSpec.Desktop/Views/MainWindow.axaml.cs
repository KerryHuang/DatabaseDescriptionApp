using System.Threading.Tasks;
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 設定確認儲存的回調
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfirmSaveCallback = ShowConfirmSaveDialogAsync;
            }
        };
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

    private async Task<bool> ShowConfirmSaveDialogAsync(string message)
    {
        var dialog = new ConfirmDialog(message);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }
}
