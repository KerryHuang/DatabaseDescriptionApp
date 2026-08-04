using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL DML 執行 Repository 介面：實際執行單一 DML（INSERT/UPDATE/DELETE），
/// 在交易中執行並 COMMIT，回傳影響筆數與前後資料對照。
/// 環境限制（Production 拒絕）由 Application 層的 IDmlExecutionService 把關，
/// 呼叫端不應繞過該服務直接使用本介面。
/// </summary>
public interface ISqlDmlExecuteRepository
{
    /// <summary>
    /// 使用預設連線實際執行單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> ExecuteAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 使用指定連線字串實際執行單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> ExecuteAsync(string sql, string connectionString, CancellationToken ct = default);
}
