using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Desktop.Converters;

/// <summary>
/// 風險等級轉換為背景顏色
/// </summary>
public class RiskLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel level)
        {
            return level switch
            {
                RiskLevel.Low => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),      // 綠色
                RiskLevel.Medium => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),   // 黃色/橙色
                RiskLevel.High => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),     // 紅色
                RiskLevel.Forbidden => new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00)), // 深紅色
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
/// 風險等級轉換為顏色（MultiBinding 版本）
/// </summary>
public class RiskLevelColorMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is RiskLevel level)
        {
            return level switch
            {
                RiskLevel.Low => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),      // 綠色
                RiskLevel.Medium => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),   // 黃色/橙色
                RiskLevel.High => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),     // 紅色
                RiskLevel.Forbidden => new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00)), // 深紅色
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }
}

/// <summary>
/// 風險等級轉換為中文文字
/// </summary>
public class RiskLevelToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel level)
        {
            return level switch
            {
                RiskLevel.Low => "低",
                RiskLevel.Medium => "中",
                RiskLevel.High => "高",
                RiskLevel.Forbidden => "禁止",
                _ => level.ToString()
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 差異類型轉換為中文文字
/// </summary>
public class DifferenceTypeToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DifferenceType type)
        {
            return type switch
            {
                DifferenceType.Added => "新增",
                DifferenceType.Modified => "修改",
                _ => type.ToString()
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Schema 物件類型轉換為中文文字
/// </summary>
public class SchemaObjectTypeToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SchemaObjectType type)
        {
            return type switch
            {
                SchemaObjectType.Table => "表格",
                SchemaObjectType.Column => "欄位",
                SchemaObjectType.Index => "索引",
                SchemaObjectType.Constraint => "約束",
                SchemaObjectType.View => "檢視表",
                SchemaObjectType.StoredProcedure => "預存程序",
                SchemaObjectType.Function => "函數",
                SchemaObjectType.Trigger => "觸發程序",
                _ => type.ToString()
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
