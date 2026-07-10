using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL Dry Run 分析結果（純離線解析，不碰資料庫）
/// </summary>
public class SqlDryRunAnalysis
{
    /// <summary>語法與分類驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>陳述式類型</summary>
    public DryRunStatementType StatementType { get; init; } = DryRunStatementType.Unknown;

    /// <summary>語法錯誤明細</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（多語句、非 DML 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>目標資料表 Schema（無法解析時為 null）</summary>
    public string? TargetSchema { get; init; }

    /// <summary>目標資料表名稱（無法解析時為 null，如 CTE 目標）</summary>
    public string? TargetTable { get; init; }

    /// <summary>使用者是否已自帶 OUTPUT 子句（不含 OUTPUT INTO）</summary>
    public bool HasUserOutputClause { get; init; }

    /// <summary>使用者是否已自帶 OUTPUT INTO 子句（結果寫入目標表，不回傳結果集）</summary>
    public bool HasUserOutputIntoClause { get; init; }
}
