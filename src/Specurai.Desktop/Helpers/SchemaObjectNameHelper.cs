using System;

namespace Specurai.Desktop.Helpers;

internal static class SchemaObjectNameHelper
{
    /// <summary>
    /// 從 ObjectName（格式：TableName.[ColumnName]）解析欄位名稱部分
    /// </summary>
    public static string ExtractColumnName(string objectName)
    {
        var start = objectName.LastIndexOf(".[", StringComparison.Ordinal);
        if (start < 0) return objectName;
        var end = objectName.LastIndexOf(']');
        if (end <= start + 2) return objectName;
        return objectName.Substring(start + 2, end - start - 2);
    }
}
