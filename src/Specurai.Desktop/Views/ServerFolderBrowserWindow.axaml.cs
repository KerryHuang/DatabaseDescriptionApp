using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class ServerFolderBrowserWindow : Window
{
    public ServerFolderBrowserWindow()
    {
        InitializeComponent();
    }

    public ServerFolderBrowserWindow(ServerFolderBrowserViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += confirmed => Close(confirmed);
        Opened += async (_, _) => await viewModel.LoadRootAsync();
    }
}
