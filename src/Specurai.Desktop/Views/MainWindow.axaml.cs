using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        // 註冊已處理的 PointerPressed 事件，攔截 TreeView 右鍵
        ObjectTreeView.AddHandler(InputElement.PointerPressedEvent,
            OnTreeViewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 設定確認儲存的回調 + 自動更新對話框開啟事件
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfirmSaveCallback = ShowConfirmSaveDialogAsync;
                vm.OpenUpdateDialogRequested += OnOpenUpdateDialogRequested;
                vm.ClearTreeSelectionRequested += () =>
                {
                    _suppressNextSelectionChanged = true;
                    ObjectTreeView.SelectedItem = null;
                };
            }
        };

        // 視窗開啟後背景檢查更新
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && vm.UpdateNotification is not null)
                await vm.UpdateNotification.CheckAsync();
        };
    }

    private bool _suppressNextSelectionChanged;

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNextSelectionChanged)
        {
            _suppressNextSelectionChanged = false;
            return;
        }

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DatabaseNodeViewModel dbNode)
        {
            if (DataContext is MainWindowViewModel vmDb)
            {
                vmDb.ObjectTree?.SelectDatabaseCommand.Execute(dbNode);
            }
            return;
        }

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ObjectItemViewModel item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ObjectTree?.SelectObjectCommand.Execute(item);
            }
        }
    }

    private void OnTreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsRightButtonPressed)
            return;

        if (e.Source is not Visual source)
            return;

        // 沿視覺樹尋找 DataContext 為 ObjectItemViewModel 的元素
        var current = source;
        while (current is not null)
        {
            if (current is StyledElement styled && styled.DataContext is ObjectItemViewModel item)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    _suppressNextSelectionChanged = true;
                    vm.OpenSqlQueryForTable(item.Table);
                    e.Handled = true;
                }
                return;
            }
            current = current.GetVisualParent();
        }
    }

    private async Task<bool> ShowConfirmSaveDialogAsync(string message)
    {
        string? banner = null;
        if (DataContext is MainWindowViewModel vm && vm.IsCurrentProfileProduction)
            banner = $"⚠ 正式環境 (Production)：{vm.CurrentEnvironmentDatabase}";

        var dialog = new ConfirmDialog(message, banner);
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
