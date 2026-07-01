using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Specurai.Desktop.ViewModels;

/// <summary>資料夾／檔案圖示轉換器（true → 📁、false → 📄）</summary>
public class FolderIconConverter : IValueConverter
{
    public static readonly FolderIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "📁" : "📄";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
