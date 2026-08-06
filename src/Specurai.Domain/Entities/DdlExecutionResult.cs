namespace Specurai.Domain.Entities;

/// <summary>
/// DDL 逐句摘要：驗證通過後回報整批要動哪些物件
/// </summary>
public class DdlStatementSummary
{
    /// <summary>語句序號（全 script 連續，1 起算）</summary>
    public required int Index { get; init; }

    /// <summary>語句類型（如 CREATE TABLE、DROP INDEX）</summary>
    public required string Type { get; init; }

    /// <summary>目標物件名稱（無法解析時為 null，如 ALTER INDEX ALL）</summary>
    public string? ObjectName { get; init; }

    /// <summary>所屬 GO 批次（1 起算）</summary>
    public required int BatchIndex { get; init; }
}

/// <summary>
/// DDL 預演／執行結果：confirm=false 一律回滾；confirm=true 成功時 COMMIT（見 <see cref="Committed"/>）。
/// </summary>
public class DdlExecutionResult
{
    /// <summary>語法與白名單驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（非白名單語句、正式環境、空 script 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>逐句摘要（驗證通過後提供）</summary>
    public IReadOnlyList<DdlStatementSummary> Statements { get; init; } = [];

    /// <summary>語法正確但實際執行失敗時的錯誤訊息（整批已回滾）</summary>
    public string? ExecutionError { get; init; }

    /// <summary>執行失敗的 GO 批次索引（1 起算；SQL 錯誤訊息本身含行號可再定位）</summary>
    public int? FailedBatchIndex { get; init; }

    /// <summary>是否已 COMMIT 變更 schema（預演一律 false）</summary>
    public bool Committed { get; init; }

    /// <summary>COMMIT 失敗、交易結果不確定時為 true；預演與一般執行失敗（COMMIT 前即失敗）皆為 false</summary>
    public bool CommitUncertain { get; init; }
}
