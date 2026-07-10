using Specurai.Domain.Entities;

namespace Specurai.Application.Models;

/// <summary>
/// 產生異動 SQL 的請求
/// </summary>
public class UpdateSqlRequest
{
    /// <summary>目標資料表 Schema（可為 null）</summary>
    public string? TargetSchema { get; init; }

    /// <summary>目標資料表名稱</summary>
    public required string TargetTable { get; init; }

    /// <summary>結果欄位中繼資料</summary>
    public required IReadOnlyList<QueryColumnMetadata> Columns { get; init; }

    /// <summary>WHERE 定位欄位（結果欄位名稱）</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>定位欄位是否為「全欄位 fallback」（無主鍵且使用者未挑選）</summary>
    public bool IsFallbackKeys { get; init; }

    /// <summary>原值/現值列（順序對應）</summary>
    public required IReadOnlyList<UpdateSqlRow> Rows { get; init; }
}

/// <summary>
/// 一列資料的原值與現值
/// </summary>
public class UpdateSqlRow
{
    public required IReadOnlyDictionary<string, object?> Original { get; init; }
    public required IReadOnlyDictionary<string, object?> Current { get; init; }
}

/// <summary>
/// 產生異動 SQL 的結果
/// </summary>
public class UpdateSqlResult
{
    /// <summary>產生的 UPDATE 語句全文（無異動時為空字串）</summary>
    public string Sql { get; init; } = string.Empty;

    /// <summary>UPDATE 語句數量</summary>
    public int StatementCount { get; init; }

    /// <summary>警告（轉型失敗跳過的列等）</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
