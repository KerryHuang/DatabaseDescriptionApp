using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Views;

public partial class MacOsUpdateInstructionsDialog : Window
{
    public MacOsUpdateInstructionsDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyCommandClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
            await Clipboard.SetTextAsync("xattr -cr /Applications/Specurai.app");
    }

    private void OnOpenReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateCheckResult result && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = result.ReleaseUrl,
                UseShellExecute = true,
            });
        }
    }
}
