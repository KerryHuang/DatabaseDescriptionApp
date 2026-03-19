using Avalonia.Controls;
using Avalonia.Interactivity;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class ImportConnectionsWindow : Window
{
    public ImportConnectionsWindow()
    {
        InitializeComponent();
    }

    public ImportConnectionsWindow(ImportConnectionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnDecryptClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ImportConnectionsViewModel vm)
        {
            vm.DecryptAndLoad();
        }
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportConnectionsViewModel vm) return;

        var result = vm.ExecuteImport();
        // 顯示結果摘要可在未來加入訊息對話框
        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
