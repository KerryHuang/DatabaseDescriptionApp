using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TableSpec.Desktop.ViewModels;

namespace TableSpec.Desktop.Converters;

/// <summary>
/// 一致性等級轉換為背景顏色
/// </summary>
public class ConsistencyLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConsistencyLevel level)
        {
            return level switch
            {
                ConsistencyLevel.Consistent => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), // 綠色
                ConsistencyLevel.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),    // 黃色
                ConsistencyLevel.Severe => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),     // 紅色
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 一致性等級轉換為前景顏色（文字顏色）
/// </summary>
public class ConsistencyLevelToForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConsistencyLevel level)
        {
            return level switch
            {
                ConsistencyLevel.Consistent => Brushes.White,
                ConsistencyLevel.Warning => Brushes.Black,
                ConsistencyLevel.Severe => Brushes.White,
                _ => Brushes.Black
            };
        }
        return Brushes.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 比較型態是否與主要型態不一致
/// </summary>
public class TypeInconsistentToColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 &&
            values[0] is string currentType &&
            values[1] is string primaryType)
        {
            if (currentType != primaryType)
            {
                // 不一致：淺紅色背景
                return new SolidColorBrush(Color.FromArgb(0x40, 0xF4, 0x43, 0x36));
            }
        }
        return Brushes.Transparent;
    }
}

/// <summary>
/// 布林值反轉
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

/// <summary>
/// 約束數量轉換為文字
/// </summary>
public class ConstraintCountToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? "-" : count.ToString();
        }
        return "-";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布林值轉換為「是/否」
/// </summary>
public class BoolToYesNoConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? "是" : "否";
        }
        return "否";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 一致性狀態轉換為背景顏色
/// </summary>
public class ConsistentToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConsistent)
        {
            return isConsistent
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) // 綠色：一致
                : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // 紅色：不一致
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 一致性狀態轉換為前景顏色
/// </summary>
public class ConsistentToForegroundConverter : IValueConverter
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
