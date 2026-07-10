using System.Data;

namespace Specurai.Domain.Entities;

/// <summary>
/// 查詢結果欄位的來源中繼資料（由 CommandBehavior.KeyInfo 取得）
/// </summary>
public class QueryColumnMetadata
{
    /// <summary>結果欄位名稱</summary>
    public required string ColumnName { get; init; }

    /// <summary>來源 Schema 名稱（無來源時為 null）</summary>
    public string? BaseSchema { get; init; }

    /// <summary>來源資料表名稱（運算式欄位為 null）</summary>
    public string? BaseTable { get; init; }

    /// <summary>來源欄位名稱（運算式欄位為 null）</summary>
    public string? BaseColumn { get; init; }

    /// <summary>是否為主鍵欄位</summary>
    public bool IsKey { get; init; }

    /// <summary>是否唯讀（identity、timestamp/rowversion、運算式欄位）</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>欄位 CLR 型別（產生 SQL 字面值與編輯值轉型用）</summary>
    public required Type ClrType { get; init; }
}

/// <summary>
/// 含 Schema 中繼資料的查詢結果
/// </summary>
public class QueryResultWithSchema
{
    /// <summary>查詢結果資料</summary>
    public required DataTable Table { get; init; }

    /// <summary>各欄位的來源中繼資料（與結果欄位順序一致）</summary>
    public IReadOnlyList<QueryColumnMetadata> Columns { get; init; } = [];

    /// <summary>唯一來源表的 Schema（非單表時為 null）</summary>
    public string? TargetSchema => DistinctBaseTables() is [var only] ? only.Schema : null;

    /// <summary>唯一來源表名稱（非單表時為 null）</summary>
    public string? TargetTable => DistinctBaseTables() is [var only] ? only.Table : null;

    /// <summary>結果是否來自單一資料表（可編輯的前提）</summary>
    public bool IsSingleTable => TargetTable != null;

    private List<(string? Schema, string Table)> DistinctBaseTables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string?, string)>();
        foreach (var column in Columns)
        {
            if (string.IsNullOrEmpty(column.BaseTable)) continue;
            if (seen.Add($"{column.BaseSchema}::{column.BaseTable}"))
                result.Add((column.BaseSchema, column.BaseTable!));
        }
        return result;
    }
}
