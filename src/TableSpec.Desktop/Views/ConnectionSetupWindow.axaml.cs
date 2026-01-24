using Avalonia.Controls;
using Avalonia.Interactivity;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class ConnectionSetupWindow : Window
{
    public ConnectionSetupWindow()
    {
        InitializeComponent();
    }

    public ConnectionSetupWindow(ConnectionSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
