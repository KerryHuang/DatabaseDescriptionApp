using System.Data;

namespace Specurai.Domain.Entities;

/// <summary>
/// Dry Run 陳述式類型
/// </summary>
public enum DryRunStatementType
{
    Unknown,
    Insert,
    Update,
    Delete
}

/// <summary>
/// Dry Run 語法錯誤明細
/// </summary>
public class DryRunSyntaxError
{
    /// <summary>錯誤所在行（1 起算）</summary>
    public required int Line { get; init; }

    /// <summary>錯誤所在列（1 起算）</summary>
    public required int Column { get; init; }

    /// <summary>錯誤訊息</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Dry Run 預演結果（永遠回滾，不會修改資料）
/// </summary>
public class DryRunResult
{
    /// <summary>語法與分類驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>陳述式類型</summary>
    public DryRunStatementType StatementType { get; init; } = DryRunStatementType.Unknown;

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（多語句、非 DML 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>影響筆數</summary>
    public int AffectedRowCount { get; init; }

    /// <summary>前後資料對照（無法提供時為 null，如 trigger fallback）</summary>
    public DataTable? PreviewTable { get; init; }

    /// <summary>預覽是否被截斷（影響筆數超過預覽上限 100 筆）</summary>
    public bool PreviewTruncated { get; init; }

    /// <summary>警告清單（IDENTITY 消耗、trigger fallback 等）</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>語法正確但實際執行會失敗時的錯誤訊息（如違反 FK 約束）</summary>
    public string? ExecutionError { get; init; }

    /// <summary>是否已 COMMIT 寫入資料庫（dry run 一律 false）</summary>
    public bool Committed { get; init; }
}
