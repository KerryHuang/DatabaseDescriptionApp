using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

/// <summary>
/// 維護計劃管理視窗
/// </summary>
public partial class MaintenancePlanManagerWindow : Window
{
    public MaintenancePlanManagerWindow()
    {
        InitializeComponent();
    }

    public MaintenancePlanManagerWindow(MaintenancePlanManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ConfirmDeleteCallback = async () =>
        {
            // 簡易確認，暫時直接回傳 true
            return true;
        };

        viewModel.OpenWizardCallback = async () =>
        {
            // TODO: 等待精靈視窗實作後啟用
            await Task.CompletedTask;
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MaintenancePlanManagerViewModel vm)
        {
            await vm.LoadJobsCommand.ExecuteAsync(null);
        }
    }
}
