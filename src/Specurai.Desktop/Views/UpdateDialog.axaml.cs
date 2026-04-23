using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
