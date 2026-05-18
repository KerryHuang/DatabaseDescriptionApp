using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 分析協調服務實作
/// </summary>
public class SchemaMigrationService : ISchemaMigrationService
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _schemaCompareService;

    public SchemaMigrationService(
        ISchemaCollector schemaCollector,
        ISchemaCompareService schemaCompareService)
    {
        _schemaCollector = schemaCollector;
        _schemaCompareService = schemaCompareService;
    }

    public async Task<MigrationAnalysis> AnalyzeAsync(
        string baseConnectionString,
        string targetConnectionString,
        string baseEnvName,
        string targetEnvName,
        CancellationToken ct = default)
    {
        var baseTask = _schemaCollector.CollectAsync(baseConnectionString, baseEnvName, ct);
        var targetTask = _schemaCollector.CollectAsync(targetConnectionString, targetEnvName, ct);
        await Task.WhenAll(baseTask, targetTask);

        var baseSchema = await baseTask;
        var targetSchema = await targetTask;
        var comparison = await _schemaCompareService.CompareAsync(baseSchema, targetSchema);

        // Migration 僅關注「基準 → 目標」方向的差異，過濾掉「目標多出來、基準才需新增」的反向差異
        comparison.Differences = comparison.Differences
            .Where(d => !(d.Description?.Contains("不存在於基準環境") ?? false))
            .ToList();

        // 標記每筆差異對應的目標表列數，並依列數+操作類型調整風險
        var targetRowCounts = targetSchema.Tables.ToDictionary(
            t => $"[{t.Schema}].[{t.Name}]",
            t => t.RowCount,
            StringComparer.OrdinalIgnoreCase);
        foreach (var diff in comparison.Differences)
        {
            diff.TargetTableRowCount = ResolveTargetRowCount(diff, targetRowCounts);
            AdjustRiskByRowCount(diff);
        }

        return new MigrationAnalysis
        {
            BaseSchema = baseSchema,
            TargetSchema = targetSchema,
            Comparison = comparison
        };
    }

    private const long MediumRowThreshold = 100_000;
    private const long HighRowThreshold = 1_000_000;

    /// <summary>
    /// 從 ObjectName 解析所屬資料表，回傳目標庫該表列數；程式物件回傳 null。
    /// </summary>
    private static long? ResolveTargetRowCount(
        SchemaDifference diff,
        IReadOnlyDictionary<string, long> targetTableLookup)
    {
        if (diff.ObjectType is SchemaObjectType.View
            or SchemaObjectType.StoredProcedure
            or SchemaObjectType.Function
            or SchemaObjectType.Trigger)
            return null;

        var name = diff.ObjectName;
        if (string.IsNullOrEmpty(name)) return null;

        var firstDot = name.IndexOf("].[", StringComparison.Ordinal);
        if (firstDot < 0) return null;
        var secondDot = name.IndexOf("].[", firstDot + 3, StringComparison.Ordinal);
        var tableFull = secondDot < 0 ? name : name.Substring(0, secondDot + 1);

        return targetTableLookup.TryGetValue(tableFull, out var rc) ? rc : null;
    }

    /// <summary>
    /// 依目標表列數+操作類型升高風險：
    ///   - 涉及全表改寫 / 索引重建 / NOT NULL 補值的操作，在大表上會耗時，需提高風險等級。
    ///   - 純 metadata 操作（拉長 varchar、ADD NULL 欄位）不調整。
    /// </summary>
    private static void AdjustRiskByRowCount(SchemaDifference diff)
    {
        // ── 絕對規則：ALTER COLUMN 縮長度 / 改型別 / 改 NOT NULL → 一律高風險 ──
        // 這些操作會全表改寫且可能因資料截斷或既有 NULL 值而失敗。
        // Dry Run 與 Migration 自動執行皆排除這些項目，只能透過「預覽 SQL / 下載 SQL」由人工處理。
        if (diff.ObjectType == SchemaObjectType.Column
            && diff.DifferenceType == DifferenceType.Modified)
        {
            var forceHigh =
                string.Equals(diff.PropertyName, "DataType", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(diff.PropertyName, "MaxLength", StringComparison.OrdinalIgnoreCase) && IsMaxLengthShrink(diff))
                || (string.Equals(diff.PropertyName, "IsNullable", StringComparison.OrdinalIgnoreCase) && IsTighteningToNotNull(diff));
            if (forceHigh)
            {
                diff.RiskLevel = RiskLevel.High;
                diff.Description = AppendNote(diff.Description, "ALTER COLUMN 縮長度/改型別/改 NOT NULL 需人工處理，自動執行不執行此項");
                return;
            }
        }

        if (diff.TargetTableRowCount is not long rows || rows < MediumRowThreshold) return;
        var bumpToHigh = rows >= HighRowThreshold;

        switch (diff.ObjectType)
        {
            case SchemaObjectType.Column when diff.DifferenceType == DifferenceType.Modified:
                // 縮短/型別變更已經是 High；這裡主要針對 IsNullable 變更與其他可能 Low/Medium 的情況
                if (string.Equals(diff.PropertyName, "IsNullable", StringComparison.OrdinalIgnoreCase))
                {
                    diff.RiskLevel = bumpToHigh ? RiskLevel.High : MaxRisk(diff.RiskLevel, RiskLevel.Medium);
                }
                else if (string.Equals(diff.PropertyName, "MaxLength", StringComparison.OrdinalIgnoreCase)
                         && diff.RiskLevel == RiskLevel.Low)
                {
                    // 拉長 varchar 雖然是 metadata-only，但會觸發相依索引 DROP/RECREATE
                    diff.RiskLevel = bumpToHigh ? RiskLevel.High : RiskLevel.Medium;
                }
                break;

            case SchemaObjectType.Column when diff.DifferenceType == DifferenceType.Added:
                // ADD COLUMN：NULL 是 metadata；NOT NULL+default（Medium）或 NOT NULL（High）在大表會更慢
                if (diff.RiskLevel == RiskLevel.Medium && bumpToHigh)
                    diff.RiskLevel = RiskLevel.High;
                break;

            case SchemaObjectType.Index when diff.DifferenceType == DifferenceType.Added:
                diff.RiskLevel = bumpToHigh ? RiskLevel.High : MaxRisk(diff.RiskLevel, RiskLevel.Medium);
                break;

            case SchemaObjectType.Constraint when diff.DifferenceType == DifferenceType.Added:
                // CHECK / FK 在 ADD 時會掃描全表
                diff.RiskLevel = bumpToHigh ? RiskLevel.High : MaxRisk(diff.RiskLevel, RiskLevel.Medium);
                break;
        }

        // 補充風險描述
        if (diff.RiskLevel >= RiskLevel.Medium && !string.IsNullOrEmpty(diff.Description))
            diff.Description += $"（目標表約 {rows:N0} 列，預估耗時較長）";
    }

    private static RiskLevel MaxRisk(RiskLevel a, RiskLevel b) => (int)a > (int)b ? a : b;

    private static bool IsMaxLengthShrink(SchemaDifference diff)
    {
        // SourceValue=基準(目標應變成)，TargetValue=目標(目前)
        // base < target → 縮短
        return int.TryParse(diff.SourceValue, out var b)
            && int.TryParse(diff.TargetValue, out var t)
            && b < t;
    }

    private static bool IsTighteningToNotNull(SchemaDifference diff)
    {
        // SourceValue 為基準的 IsNullable.ToString()；"False" 代表基準要求 NOT NULL，目標需要由 NULL 收緊為 NOT NULL
        return string.Equals(diff.SourceValue, "False", StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendNote(string? desc, string note) =>
        string.IsNullOrEmpty(desc) ? note : $"{desc}（{note}）";
}
