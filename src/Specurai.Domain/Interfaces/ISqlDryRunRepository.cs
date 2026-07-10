using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL Dry Run Repository 介面：預演單一 DML（INSERT/UPDATE/DELETE），
/// 在交易中執行以取得影響筆數與前後資料對照，最後一律 ROLLBACK，絕不修改資料。
/// </summary>
public interface ISqlDryRunRepository
{
    /// <summary>
    /// 使用預設連線預演單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> DryRunAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 使用指定連線字串預演單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default);
}
