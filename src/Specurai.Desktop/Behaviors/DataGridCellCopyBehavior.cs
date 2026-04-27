using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Behaviors;

/// <summary>
/// 為 DataGrid 啟用「按儲存格複製」的附加屬性 Behavior。
/// 設 Enable="True" 後：
///   1. Ctrl+C 改為僅複製目前儲存格的值（編輯模式時放行）。
///   2. 右鍵選單提供「複製儲存格」/「複製整列」。
///   3. 僅對 DataGridBoundColumn（Text、CheckBox）生效；DataGridTemplateColumn 不處理。
/// </summary>
public static class DataGridCellCopyBehavior
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>(
            "Enable", typeof(DataGridCellCopyBehavior));

    static DataGridCellCopyBehavior()
    {
        EnableProperty.Changed.AddClassHandler<DataGrid>(OnEnableChanged);
    }

    public static void SetEnable(DataGrid d, bool v) => d.SetValue(EnableProperty, v);
    public static bool GetEnable(DataGrid d) => d.GetValue(EnableProperty);

    // --- 生命週期 ---

    private static void OnEnableChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
        }
        else
        {
            grid.AttachedToVisualTree -= OnAttached;
            grid.DetachedFromVisualTree -= OnDetached;
            Detach(grid);
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) Attach(grid);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) Detach(grid);
    }

    private static void Attach(DataGrid grid)
    {
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;

        if (grid.ContextMenu == null)
        {
            var menu = new ContextMenu();
            var cellItem = new MenuItem { Header = "複製儲存格" };
            cellItem.Click += (_, _) => CopyCurrentCell(grid);
            var rowItem = new MenuItem { Header = "複製整列" };
            rowItem.Click += (_, _) => CopyCurrentRow(grid);
            menu.Items.Add(cellItem);
            menu.Items.Add(rowItem);
            grid.ContextMenu = menu;
        }

        grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void Detach(DataGrid grid)
    {
        grid.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    }

    // --- 鍵盤攔截 ---

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox or TextPresenter)
            return;

        if (e.Key != Key.C || e.KeyModifiers != KeyModifiers.Control)
            return;

        if (sender is not DataGrid grid)
            return;

        e.Handled = true;
        CopyCurrentCell(grid);
    }

    // --- 複製動作 ---

    private static void CopyCurrentCell(DataGrid grid)
    {
        if (grid.SelectedItem is null) return;
        var path = GetBindingPath(grid.CurrentColumn);
        if (path == null) return;

        var value = GetCellValue(grid.SelectedItem, path) ?? string.Empty;
        SetClipboardText(grid, value);
    }

    private static void CopyCurrentRow(DataGrid grid)
    {
        if (grid.SelectedItem is null) return;

        var values = grid.Columns
            .Select(GetBindingPath)
            .Where(p => p != null)
            .Select(p => GetCellValue(grid.SelectedItem!, p!) ?? string.Empty);

        var text = string.Join("\t", values);
        SetClipboardText(grid, text);
    }

    private static void SetClipboardText(Control grid, string text)
    {
        TopLevel.GetTopLevel(grid)?.Clipboard?.SetTextAsync(text);
    }

    // --- 取繫結路徑 ---

    private static string? GetBindingPath(DataGridColumn? column)
    {
        if (column is DataGridBoundColumn bound &&
            bound.Binding is Binding b)
        {
            return NormalizeBindingPath(b.Path);
        }
        return null;
    }

    // --- 純函式（Task 1 已測試）---

    internal static string? NormalizeBindingPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return raw.TrimStart('[').TrimEnd(']');
    }

    internal static string? GetCellValue(object row, string path)
    {
        if (row is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(path, out var v) ? v?.ToString() : null;
        }

        var prop = row.GetType().GetProperty(path);
        return prop?.GetValue(row)?.ToString();
    }
}
