using System.Text;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Application.Services;

/// <summary>
/// T-SQL Migration 腳本產生器
/// </summary>
public class SqlScriptGenerator : ISqlScriptGenerator
{
    public SyncScript Generate(
        IList<SchemaDifference> selectedDifferences,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName)
    {
        // 預先建立程式物件查找表，避免 O(n×m) 線性掃描
        var programObjectLookup = BuildProgramObjectLookup(baseSchema);

        var sb = new StringBuilder();
        AppendHeader(sb, baseEnvName, targetEnvName);
        AppendDiffStatements(sb, selectedDifferences, baseSchema, programObjectLookup);

        sb.AppendLine("    COMMIT TRANSACTION;");
        sb.AppendLine("    PRINT N'Migration 成功完成';");
        sb.AppendLine();
        sb.AppendLine("END TRY");
        sb.AppendLine("BEGIN CATCH");
        sb.AppendLine("    ROLLBACK TRANSACTION;");
        sb.AppendLine("    PRINT N'發生錯誤，已自動回滾：' + ERROR_MESSAGE();");
        sb.AppendLine("    THROW;");
        sb.AppendLine("END CATCH;");

        var applyScript = sb.ToString();

        // Dry Run 腳本：同樣結構但強制 ROLLBACK，不需外層包 ADO.NET transaction
        var drySb = new StringBuilder();
        AppendHeader(drySb, baseEnvName, targetEnvName);
        AppendDiffStatements(drySb, selectedDifferences, baseSchema, programObjectLookup);
        drySb.AppendLine("    ROLLBACK TRANSACTION;");
        drySb.AppendLine("    PRINT N'[Dry Run] 腳本驗證通過，已強制回滾，資料庫無實際變更';");
        drySb.AppendLine();
        drySb.AppendLine("END TRY");
        drySb.AppendLine("BEGIN CATCH");
        drySb.AppendLine("    ROLLBACK TRANSACTION;");
        drySb.AppendLine("    PRINT N'[Dry Run] 腳本驗證失敗：' + ERROR_MESSAGE();");
        drySb.AppendLine("    THROW;");
        drySb.AppendLine("END CATCH;");

        return new SyncScript
        {
            TargetEnvironment = targetEnvName,
            GeneratedAt = DateTime.Now,
            ApplyScript = applyScript,
            DryRunScript = drySb.ToString(),
            Differences = selectedDifferences
        };
    }

    private static void AppendHeader(StringBuilder sb, string baseEnvName, string targetEnvName)
    {
        sb.AppendLine("-- ================================================");
        sb.AppendLine("-- Schema Migration Script");
        sb.AppendLine($"-- 基準環境：{baseEnvName}");
        sb.AppendLine($"-- 目標環境：{targetEnvName}");
        sb.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- ================================================");
        sb.AppendLine();
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine("BEGIN TRY");
        sb.AppendLine();
    }

    private static void AppendDiffStatements(
        StringBuilder sb,
        IList<SchemaDifference> diffs,
        DatabaseSchema baseSchema,
        Dictionary<(string Schema, string Name, SchemaObjectType Type), SchemaProgramObject> programObjectLookup)
    {
        foreach (var diff in diffs)
        {
            var sql = GenerateSqlForDifference(diff, baseSchema, programObjectLookup);
            if (!string.IsNullOrWhiteSpace(sql))
            {
                sb.AppendLine($"    -- [{RiskLevelText(diff.RiskLevel)}] {diff.Description ?? diff.ObjectName}");
                sb.AppendLine($"    {sql}");
                sb.AppendLine();
            }
        }
    }

    private static Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> BuildProgramObjectLookup(
        DatabaseSchema baseSchema)
    {
        var lookup = new Dictionary<(string, string, SchemaObjectType), SchemaProgramObject>(
            StringComparer.OrdinalIgnoreCase.GetHashCode() == 0
                ? EqualityComparer<(string, string, SchemaObjectType)>.Default
                : new TupleIgnoreCaseComparer());

        foreach (var v in baseSchema.Views)
            lookup.TryAdd((v.Schema, v.Name, SchemaObjectType.View), v);
        foreach (var p in baseSchema.StoredProcedures)
            lookup.TryAdd((p.Schema, p.Name, SchemaObjectType.StoredProcedure), p);
        foreach (var f in baseSchema.Functions)
            lookup.TryAdd((f.Schema, f.Name, SchemaObjectType.Function), f);
        foreach (var t in baseSchema.Triggers)
            lookup.TryAdd((t.Schema, t.Name, SchemaObjectType.Trigger), t);

        return lookup;
    }

    private static string GenerateSqlForDifference(
        SchemaDifference diff,
        DatabaseSchema baseSchema,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programObjectLookup)
    {
        return diff.ObjectType switch
        {
            SchemaObjectType.Table => GenerateTableSql(diff, baseSchema),
            SchemaObjectType.Column => GenerateColumnSql(diff, baseSchema),
            SchemaObjectType.Index => GenerateIndexSql(diff, baseSchema),
            SchemaObjectType.Constraint => GenerateConstraintSql(diff, baseSchema),
            SchemaObjectType.View or SchemaObjectType.StoredProcedure
                or SchemaObjectType.Function or SchemaObjectType.Trigger
                => GenerateProgramObjectSql(diff, programObjectLookup),
            _ => string.Empty
        };
    }

