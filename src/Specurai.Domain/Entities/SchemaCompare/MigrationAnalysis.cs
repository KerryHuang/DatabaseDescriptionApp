using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Schema Migration 分析結果
/// </summary>
public class MigrationAnalysis
{
    /// <summary>
    /// 基準 DatabaseSchema（用於產生 SQL）
    /// </summary>
    public required DatabaseSchema BaseSchema { get; init; }

    /// <summary>
    /// 目標 DatabaseSchema
    /// </summary>
    public required DatabaseSchema TargetSchema { get; init; }

    /// <summary>
    /// 完整比對結果
    /// </summary>
    public required SchemaComparison Comparison { get; init; }

    /// <summary>
    /// 高/禁止風險差異（不可執行，僅顯示報告）
    /// </summary>
    public IReadOnlyList<SchemaDifference> BlockedDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel >= RiskLevel.High)
            .ToList();

    /// <summary>
    /// 中風險差異（需使用者確認才執行）
    /// </summary>
    public IReadOnlyList<SchemaDifference> WarnDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel == RiskLevel.Medium)
            .ToList();

    /// <summary>
    /// 低風險差異（預設勾選）
    /// </summary>
    public IReadOnlyList<SchemaDifference> SafeDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel == RiskLevel.Low)
            .ToList();
}
