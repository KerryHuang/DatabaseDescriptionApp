using System.Collections.Specialized;
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class SqlQueryWindow : Window
{
    public SqlQueryWindow()
    {
        InitializeComponent();
    }

    public SqlQueryWindow(SqlQueryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 監聽欄位變更
        viewModel.ResultColumns.CollectionChanged += OnResultColumnsChanged;
    }

    private void OnResultColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResultGrid.Columns.Clear();
        }
        else if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (DataGridColumn col in e.NewItems)
            {
                ResultGrid.Columns.Add(col);
            }
        }
    }
}
