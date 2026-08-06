using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// DDL script 離線分析結果：驗證通過時附逐句摘要與 GO 批次切分
/// </summary>
public class SqlDdlScriptAnalysis
{
    /// <summary>語法與白名單驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（非白名單語句、空 script）</summary>
    public string? RejectReason { get; init; }

    /// <summary>逐句摘要</summary>
    public IReadOnlyList<DdlStatementSummary> Statements { get; init; } = [];

    /// <summary>依 GO 切分的可執行批次文字（依原始順序）</summary>
    public IReadOnlyList<string> Batches { get; init; } = [];
}
