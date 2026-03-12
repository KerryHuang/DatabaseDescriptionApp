using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class ExportConnectionsWindow : Window
{
    public ExportConnectionsWindow()
    {
        InitializeComponent();
    }

    public ExportConnectionsWindow(ExportConnectionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportConnectionsViewModel vm) return;

        // 驗證
        if (vm.ProfileSelections.All(p => !p.IsSelected))
        {
            // 無選擇任何連線
            return;
        }

        if (vm.UseEncryption)
        {
            if (string.IsNullOrEmpty(vm.EncryptionPassword))
                return;
            if (vm.EncryptionPassword != vm.ConfirmPassword)
                return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var defaultFileName = vm.UseEncryption ? "connections.tsjson" : "connections.json";
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存連線設定檔",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = vm.UseEncryption
                ?
                [
                    new FilePickerFileType("加密連線設定檔") { Patterns = ["*.tsjson"] }
                ]
                :
                [
                    new FilePickerFileType("JSON 檔案") { Patterns = ["*.json"] }
                ]
        });

        if (file == null) return;

        var data = vm.GetExportData();
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(data);

        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
