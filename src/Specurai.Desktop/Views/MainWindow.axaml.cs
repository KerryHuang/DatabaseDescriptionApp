using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 設定確認儲存的回調 + 自動更新對話框開啟事件
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfirmSaveCallback = ShowConfirmSaveDialogAsync;
                vm.OpenUpdateDialogRequested += OnOpenUpdateDialogRequested;
            }
        };

        // 視窗開啟後背景檢查更新
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && vm.UpdateNotification is not null)
                await vm.UpdateNotification.CheckAsync();
        };
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ObjectItemViewModel item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ObjectTree?.SelectObjectCommand.Execute(item);
            }
        }
    }

    private async Task<bool> ShowConfirmSaveDialogAsync(string message)
    {
        var dialog = new ConfirmDialog(message);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    private void OnOpenUpdateDialogRequested(UpdateCheckResult? result)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (result is null)
        {
            var current = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            vm.StatusMessage = $"目前已是最新版本（v{current}）";
            return;
        }

        var services = App.Services!;
        var updateService = services.GetRequiredService<IUpdateService>();

        if (result.CanAutoApply)
        {
            var dialogVm = new UpdateDialogViewModel(updateService, result);
            new UpdateDialog { DataContext = dialogVm }.ShowDialog(this);
        }
        else
        {
            new MacOsUpdateInstructionsDialog { DataContext = result }.ShowDialog(this);
        }
    }
}