    private static string GenerateTableSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (_, tableName) = ParseTwoParts(diff.ObjectName);
        var schema = diff.Schema;
        var table = baseSchema.GetTable(schema, tableName);
        if (table == null) return $"-- 無法找到表格定義：{diff.ObjectName}";

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{table.Schema}].[{table.Name}] (");

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var identity = col.IsIdentity ? " IDENTITY(1,1)" : string.Empty;
            var defaultVal = string.IsNullOrEmpty(col.DefaultValue) ? string.Empty : $" DEFAULT {col.DefaultValue}";
            var dataType = col.GetFullDataType();
            var collation = string.IsNullOrEmpty(col.Collation) ? string.Empty : $" COLLATE {col.Collation}";
            sb.Append($"    [{col.Name}] {dataType}{collation}{identity} {nullable}{defaultVal}");
            if (i < table.Columns.Count - 1) sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(");");
        return sb.ToString();
    }

    private static string GenerateColumnSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        var (_, tableName, columnName) = ParseThreeParts(diff.ObjectName);
        var schema = diff.Schema;
        var table = baseSchema.GetTable(schema, tableName);

        if (diff.DifferenceType == DifferenceType.Added)
        {
            var col = table?.GetColumn(columnName);
            if (col == null) return $"-- 無法找到欄位定義：{diff.ObjectName}";

            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var defaultVal = string.IsNullOrEmpty(col.DefaultValue) ? string.Empty : $" DEFAULT {col.DefaultValue}";
            return $"ALTER TABLE [{schema}].[{tableName}] ADD [{col.Name}] {col.GetFullDataType()} {nullable}{defaultVal};";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            var col = table?.GetColumn(columnName);
            if (col == null) return $"-- 無法找到欄位定義：{diff.ObjectName}";

            var newLength = int.TryParse(diff.SourceValue, out var len) ? len : col.MaxLength;
            var dataType = newLength.HasValue ? $"{col.DataType}({newLength})" : col.DataType;
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            return $"ALTER TABLE [{schema}].[{tableName}] ALTER COLUMN [{columnName}] {dataType} {nullable};";
        }

        return string.Empty;
    }

    private static string GenerateIndexSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (_, tableName, indexName) = ParseThreeParts(diff.ObjectName);
        var schema = diff.Schema;
        var table = baseSchema.GetTable(schema, tableName);
        var index = table?.Indexes.FirstOrDefault(i =>
            i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase));

        if (index == null) return $"-- 無法找到索引定義：{diff.ObjectName}";

        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var clustered = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
        var columns = string.Join(", ", index.Columns.Select(c => $"[{c}]"));
        var include = index.IncludeColumns.Count > 0
            ? $" INCLUDE ({string.Join(", ", index.IncludeColumns.Select(c => $"[{c}]"))})"
            : string.Empty;
        var filter = string.IsNullOrEmpty(index.FilterDefinition) ? string.Empty : $" WHERE {index.FilterDefinition}";

        return $"CREATE {unique}{clustered}INDEX [{index.Name}] ON [{schema}].[{tableName}] ({columns}){include}{filter};";
    }

    private static string GenerateConstraintSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (_, tableName, constraintName) = ParseThreeParts(diff.ObjectName);
        var schema = diff.Schema;
        var table = baseSchema.GetTable(schema, tableName);
        var constraint = table?.Constraints.FirstOrDefault(c =>
            c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase));

        if (constraint == null) return $"-- 無法找到約束定義：{diff.ObjectName}";

        return constraint.ConstraintType switch
        {
            ConstraintType.Unique =>
                $"ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [{constraint.Name}] UNIQUE ({string.Join(", ", constraint.Columns.Select(c => $"[{c}]"))});",
            ConstraintType.Default =>
                $"ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [{constraint.Name}] DEFAULT {constraint.Definition} FOR [{constraint.Columns.FirstOrDefault()}];",
            _ => $"-- 不支援自動產生此約束類型：{constraint.ConstraintType}"
        };
    }

    private static string GenerateProgramObjectSql(
        SchemaDifference diff,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> lookup)
    {
        var (_, objName) = ParseTwoParts(diff.ObjectName);
        var schema = diff.Schema;

        if (!lookup.TryGetValue((schema, objName, diff.ObjectType), out var obj) || obj.Definition == null)
            return $"-- 無法找到物件定義：{diff.ObjectName}";

        if (diff.DifferenceType == DifferenceType.Added)
        {
            var def = obj.Definition.Trim();
            if (def.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase))
                def = "CREATE " + def[6..];
            return def + ";";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            var def = obj.Definition.Trim();
            if (def.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase))
                def = "ALTER " + def[7..];
            return def + ";";
        }

        return string.Empty;
    }

    private static (string schema, string name) ParseTwoParts(string objectName)
    {
        var clean = objectName.Replace("[", "").Replace("]", "");
        var parts = clean.Split('.');
        return parts.Length >= 2 ? (parts[0], parts[1]) : ("dbo", clean);
    }

    private static (string schema, string table, string column) ParseThreeParts(string objectName)
    {
        var clean = objectName.Replace("[", "").Replace("]", "");
        var parts = clean.Split('.');
        return parts.Length >= 3
            ? (parts[0], parts[1], parts[2])
            : ("dbo", parts.Length >= 2 ? parts[0] : string.Empty, parts[^1]);
    }

    private static string RiskLevelText(RiskLevel level) => level switch
    {
        RiskLevel.Low => "低風險",
        RiskLevel.Medium => "中風險",
        _ => "未知"
    };

    private sealed class TupleIgnoreCaseComparer : IEqualityComparer<(string, string, SchemaObjectType)>
    {
        public bool Equals((string, string, SchemaObjectType) x, (string, string, SchemaObjectType) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase) &&
            x.Item3 == y.Item3;

        public int GetHashCode((string, string, SchemaObjectType) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2),
                obj.Item3);
    }
}
