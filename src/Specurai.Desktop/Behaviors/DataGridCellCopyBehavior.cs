using System.Collections.Generic;

namespace Specurai.Desktop.Behaviors;

/// <summary>
/// 為 DataGrid 啟用「按儲存格複製」行為的附加屬性。
/// 互動邏輯（Ctrl+C 攔截、ContextMenu 注入）將於後續 Task 加入。
/// </summary>
public static class DataGridCellCopyBehavior
{
    /// <summary>
    /// 將 Avalonia Binding.Path 字串正規化為純屬性名（去除前後中括號）。
    /// </summary>
    internal static string? NormalizeBindingPath(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return raw.TrimStart('[').TrimEnd(']');
    }

    /// <summary>
    /// 從 row 物件依路徑取值並轉為字串。
    /// 支援 IDictionary&lt;string, object?&gt;（動態欄位，如 SqlQuery）與一般強型別物件（反射）。
    /// </summary>
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
