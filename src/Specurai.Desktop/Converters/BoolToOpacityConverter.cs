using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Specurai.Desktop.Converters;

/// <summary>
/// 將布林值轉為透明度：true 為 1.0，false 為 0.45（用於停用項目的灰階呈現）。
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? 0.45 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
