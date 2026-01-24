using System.Linq;
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class SqlQueryDocumentView : UserControl
{
    public SqlQueryDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        UpdateResultGridColumns();
    }

    private void UpdateResultGridColumns()
    {
        if (DataContext is SqlQueryDocumentViewModel vm)
        {
            // 監聽 ResultColumns 的變更
            vm.ResultColumns.CollectionChanged += (s, args) =>
            {
                ResultGrid.Columns.Clear();
                foreach (var col in vm.ResultColumns)
                {
                    ResultGrid.Columns.Add(col);
                }
            };

            // 初始化現有的欄位
            ResultGrid.Columns.Clear();
            foreach (var col in vm.ResultColumns)
            {
                ResultGrid.Columns.Add(col);
            }
        }
    }
}
