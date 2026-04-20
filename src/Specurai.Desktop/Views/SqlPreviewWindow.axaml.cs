using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Specurai.Desktop.Views;

public partial class SqlPreviewWindow : Window
{
    public SqlPreviewWindow(string sql)
    {
        InitializeComponent();
        SqlText.Text = sql;
    }

    private async void OnCopyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(SqlText.Text);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
