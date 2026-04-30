using System;
using Avalonia.Controls;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

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
        if (_currentVm != null)
        {
            _currentVm.ResultColumns.CollectionChanged -= OnResultColumnsChanged;
        }

        _currentVm = DataContext as SqlQueryDocumentViewModel;

        if (_currentVm != null)
        {
            _currentVm.ResultColumns.CollectionChanged += OnResultColumnsChanged;
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
            if (col is DataGridTextColumn textCol)
            {
                ResultGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = textCol.Header,
                    Binding = textCol.Binding,
                    Width = textCol.Width,
                    IsReadOnly = textCol.IsReadOnly
                });
            }
            else
            {
                try { ResultGrid.Columns.Add(col); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
