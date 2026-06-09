using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Converters;

/// <summary>
/// 將 <see cref="DatabaseEnvironment"/> 列舉轉為繁體中文顯示名稱，供環境下拉選單使用。
/// </summary>
public class DatabaseEnvironmentDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DatabaseEnvironment.Development => "開發環境",
            DatabaseEnvironment.Testing => "測試環境",
            DatabaseEnvironment.Staging => "預備環境",
            DatabaseEnvironment.Production => "正式環境",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
