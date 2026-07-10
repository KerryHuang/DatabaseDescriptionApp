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
    // (CheckBox, 欄名) 成對保存：CheckBox.Content 若直接給字串，Avalonia 會把 "_" 當成
    // 快捷鍵記號吃掉（例如 EMP_ID 顯示成 EMPID）。改用 TextBlock 顯示（不解析快捷鍵記號），
    // 並另外保存原始欄名清單，選取結果不再從 Content?.ToString() 取值。
    private readonly List<(CheckBox CheckBox, string ColumnName)> _checkBoxes = [];

    public KeyColumnPickerWindow()
    {
        // 設計時建構子
        InitializeComponent();
    }

    public KeyColumnPickerWindow(IReadOnlyList<string> columns) : this()
    {
        foreach (var column in columns)
        {
            var checkBox = new CheckBox
            {
                Content = new TextBlock { Text = column },
                Margin = new Avalonia.Thickness(4, 2)
            };
            _checkBoxes.Add((checkBox, column));
            ColumnList.Items.Add(checkBox);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selected = _checkBoxes
            .Where(c => c.CheckBox.IsChecked == true)
            .Select(c => c.ColumnName)
            .ToList();

        Close(selected.Count > 0 ? (IReadOnlyList<string>?)selected : null);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(null);
}
