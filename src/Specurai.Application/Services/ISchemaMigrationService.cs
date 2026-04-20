using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 分析協調服務介面
/// </summary>
public interface ISchemaMigrationService
{
    /// <summary>
    /// 分析基準與目標資料庫的 Schema 差異並進行風險分類
    /// </summary>
    Task<MigrationAnalysis> AnalyzeAsync(
        string baseConnectionString,
        string targetConnectionString,
        string baseEnvName,
        string targetEnvName,
        CancellationToken ct = default);
}
