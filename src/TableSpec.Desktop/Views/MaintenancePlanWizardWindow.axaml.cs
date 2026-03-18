using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

/// <summary>
/// 維護計劃精靈視窗
/// </summary>
public partial class MaintenancePlanWizardWindow : Window
{
    public MaintenancePlanWizardWindow()
    {
        InitializeComponent();
    }

    public MaintenancePlanWizardWindow(MaintenancePlanWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
