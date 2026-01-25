using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TableSpec.Desktop.Converters;

/// <summary>
/// 健康狀態轉顏色轉換器
/// </summary>
public class HealthStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "OK" => Brushes.Green,
                "WARNING" => Brushes.Orange,
                "CRITICAL" => Brushes.Red,
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
/// 健康狀態轉圖示轉換器
/// </summary>
public class HealthStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "OK" => "✓",
                "WARNING" => "⚠",
                "CRITICAL" => "✗",
                _ => "?"
            };
        }
        return "?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 整體狀態轉背景色轉換器
/// </summary>
public class OverallStatusBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "OK" => new SolidColorBrush(Color.FromArgb(40, 0, 200, 0)),
                "WARNING" => new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)),
                "CRITICAL" => new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 安裝狀態轉顏色轉換器
/// </summary>
public class InstallStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool installed)
        {
            return installed ? Brushes.Green : Brushes.Gray;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
