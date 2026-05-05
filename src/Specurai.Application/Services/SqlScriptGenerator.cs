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
        var ordered = OrderDiffs(diffs, programObjectLookup);
        foreach (var diff in ordered)
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

    /// <summary>
    /// 依物件類型與依賴關係排序差異清單：
    /// 資料表 → 欄位/索引/約束 → View（拓撲排序）→ StoredProcedure/Function/Trigger
    /// </summary>
    private static IEnumerable<SchemaDifference> OrderDiffs(
        IList<SchemaDifference> diffs,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programObjectLookup)
    {
        // 階段 1：資料表
        var tables = diffs.Where(d => d.ObjectType == SchemaObjectType.Table);
        // 階段 2a：新增欄位（必須在 ALTER COLUMN 之前，避免 RECREATE INDEX 時參考尚未存在的欄位）
        var columnsAdded    = diffs.Where(d => d.ObjectType == SchemaObjectType.Column && d.DifferenceType == DifferenceType.Added);
        // 階段 2b：修改欄位（含 DROP/ALTER/RECREATE INDEX，此時新欄位已存在）
        var columnsModified = diffs.Where(d => d.ObjectType == SchemaObjectType.Column && d.DifferenceType != DifferenceType.Added);
        var indexes         = diffs.Where(d => d.ObjectType == SchemaObjectType.Index);
        var constraints     = diffs.Where(d => d.ObjectType == SchemaObjectType.Constraint);
        var tableChildren   = columnsAdded.Concat(columnsModified).Concat(indexes).Concat(constraints);
        // 階段 3：View（需拓撲排序）
        var views = TopologicalSortViews(
            diffs.Where(d => d.ObjectType == SchemaObjectType.View).ToList(),
            programObjectLookup);
        // 階段 4：其餘程式物件
        var others = diffs.Where(d =>
            d.ObjectType is SchemaObjectType.StoredProcedure or SchemaObjectType.Function or SchemaObjectType.Trigger);

        return tables.Concat(tableChildren).Concat(views).Concat(others);
    }

    /// <summary>
    /// 對 View 差異清單做拓撲排序，確保被依賴的 View 先建立
    /// </summary>
    private static IEnumerable<SchemaDifference> TopologicalSortViews(
        IList<SchemaDifference> viewDiffs,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programObjectLookup)
    {
        if (viewDiffs.Count == 0) return viewDiffs;

        // 建立 objectName → diff 的快速查找（去除括號，大小寫不分）
        var byName = viewDiffs.ToDictionary(
            d => d.ObjectName.Replace("[", "").Replace("]", "").ToUpperInvariant(),
            d => d);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SchemaDifference>();

        void Visit(SchemaDifference diff)
        {
            var key = diff.ObjectName.Replace("[", "").Replace("]", "").ToUpperInvariant();
            if (!visited.Add(key)) return;

            // 找出此 View 的 definition，掃描其中引用了哪些同批次的 View
            if (programObjectLookup.TryGetValue((diff.Schema, ParseTwoParts(diff.ObjectName).name, SchemaObjectType.View), out var obj)
                && obj.Definition != null)
            {
                foreach (var dep in byName.Keys)
                {
                    if (dep == key) continue;
                    // 以 dot-notation 檢查 definition 是否包含依賴 View 名稱
                    var depParts = dep.Split('.');
                    if (depParts.Length >= 2)
                    {
                        var searchPattern = depParts[^2] + "." + depParts[^1];
                        if (obj.Definition.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                            Visit(byName[dep]);
                    }
                }
            }

            result.Add(diff);
        }

        foreach (var diff in viewDiffs)
            Visit(diff);

        return result;
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
        if (string.IsNullOrEmpty(diff.Schema))
            return $"-- [Schema 未設定，略過] {diff.ObjectName}";

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

        // 確保 Schema 存在，非 dbo 的 Schema 在目標資料庫可能尚未建立
        if (!table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{table.Schema}')");
            sb.AppendLine($"    EXEC(N'CREATE SCHEMA [{table.Schema}]');");
        }

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

            // timestamp/rowversion 不能手動 ADD，且每資料表只能有一個
            if (col.DataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                col.DataType.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
                return $"-- [略過] {diff.ObjectName}：timestamp/rowversion 欄位需手動新增";

            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var defaultVal = string.IsNullOrEmpty(col.DefaultValue) ? string.Empty : $" DEFAULT {col.DefaultValue}";
            return $"ALTER TABLE [{schema}].[{tableName}] ADD [{col.Name}] {col.GetFullDataType()} {nullable}{defaultVal};";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            var col = table?.GetColumn(columnName);
            if (col == null) return $"-- 無法找到欄位定義：{diff.ObjectName}";

            // timestamp/rowversion 由 SQL Server 自動管理，不可用 ALTER COLUMN 修改
            if (col.DataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                col.DataType.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
                return $"-- [略過] {diff.ObjectName}：timestamp/rowversion 欄位不支援 ALTER COLUMN";

            // DefaultValue 變更不用 ALTER COLUMN，改用 DROP CONSTRAINT + ADD DEFAULT
            if ("DefaultValue".Equals(diff.PropertyName, StringComparison.OrdinalIgnoreCase))
                return GenerateDefaultValueChangeSql(schema, tableName, columnName, col.DefaultValue);

            var newLength = int.TryParse(diff.SourceValue, out var len) ? len : col.MaxLength;
            var dataType = newLength.HasValue ? $"{col.DataType}({newLength})" : col.GetFullDataType();
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";

            // 若欄位有相依索引，需先 DROP 再 ALTER COLUMN 再 RECREATE
            var dependentIndexes = table?.Indexes
                .Where(idx => idx.Columns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)) ||
                              idx.IncludeColumns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                .ToList() ?? [];

            var sb = new StringBuilder();

            // 僅在 IsNullable 屬性變更為 NOT NULL 時才需要先清除 NULL 值
            // 若只是改長度（PropertyName = "MaxLength"），欄位本就不允許 NULL，不需要 UPDATE
            if (!col.IsNullable && "IsNullable".Equals(diff.PropertyName, StringComparison.OrdinalIgnoreCase))
            {
                var fillValue = GetNotNullFillValue(col.DataType);
                // 用 EXEC() 延遲編譯，避免 SQL Server 在欄位尚未存在時就拋出編譯期錯誤
                // COL_LENGTH 不使用括號以確保正確解析資料表名稱
                var updateSql = $"UPDATE [{schema}].[{tableName}] SET [{columnName}] = {fillValue} WHERE [{columnName}] IS NULL"
                    .Replace("'", "''");
                sb.AppendLine($"IF COL_LENGTH(N'{schema}.{tableName}', N'{columnName}') IS NOT NULL");
                sb.AppendLine($"    EXEC(N'{updateSql}');");
            }

            foreach (var idx in dependentIndexes)
            {
                sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{idx.Name}' AND object_id = OBJECT_ID(N'[{schema}].[{tableName}]'))");
                sb.AppendLine($"    DROP INDEX [{idx.Name}] ON [{schema}].[{tableName}];");
            }

            sb.AppendLine($"ALTER TABLE [{schema}].[{tableName}] ALTER COLUMN [{columnName}] {dataType} {nullable};");

            foreach (var idx in dependentIndexes)
            {
                var unique = idx.IsUnique ? "UNIQUE " : string.Empty;
                var clustered = idx.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
                var cols = string.Join(", ", idx.Columns.Select(c => $"[{c}]"));
                var include = idx.IncludeColumns.Count > 0
                    ? $" INCLUDE ({string.Join(", ", idx.IncludeColumns.Select(c => $"[{c}]"))})"
                    : string.Empty;
                var filter = string.IsNullOrEmpty(idx.FilterDefinition) ? string.Empty : $" WHERE {idx.FilterDefinition}";
                // 若索引欄位（或 INCLUDE 欄位）在目標資料庫尚未存在（例如未選取該欄位差異），跳過重建以防止執行錯誤
                var allIdxCols = idx.Columns.Concat(idx.IncludeColumns)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var colChecks = string.Join($"{Environment.NewLine}       AND ",
                    allIdxCols.Select(c => $"COL_LENGTH(N'{schema}.{tableName}', N'{c}') IS NOT NULL"));
                sb.AppendLine($"IF {colChecks}");
                sb.AppendLine($"   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{idx.Name}' AND object_id = OBJECT_ID(N'[{schema}].[{tableName}]'))");
                sb.Append($"    CREATE {unique}{clustered}INDEX [{idx.Name}] ON [{schema}].[{tableName}] ({cols}){include}{filter};");
            }

            return sb.ToString();
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

        // CREATE/ALTER VIEW、PROCEDURE、FUNCTION、TRIGGER 不能直接放在 BEGIN TRY 區塊內，
        // 需以 EXEC(N'...') 動態執行
        if (diff.DifferenceType == DifferenceType.Added)
        {
            var def = obj.Definition.Trim().TrimEnd(';');
            if (def.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase))
                def = "CREATE " + def[6..];
            return $"EXEC(N'{def.Replace("'", "''")}');";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            var def = obj.Definition.Trim().TrimEnd(';');
            if (def.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase))
                def = "ALTER " + def[7..];
            return $"EXEC(N'{def.Replace("'", "''")}');";
        }

        return string.Empty;
    }

    /// <summary>
    /// 產生修改預設值的 T-SQL：先動態 DROP 現有 DEFAULT 約束，再 ADD DEFAULT
    /// </summary>
    private static string GenerateDefaultValueChangeSql(
        string schema, string tableName, string columnName, string? newDefault)
    {
        // 整段包在 EXEC() 內以形成獨立變數作用域，避免多欄位同名時 DECLARE 重複衝突
        var innerSql = $"DECLARE @dc NVARCHAR(200); " +
                       $"SELECT @dc = name FROM sys.default_constraints " +
                       $"WHERE parent_object_id = OBJECT_ID(N''[{schema}].[{tableName}]'') " +
                       $"  AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N''[{schema}].[{tableName}]''), N''{columnName}'', ''ColumnId''); " +
                       $"IF @dc IS NOT NULL " +
                       $"    EXEC(N''ALTER TABLE [{schema}].[{tableName}] DROP CONSTRAINT ['' + @dc + '']'');";

        var sb = new StringBuilder();
        sb.AppendLine($"EXEC(N'{innerSql}');");

        if (!string.IsNullOrEmpty(newDefault))
            sb.Append($"ALTER TABLE [{schema}].[{tableName}] ADD DEFAULT {newDefault} FOR [{columnName}];");

        return sb.ToString().TrimEnd();
    }

    private static string GetNotNullFillValue(string dataType) => dataType.ToUpperInvariant() switch
    {
        "TINYINT" or "SMALLINT" or "INT" or "BIGINT"
            or "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY"
            or "FLOAT" or "REAL" or "BIT" => "0",
        "NVARCHAR" or "VARCHAR" or "CHAR" or "NCHAR"
            or "TEXT" or "NTEXT" => "N''",
        "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATE" or "TIME"
            or "DATETIMEOFFSET" => "'19000101'",
        "UNIQUEIDENTIFIER" => "'00000000-0000-0000-0000-000000000000'",
        "BINARY" or "VARBINARY" or "IMAGE" => "0x00",
        // xml、geography、geometry 等複雜型別無法自動填充，需人工處理
        _ => "NULL /* TODO: 請手動指定此型別的預設填充值 */"
    };

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
