using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Views;

/// <summary>
/// 無主鍵時的定位欄位挑選視窗：確定回傳勾選欄位清單，略過/關閉回傳 null
/// </summary>
public partial class KeyColumnPickerWindow : Window
{
    private readonly List<CheckBox> _checkBoxes = [];

    public KeyColumnPickerWindow()
    {
        // 設計時建構子
        InitializeComponent();
    }

    public KeyColumnPickerWindow(IReadOnlyList<string> columns) : this()
    {
        foreach (var column in columns)
        {
            var checkBox = new CheckBox { Content = column, Margin = new Avalonia.Thickness(4, 2) };
            _checkBoxes.Add(checkBox);
            ColumnList.Items.Add(checkBox);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selected = _checkBoxes
            .Where(c => c.IsChecked == true)
            .Select(c => c.Content?.ToString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        Close(selected.Count > 0 ? (IReadOnlyList<string>?)selected : null);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(null);
}
