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
        if (grid.CurrentColumn is not DataGridBoundColumn bound) return;

        var value = ExtractCellText(bound, grid.SelectedItem) ?? string.Empty;
        SetClipboardText(grid, value);
    }

    private static void CopyCurrentRow(DataGrid grid)
    {
        if (grid.SelectedItem is null) return;

        var values = grid.Columns
            .OfType<DataGridBoundColumn>()
            .Select(c => ExtractCellText(c, grid.SelectedItem!) ?? string.Empty);

        var text = string.Join("\t", values);
        SetClipboardText(grid, text);
    }

    private static void SetClipboardText(Control grid, string text)
    {
        TopLevel.GetTopLevel(grid)?.Clipboard?.SetTextAsync(text);
    }

    // --- 取得儲存格文字 ---
    // 先走快速路徑（classic Binding，含 SqlQuery 動態欄位 Dictionary indexer）；
    // 對 compiled binding（AXAML 預設）等其他 IBinding 實作，
    // 透過暫時 TextBlock 套用 column.Binding 並以 row 作 DataContext 取值，
    // 對所有 binding 型別通用。
    private static string? ExtractCellText(DataGridBoundColumn column, object row)
    {
        if (column.Binding is Binding classic && !string.IsNullOrEmpty(classic.Path))
        {
            var path = NormalizeBindingPath(classic.Path);
            return path != null ? GetCellValue(row, path) : null;
        }

        if (column.Binding is null) return null;

        var temp = new TextBlock { DataContext = row };
        using var subscription = temp.Bind(TextBlock.TextProperty, column.Binding);
        return temp.Text;
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
