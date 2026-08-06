using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL DDL 執行 Repository 介面：驗證白名單物件級 DDL 批次後在單一交易中逐批執行，
/// commit=false 一律 ROLLBACK（預演）、commit=true 全部成功才 COMMIT。
/// 環境限制（Production 拒絕）由 Application 層的 IDdlExecutionService 把關，
/// 呼叫端不應繞過該服務直接使用本介面。
/// </summary>
public interface ISqlDdlExecuteRepository
{
    /// <summary>
    /// 使用指定連線字串執行 DDL script（可含多句與 GO）
    /// </summary>
    Task<DdlExecutionResult> ExecuteAsync(
        string script, string connectionString, bool commit, CancellationToken ct = default);
}
