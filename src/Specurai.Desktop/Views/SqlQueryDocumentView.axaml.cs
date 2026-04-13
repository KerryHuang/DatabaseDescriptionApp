using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Views;

public partial class SqlQueryDocumentView : UserControl
{
    private SqlQueryDocumentViewModel? _currentVm;

    public SqlQueryDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 攔截 Ctrl+C，只複製目前儲存格的值
        ResultGrid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
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

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true; // 阻止預設的整列複製行為
            CopyCurrentCell();
        }
    }

    private void OnCopyCellClicked(object? sender, RoutedEventArgs e)
    {
        CopyCurrentCell();
    }

    private void OnCopyRowClicked(object? sender, RoutedEventArgs e)
    {
        if (ResultGrid.SelectedItem is not Dictionary<string, object?> row) return;

        var values = row.Values.Select(v => v?.ToString() ?? string.Empty);
        var text = string.Join("\t", values);
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    private void CopyCurrentCell()
    {
        if (ResultGrid.SelectedItem is not Dictionary<string, object?> row) return;

        var columnName = GetCurrentColumnName();
        if (columnName == null) return;

        if (!row.TryGetValue(columnName, out var value)) return;

        var text = value?.ToString()?.Trim() ?? string.Empty;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    private string? GetCurrentColumnName()
    {
        var col = ResultGrid.CurrentColumn as DataGridTextColumn;
        if (col?.Binding is Avalonia.Data.Binding { Path: { } path })
            return path.TrimStart('[').TrimEnd(']');
        return null;
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
