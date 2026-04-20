using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 執行器介面
/// </summary>
public interface ISchemaMigrationExecutor
{
    /// <summary>
    /// 執行 Migration 腳本並回傳執行報告
    /// </summary>
    /// <param name="script">要執行的同步腳本</param>
    /// <param name="targetConnectionString">目標資料庫連線字串</param>
    /// <param name="ct">取消權杖</param>
    Task<MigrationReport> ExecuteAsync(
        SyncScript script,
        string targetConnectionString,
        bool dryRun = false,
        CancellationToken ct = default);
}
