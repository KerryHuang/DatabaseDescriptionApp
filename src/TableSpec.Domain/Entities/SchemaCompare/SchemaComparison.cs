using TableSpec.Domain.Enums;

namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 風險摘要
/// </summary>
public class RiskSummary
{
    /// <summary>
    /// 低風險數量
    /// </summary>
    public int LowCount { get; set; }

    /// <summary>
    /// 中風險數量
    /// </summary>
    public int MediumCount { get; set; }

    /// <summary>
    /// 高風險數量
    /// </summary>
    public int HighCount { get; set; }

    /// <summary>
    /// 禁止數量
    /// </summary>
    public int ForbiddenCount { get; set; }

    /// <summary>
    /// 總數
    /// </summary>
    public int TotalCount => LowCount + MediumCount + HighCount + ForbiddenCount;
}

/// <summary>
/// 比對結果
/// </summary>
public class SchemaComparison
{
    /// <summary>
    /// 基準環境名稱
    /// </summary>
    public string BaseEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 目標環境名稱
    /// </summary>
    public string TargetEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 比對時間
    /// </summary>
    public DateTime ComparedAt { get; set; }

    /// <summary>
    /// 差異清單
    /// </summary>
    public IList<SchemaDifference> Differences { get; set; } = new List<SchemaDifference>();

    /// <summary>
    /// 是否有差異
    /// </summary>
    public bool HasDifferences => Differences.Count > 0;

    /// <summary>
    /// 風險摘要
    /// </summary>
    public RiskSummary RiskSummary
    {
        get
        {
            return new RiskSummary
            {
                LowCount = Differences.Count(d => d.RiskLevel == RiskLevel.Low),
                MediumCount = Differences.Count(d => d.RiskLevel == RiskLevel.Medium),
                HighCount = Differences.Count(d => d.RiskLevel == RiskLevel.High),
                ForbiddenCount = Differences.Count(d => d.RiskLevel == RiskLevel.Forbidden)
            };
        }
    }

    /// <summary>
    /// 根據風險等級取得差異
    /// </summary>
    public IEnumerable<SchemaDifference> GetDifferencesByRiskLevel(RiskLevel riskLevel)
    {
        return Differences.Where(d => d.RiskLevel == riskLevel);
    }

    /// <summary>
    /// 根據物件類型取得差異
    /// </summary>
    public IEnumerable<SchemaDifference> GetDifferencesByObjectType(SchemaObjectType objectType)
    {
        return Differences.Where(d => d.ObjectType == objectType);
    }

    /// <summary>
    /// 取得可執行的差異（Low 和 Medium）
    /// </summary>
    public IEnumerable<SchemaDifference> GetExecutableDifferences()
    {
        return Differences.Where(d => d.CanExecuteDirectly);
    }

    /// <summary>
    /// 取得只能匯出腳本的差異（High 和 Forbidden）
    /// </summary>
    public IEnumerable<SchemaDifference> GetScriptOnlyDifferences()
    {
        return Differences.Where(d => !d.CanExecuteDirectly);
    }
}
