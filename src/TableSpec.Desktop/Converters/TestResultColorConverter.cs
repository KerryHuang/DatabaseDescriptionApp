using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TableSpec.Desktop.Converters;

public class TestResultColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string result)
        {
            if (result.Contains("成功"))
                return Brushes.Green;
            if (result.Contains("失敗") || result.Contains("錯誤"))
                return Brushes.Red;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
