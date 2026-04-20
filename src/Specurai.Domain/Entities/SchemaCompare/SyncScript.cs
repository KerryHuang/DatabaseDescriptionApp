using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// 同步腳本
/// </summary>
public class SyncScript
{
    /// <summary>
    /// 目標環境名稱
    /// </summary>
    public string TargetEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 產生時間
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// 套用腳本
    /// </summary>
    public string ApplyScript { get; set; } = string.Empty;

    /// <summary>
    /// 回滾腳本
    /// </summary>
    public string? RollbackScript { get; set; }

    /// <summary>
    /// Dry Run 腳本（與 ApplyScript 相同但強制 ROLLBACK，不需外層包 ADO.NET transaction）
    /// </summary>
    public string DryRunScript { get; set; } = string.Empty;

    /// <summary>
    /// 相關的差異清單
    /// </summary>
    public IList<SchemaDifference> Differences { get; set; } = new List<SchemaDifference>();

    /// <summary>
    /// 是否有回滾腳本
    /// </summary>
    public bool HasRollbackScript => !string.IsNullOrWhiteSpace(RollbackScript);

    /// <summary>
    /// 最高風險等級
    /// </summary>
    public RiskLevel MaxRiskLevel
    {
        get
        {
            if (Differences.Count == 0)
                return RiskLevel.Low;

            return Differences.Max(d => d.RiskLevel);
        }
    }

    /// <summary>
    /// 是否可以執行（最高風險等級為 Low 或 Medium）
    /// </summary>
    public bool CanExecute => MaxRiskLevel == RiskLevel.Low || MaxRiskLevel == RiskLevel.Medium;
}
