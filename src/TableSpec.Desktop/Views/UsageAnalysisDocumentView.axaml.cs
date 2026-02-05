using Avalonia.Controls;
using Avalonia.Media;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Views;

/// <summary>
/// 使用狀態分析文件視圖
/// </summary>
public partial class UsageAnalysisDocumentView : UserControl
{
    private static readonly IBrush UnusedBrush = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));

    public UsageAnalysisDocumentView()
    {
        InitializeComponent();

        var tableGrid = this.FindControl<DataGrid>("TableDataGrid");
        if (tableGrid != null)
            tableGrid.LoadingRow += OnTableGridLoadingRow;

        var columnGrid = this.FindControl<DataGrid>("ColumnDataGrid");
        if (columnGrid != null)
            columnGrid.LoadingRow += OnColumnGridLoadingRow;
    }

    private void OnTableGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is TableUsageInfo info)
        {
            e.Row.Background = !info.HasQueryActivity
                ? UnusedBrush
                : Brushes.Transparent;
        }
    }

    private void OnColumnGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is ColumnUsageStatus status)
        {
            e.Row.Background = !status.IsUsed
                ? UnusedBrush
                : Brushes.Transparent;
        }
    }
}
