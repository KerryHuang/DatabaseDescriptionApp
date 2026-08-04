using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// DML 執行服務：環境閘門與 confirm 分流的唯一所在。
/// Production 連線一律拒絕（不連資料庫）；
/// confirm=false 走 dry run 預演（一律回滾）、confirm=true 走實際執行（COMMIT）。
/// </summary>
public interface IDmlExecutionService
{
    /// <summary>
    /// 執行單一 DML（INSERT/UPDATE/DELETE）
    /// </summary>
    /// <param name="sql">單一 DML 陳述式</param>
    /// <param name="confirm">false 僅預演；true 實際執行並 COMMIT</param>
    /// <param name="profileId">目標連線設定檔（null 表示目前連線，跟隨資料庫覆寫）</param>
    Task<DryRunResult> ExecuteAsync(string sql, bool confirm, Guid? profileId = null, CancellationToken ct = default);
}
