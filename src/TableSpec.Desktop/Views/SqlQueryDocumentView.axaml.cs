using System;
using System.Linq;
using Avalonia.Controls;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Views;

public partial class SqlQueryDocumentView : UserControl
{
    private SqlQueryDocumentViewModel? _currentVm;

    public SqlQueryDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 移除舊的事件處理
        if (_currentVm != null)
        {
            _currentVm.ResultColumns.CollectionChanged -= OnResultColumnsChanged;
        }

        _currentVm = DataContext as SqlQueryDocumentViewModel;

        if (_currentVm != null)
        {
            // 監聽 ResultColumns 的變更
            _currentVm.ResultColumns.CollectionChanged += OnResultColumnsChanged;

            // 初始化現有的欄位
            UpdateResultGridColumns();
        }
    }

    private void OnResultColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateResultGridColumns();
    }

    private void UpdateResultGridColumns()
    {
        if (_currentVm == null)
            return;

        ResultGrid.Columns.Clear();

        foreach (var col in _currentVm.ResultColumns)
        {
            // 檢查欄位是否已經屬於其他 DataGrid
            // 如果是，則建立新的欄位實例
            if (col is DataGridTextColumn textCol)
            {
                var newCol = new DataGridTextColumn
                {
                    Header = textCol.Header,
                    Binding = textCol.Binding,
                    Width = textCol.Width,
                    IsReadOnly = textCol.IsReadOnly
                };
                ResultGrid.Columns.Add(newCol);
            }
            else
            {
                // 嘗試直接加入（如果沒有被其他 DataGrid 使用）
                try
                {
                    ResultGrid.Columns.Add(col);
                }
                catch (InvalidOperationException)
                {
                    // 如果欄位已被使用，則忽略
                }
            }
        }
    }
}
