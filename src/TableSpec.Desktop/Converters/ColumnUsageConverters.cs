using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TableSpec.Desktop.Converters;

/// <summary>
/// 布林值轉換為一致性顏色（背景）
/// </summary>
public class BoolToConsistencyColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConsistent)
        {
            return isConsistent
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) // 綠色
                : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // 紅色
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布林值轉換為前景顏色
/// </summary>
public class BoolToConsistencyForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 無論一致或不一致，都用白色文字
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布林值轉換為差異標記文字
/// </summary>
public class BoolToDifferenceTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasDifference)
        {
            return hasDifference ? "✗" : "✓";
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 差異布林值轉換為顏色
/// </summary>
public class DifferenceToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasDifference)
        {
            return hasDifference
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)) // 紅色
                : new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 綠色
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布林值轉換為可空文字（NULL / NOT NULL）
/// </summary>
public class BoolToNullableTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isNullable)
        {
            return isNullable ? "NULL" : "NOT NULL";
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
