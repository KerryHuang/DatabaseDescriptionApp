using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Converters;

/// <summary>
/// 將 <see cref="ConnectionProfile"/> 轉為選擇器顯示字串：【環境簡稱】名稱 (預設)。
/// </summary>
public class ConnectionProfileDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionProfile p)
            return value?.ToString();

        var tag = p.Environment switch
        {
            DatabaseEnvironment.Development => "開發",
            DatabaseEnvironment.Testing     => "測試",
            DatabaseEnvironment.Staging     => "預備",
            DatabaseEnvironment.Production  => "正式",
            _                               => p.Environment.ToString()
        };

        return p.IsDefault ? $"【{tag}】{p.Name} (預設)" : $"【{tag}】{p.Name}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
