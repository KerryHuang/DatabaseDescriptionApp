using System.Text;
using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Schema Migration 分析結果
/// </summary>
public class MigrationAnalysis
{
    public required DatabaseSchema BaseSchema { get; init; }
    public required DatabaseSchema TargetSchema { get; init; }
    public required SchemaComparison Comparison { get; init; }

    public IReadOnlyList<SchemaDifference> BlockedDifferences =>
        Comparison.Differences.Where(d => d.RiskLevel >= RiskLevel.High).ToList();

    public IReadOnlyList<SchemaDifference> WarnDifferences =>
        Comparison.Differences.Where(d => d.RiskLevel == RiskLevel.Medium).ToList();

    public IReadOnlyList<SchemaDifference> SafeDifferences =>
        Comparison.Differences.Where(d => d.RiskLevel == RiskLevel.Low).ToList();

    /// <summary>
    /// 產生完整的文字建議報告
    /// </summary>
    public string GenerateReport()
    {
        var diffs = Comparison.Differences;
        var sb = new StringBuilder();
        var line = new string('═', 52);
        var thin = new string('─', 52);

        sb.AppendLine("📊 Schema 差異分析報告");
        sb.AppendLine($"   基準環境：{BaseSchema.ConnectionName}（{BaseSchema.DatabaseName}）");
        sb.AppendLine($"   目標環境：{TargetSchema.ConnectionName}（{TargetSchema.DatabaseName}）");
        sb.AppendLine($"   分析時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(line);
        sb.AppendLine();

        // 摘要統計
        sb.AppendLine("📈 差異摘要");
        sb.AppendLine($"   總計差異：{diffs.Count} 項");
        sb.AppendLine($"   🟢 低風險（可自動執行）：{SafeDifferences.Count} 項");
        sb.AppendLine($"   🟡 中風險（需人工確認）：{WarnDifferences.Count} 項");
        sb.AppendLine($"   🔴 高風險 / 禁止（不可自動執行）：{BlockedDifferences.Count} 項");
        sb.AppendLine();

        // 物件類型分佈
        sb.AppendLine("📦 各物件類型分佈");
        var byType = diffs.GroupBy(d => d.ObjectType)
            .OrderBy(g => (int)g.Key);
        foreach (var group in byType)
        {
            var typeText = ObjectTypeText(group.Key);
            var addedCount = group.Count(d => d.DifferenceType == DifferenceType.Added);
            var modCount = group.Count(d => d.DifferenceType == DifferenceType.Modified);
            var parts = new List<string>();
            if (addedCount > 0) parts.Add($"新增 {addedCount}");
            if (modCount > 0) parts.Add($"修改 {modCount}");
            sb.AppendLine($"   {typeText}：{group.Count()} 項（{string.Join("、", parts)}）");
        }
        sb.AppendLine();

        // 執行建議順序
        sb.AppendLine("💡 建議執行順序");
        var executableOrder = new[]
        {
            SchemaObjectType.Table, SchemaObjectType.Column, SchemaObjectType.Constraint,
            SchemaObjectType.Index, SchemaObjectType.View,
            SchemaObjectType.StoredProcedure, SchemaObjectType.Function, SchemaObjectType.Trigger
        };
        var step = 1;
        foreach (var objType in executableOrder)
        {
            var items = SafeDifferences.Concat(WarnDifferences)
                .Where(d => d.ObjectType == objType).ToList();
            if (items.Count == 0) continue;
            sb.AppendLine($"   {step++}. {ObjectTypeText(objType)}（{items.Count} 項）");
        }
        if (BlockedDifferences.Count > 0)
            sb.AppendLine($"   ⚠️  以下 {BlockedDifferences.Count} 項高風險需手動處理（見下方清單）");
        sb.AppendLine();

        // 中風險注意事項
        if (WarnDifferences.Count > 0)
        {
            sb.AppendLine("🟡 需人工確認（中風險）");
            sb.AppendLine(thin);
            foreach (var d in WarnDifferences)
                sb.AppendLine($"   [{ObjectTypeText(d.ObjectType)}] {d.ObjectName}");
            sb.AppendLine($"   → 建議在非尖峰時段執行，並先備份目標資料庫");
            sb.AppendLine();
        }

        // 高風險 / 禁止
        if (BlockedDifferences.Count > 0)
        {
            sb.AppendLine("🔴 禁止自動執行（需手動處理）");
            sb.AppendLine(thin);
            foreach (var d in BlockedDifferences)
            {
                var risk = d.RiskLevel == RiskLevel.Forbidden ? "禁止" : "高風險";
                sb.AppendLine($"   [{risk}] [{ObjectTypeText(d.ObjectType)}] {d.ObjectName}");
                if (!string.IsNullOrEmpty(d.Description))
                    sb.AppendLine($"          原因：{d.Description}");
            }
            sb.AppendLine($"   → 請評估影響後手動執行，或聯繫 DBA 協助處理");
            sb.AppendLine();
        }

        // 結論
        sb.AppendLine(line);
        if (diffs.Count == 0)
            sb.AppendLine("✅ 兩個資料庫 Schema 完全一致，無需執行 Migration。");
        else if (BlockedDifferences.Count == 0 && WarnDifferences.Count == 0)
            sb.AppendLine("✅ 所有差異均為低風險，可安全自動執行 Migration。");
        else if (BlockedDifferences.Count == 0)
            sb.AppendLine($"⚠️  含 {WarnDifferences.Count} 項中風險差異，請確認後再執行 Migration。");
        else
            sb.AppendLine($"🔴 含 {BlockedDifferences.Count} 項高風險差異，請手動處理後再執行其餘 Migration。");

        return sb.ToString();
    }

    private static string ObjectTypeText(SchemaObjectType type) => type switch
    {
        SchemaObjectType.Table => "表格",
        SchemaObjectType.Column => "欄位",
        SchemaObjectType.Index => "索引",
        SchemaObjectType.Constraint => "約束",
        SchemaObjectType.View => "檢視表",
        SchemaObjectType.StoredProcedure => "預存程序",
        SchemaObjectType.Function => "函數",
        SchemaObjectType.Trigger => "觸發程序",
        _ => type.ToString()
    };
}
