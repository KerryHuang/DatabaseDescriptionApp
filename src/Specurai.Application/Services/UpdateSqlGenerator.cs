using System.Globalization;
using System.Text;
using Specurai.Application.Models;
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 異動 SQL 產生器介面
/// </summary>
public interface IUpdateSqlGenerator
{
    /// <summary>比對原值與現值，產生 UPDATE 語句（純邏輯，不碰資料庫）</summary>
    UpdateSqlResult Generate(UpdateSqlRequest request);
}

/// <summary>
/// 異動 SQL 產生器：比對每列原值/現值差異，產出 UPDATE 語句。
/// SET 只含實際改過的欄位；WHERE 一律使用原值（NULL 用 IS NULL）。
/// </summary>
public class UpdateSqlGenerator : IUpdateSqlGenerator
{
    public UpdateSqlResult Generate(UpdateSqlRequest request)
    {
        // GroupBy 容錯：重複欄名（理論上單表 SELECT 不會有）取第一個，不擲例外
        var metaByName = request.Columns
            .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var statements = new List<string>();

        for (var rowIndex = 0; rowIndex < request.Rows.Count; rowIndex++)
        {
            var row = request.Rows[rowIndex];
            var setClauses = new List<string>();
            var conversionFailed = false;

            foreach (var column in request.Columns)
            {
                // 唯讀欄位（identity/timestamp/運算式）與無來源欄者不可異動
                if (column.IsReadOnly || string.IsNullOrEmpty(column.BaseColumn))
                    continue;

                var original = Normalize(row.Original.GetValueOrDefault(column.ColumnName));
                if (!TryConvert(row.Current.GetValueOrDefault(column.ColumnName), column.ClrType, out var current))
                {
                    warnings.Add($"第 {rowIndex + 1} 列：欄位「{column.ColumnName}」的值無法轉換為 {column.ClrType.Name}，已跳過該列。");
                    conversionFailed = true;
                    break;
                }

                if (!ValuesEqual(original, current))
                    setClauses.Add($"{Quote(column.BaseColumn!)} = {FormatLiteral(current)}");
            }

            if (conversionFailed || setClauses.Count == 0)
                continue;

            var whereClauses = new List<string>();
            foreach (var keyName in request.KeyColumns)
            {
                if (!metaByName.TryGetValue(keyName, out var meta) || string.IsNullOrEmpty(meta.BaseColumn))
                    continue;
                // timestamp/byte[] 不進 WHERE
                if (meta.ClrType == typeof(byte[]))
                    continue;

                var value = Normalize(row.Original.GetValueOrDefault(keyName));
                whereClauses.Add(value == null
                    ? $"{Quote(meta.BaseColumn!)} IS NULL"
                    : $"{Quote(meta.BaseColumn!)} = {FormatLiteral(value)}");
            }

            if (whereClauses.Count == 0)
            {
                warnings.Add($"第 {rowIndex + 1} 列：無可用的定位欄位，已跳過該列。");
                continue;
            }

            var tableName = string.IsNullOrEmpty(request.TargetSchema)
                ? Quote(request.TargetTable)
                : $"{Quote(request.TargetSchema)}.{Quote(request.TargetTable)}";

            statements.Add($"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)};");
        }

        if (statements.Count == 0)
            return new UpdateSqlResult { Warnings = warnings };

        var sb = new StringBuilder();
        if (request.IsFallbackKeys)
            sb.AppendLine("-- 警告：無主鍵定位，執行前請先 Dry Run 確認影響筆數");
        foreach (var warning in warnings)
            sb.AppendLine($"-- 警告：{warning}");
        sb.AppendJoin(Environment.NewLine, statements);

        return new UpdateSqlResult
        {
            Sql = sb.ToString(),
            StatementCount = statements.Count,
            Warnings = warnings
        };
    }

    /// <summary>DBNull 正規化為 null</summary>
    private static object? Normalize(object? value) => value is DBNull ? null : value;

    /// <summary>
    /// 將現值轉為欄位 CLR 型別（DataGrid 編輯後的值常是字串）。
    /// 字串輸入依使用者目前文化解析（與格子顯示格式一致）。
    /// </summary>
    private static bool TryConvert(object? value, Type clrType, out object? converted)
    {
        converted = value is DBNull ? null : value;
        if (converted == null || clrType.IsInstanceOfType(converted))
            return true;

        try
        {
            if (converted is string s)
            {
                converted = clrType == typeof(Guid) ? Guid.Parse(s)
                    : clrType == typeof(DateTime) ? DateTime.Parse(s, CultureInfo.CurrentCulture)
                    : Convert.ChangeType(s, clrType, CultureInfo.CurrentCulture);
            }
            else
            {
                converted = Convert.ChangeType(converted, clrType, CultureInfo.InvariantCulture);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b);
    }

    /// <summary>識別字加方括號（] 跳脫為 ]]）</summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    /// <summary>依型別產生 SQL 字面值</summary>
    private static string FormatLiteral(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}'",
        DateTimeOffset dto => $"'{dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}'",
        TimeSpan ts => $"'{ts}'",
        Guid g => $"'{g}'",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        string s => $"N'{s.Replace("'", "''")}'",
        char c => $"N'{(c == '\'' ? "''" : c.ToString())}'",
        _ => $"N'{value.ToString()?.Replace("'", "''")}'"
    };
}
