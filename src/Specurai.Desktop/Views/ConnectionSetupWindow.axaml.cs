using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

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

    private async void BrowseSourceDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選擇來源目錄",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is ConnectionSetupViewModel vm)
            vm.ExternalSourceDirectory = folders[0].Path.LocalPath;
    }

    private async void BrowseKeyFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇金鑰檔",
            AllowMultiple = false
        });

        if (files.Count > 0 && DataContext is ConnectionSetupViewModel vm)
            vm.ExternalKeyFilePath = files[0].Path.LocalPath;
    }

    private void SaveExternalSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectionSetupViewModel vm)
            vm.SaveExternalSourceSettings();
    }
}
