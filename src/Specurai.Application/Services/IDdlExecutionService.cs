using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// DDL 執行服務：環境閘門與 confirm 分流的唯一所在。
/// Production 連線一律拒絕（不連資料庫）；
/// confirm=false 走交易內預演（一律回滾）、confirm=true 走實際執行（COMMIT）。
/// </summary>
public interface IDdlExecutionService
{
    /// <summary>
    /// 執行 DDL script（白名單物件級 DDL，可含多句與 GO）
    /// </summary>
    /// <param name="script">DDL script</param>
    /// <param name="confirm">false 僅預演；true 實際執行並 COMMIT</param>
    /// <param name="profileId">目標連線設定檔（null 表示目前連線，跟隨資料庫覆寫）</param>
    Task<DdlExecutionResult> ExecuteAsync(
        string script, bool confirm, Guid? profileId = null, CancellationToken ct = default);
}
