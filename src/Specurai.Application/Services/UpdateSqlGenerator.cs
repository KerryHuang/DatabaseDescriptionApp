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
/// 注意：Avalonia DataGrid 可編輯欄的 TwoWay 綁定會在儲存格顯示時，把「顯示文字」
/// 回寫進資料列（早於快照），因此 Original/Current 都可能因 UI 綁定而是字串形態，
/// 兩邊都必須正規化到欄位型別後再比對，否則會把 bool/DateTime/數字誤判為異動。
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

                var rawOriginal = Normalize(row.Original.GetValueOrDefault(column.ColumnName));
                if (!TryConvert(row.Current.GetValueOrDefault(column.ColumnName), column.ClrType, out var current))
                {
                    warnings.Add($"第 {rowIndex + 1} 列：欄位「{column.ColumnName}」的值無法轉換為 {column.ClrType.Name}，已跳過該列。");
                    conversionFailed = true;
                    break;
                }

                // 對稱正規化：Original 也可能被 UI 綁定污染成顯示字串，需比照 Current 轉型。
                // 轉不動時保守視為「相等（不產生異動）」，因為假異動的破壞性大於漏產生
                // （漏產生使用者仍可再次比對重跑；假異動可能覆寫別人的正確資料）。
                if (!TryConvert(rawOriginal, column.ClrType, out var original))
                    original = current;

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

                var rawValue = Normalize(row.Original.GetValueOrDefault(keyName));
                // WHERE 字面值同樣正規化到欄位型別，避免鍵欄被污染成字串時輸出 N'100719' 而非 100719；
                // 轉不動時退回原始值，維持既有字串比對行為。
                var value = TryConvert(rawValue, meta.ClrType, out var normalizedValue) ? normalizedValue : rawValue;
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
