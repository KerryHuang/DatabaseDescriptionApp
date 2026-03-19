using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

/// <summary>
/// 排程編輯對話框
/// </summary>
public partial class ScheduleEditWindow : Window
{
    public ScheduleEditWindow()
    {
        InitializeComponent();
    }

    public ScheduleEditWindow(ScheduleEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 儲存成功後自動關閉視窗
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ScheduleEditViewModel.IsSaved) && viewModel.IsSaved)
                Close();
        };

        // 根據 ViewModel 的 SelectedFreqType 設定 ComboBox 初始值
        FreqTypeCombo.SelectedIndex = viewModel.SelectedFreqType == 8 ? 1 : 0;
    }

    private void OnFreqTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ScheduleEditViewModel vm && FreqTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (int.TryParse(tag, out var freqType))
                vm.SelectedFreqType = freqType;
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
