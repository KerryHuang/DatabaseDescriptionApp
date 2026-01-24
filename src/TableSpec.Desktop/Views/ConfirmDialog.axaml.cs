using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TableSpec.Desktop.Views;

public partial class ConfirmDialog : Window
{
    public bool Result { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
