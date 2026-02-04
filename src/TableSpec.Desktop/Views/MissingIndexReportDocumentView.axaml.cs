using Avalonia.Controls;
using Avalonia.Media;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Views;

/// <summary>
/// 缺少索引報表文件視圖
/// </summary>
public partial class MissingIndexReportDocumentView : UserControl
{
    // 嚴重度背景色
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.FromArgb(68, 255, 0, 0));
    private static readonly IBrush HighBrush = new SolidColorBrush(Color.FromArgb(68, 255, 140, 0));
    private static readonly IBrush MediumBrush = new SolidColorBrush(Color.FromArgb(68, 255, 215, 0));

    public MissingIndexReportDocumentView()
    {
        InitializeComponent();

        var dataGrid = this.FindControl<DataGrid>("MissingIndexDataGrid");
        if (dataGrid != null)
        {
            dataGrid.LoadingRow += OnDataGridLoadingRow;
        }
    }

    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is MissingIndex index)
        {
            e.Row.Background = index.SeverityLevel switch
            {
                "Critical" => CriticalBrush,
                "High" => HighBrush,
                "Medium" => MediumBrush,
                _ => Brushes.Transparent
            };
        }
    }
}
