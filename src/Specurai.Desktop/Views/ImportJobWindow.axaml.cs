using System.Linq;
using Avalonia.Controls;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Views;

/// <summary>
/// 匯入現有 Job 對話框
/// </summary>
public partial class ImportJobWindow : Window
{
    public ImportJobWindow()
    {
        InitializeComponent();
    }

    public ImportJobWindow(ImportJobWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 匯入完成後自動關閉視窗
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ImportJobWindowViewModel.IsCompleted) && viewModel.IsCompleted)
                Close();
        };

        // 開啟時自動載入
        Opened += async (s, e) => await viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ImportJobWindowViewModel vm) return;

        // 將 DataGrid 選取的項目同步到 ViewModel
        vm.SelectedJobs.Clear();
        foreach (var item in JobsGrid.SelectedItems.OfType<AgentJobInfo>())
            vm.SelectedJobs.Add(item);

        if (vm.SelectedJobs.Count == 0)
        {
            vm.StatusMessage = "請先選取要匯入的 Job";
            return;
        }

        await vm.ImportCommand.ExecuteAsync(null);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
