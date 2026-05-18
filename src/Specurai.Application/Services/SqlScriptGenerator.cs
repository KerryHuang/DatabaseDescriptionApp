using System.Text;
using System.Text.RegularExpressions;
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
        string targetEnvName,
        DatabaseSchema? targetSchema = null)
    {
        var programObjectLookup = BuildProgramObjectLookup(baseSchema);
        selectedDifferences = ExpandViewDependencies(selectedDifferences, baseSchema, targetSchema);
        var ordered = OrderDiffs(selectedDifferences, programObjectLookup).ToList();

        var applyScript = BuildApplyScript(ordered, baseSchema, baseEnvName, targetEnvName, programObjectLookup);
        var dryRunScript = BuildDryRunScript(ordered, baseSchema, baseEnvName, targetEnvName, programObjectLookup);

        return new SyncScript
        {
            TargetEnvironment = targetEnvName,
            GeneratedAt = DateTime.Now,
            ApplyScript = applyScript,
            DryRunScript = dryRunScript,
            Differences = selectedDifferences
        };
    }

    /// <summary>
    /// 拿掉外層大 transaction，每個批次以 GO 分隔，
    /// 每張表結束後 CHECKPOINT 釋放 SIMPLE 模式 log，避免大量 ALTER 撐爆 transaction log。
    /// 所有 DDL 加上 idempotent guard（IF NOT EXISTS / COL_LENGTH IS NULL），可安全重跑。
    /// </summary>
    private static string BuildApplyScript(
        IList<SchemaDifference> ordered,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programLookup)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, baseEnvName, targetEnvName, dryRun: false);
        AppendSchemaCreation(sb, ordered, baseSchema, programLookup);

        sb.AppendLine("PRINT N'======== Migration 開始 ========';");
        sb.AppendLine("GO");
        sb.AppendLine();

        string? currentGroup = null;
        for (int i = 0; i < ordered.Count; )
        {
            var diff = ordered[i];
            var sql = GenerateSqlForDifference(diff, baseSchema, programLookup);
            if (string.IsNullOrWhiteSpace(sql)) { i++; continue; }

            var groupKey = GetTableGroupKey(diff);
            if (!StringEq(groupKey, currentGroup))
            {
                if (currentGroup != null) AppendCheckpoint(sb);
                sb.AppendLine($"-- ===== {groupKey ?? diff.ObjectName} =====");
                sb.AppendLine($"PRINT N'>> {EscapeSqlString(diff.ObjectName)}';");
                currentGroup = groupKey;
            }

            // 連續同表的 ADD COLUMN 合併成單一 ALTER TABLE ADD（單次 metadata 鎖、單次 log 寫入）
            int batchEnd = TryFindAddColumnBatch(ordered, i, groupKey);
            if (batchEnd - i >= 2)
            {
                EmitBatchedAddColumns(sb, ordered, i, batchEnd, baseSchema, wrapTransaction: false);
                i = batchEnd;
                continue;
            }

            sb.AppendLine($"-- [{RiskLevelText(diff.RiskLevel)}] {diff.Description ?? diff.ObjectName}");
            sb.AppendLine(ApplyIdempotencyGuard(diff, sql));
            sb.AppendLine("GO");
            sb.AppendLine();
            i++;
        }

        if (currentGroup != null)
            AppendCheckpoint(sb);

        sb.AppendLine("PRINT N'======== Migration 完成 ========';");
        sb.AppendLine("GO");
        return sb.ToString();
    }

    /// <summary>
    /// Dry Run：每個批次各自 BEGIN TRAN + ROLLBACK，能驗證能不能跑、又不留下變更，
    /// 且每批 rollback 後 log 即可重用，不會跟 Apply 一樣有 log 暴增風險。
    /// </summary>
    private static string BuildDryRunScript(
        IList<SchemaDifference> ordered,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programLookup)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, baseEnvName, targetEnvName, dryRun: true);
        AppendSchemaCreation(sb, ordered, baseSchema, programLookup);

        sb.AppendLine("PRINT N'[Dry Run] 開始驗證（每個批次 BEGIN TRAN + ROLLBACK，無實際變更）';");
        sb.AppendLine("GO");
        sb.AppendLine();

        for (int i = 0; i < ordered.Count; )
        {
            var diff = ordered[i];
            var sql = GenerateSqlForDifference(diff, baseSchema, programLookup);
            if (string.IsNullOrWhiteSpace(sql)) { i++; continue; }

            int batchEnd = TryFindAddColumnBatch(ordered, i, GetTableGroupKey(diff));
            if (batchEnd - i >= 2)
            {
                EmitBatchedAddColumns(sb, ordered, i, batchEnd, baseSchema, wrapTransaction: true);
                i = batchEnd;
                continue;
            }

            sb.AppendLine($"-- [{RiskLevelText(diff.RiskLevel)}] {diff.Description ?? diff.ObjectName}");
            sb.AppendLine($"PRINT N'>> {EscapeSqlString(diff.ObjectName)}';");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine(ApplyIdempotencyGuard(diff, sql));
            sb.AppendLine("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
            sb.AppendLine("GO");
            sb.AppendLine();
            i++;
        }

        sb.AppendLine("PRINT N'[Dry Run] 驗證完成';");
        sb.AppendLine("GO");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, string baseEnvName, string targetEnvName, bool dryRun)
    {
        sb.AppendLine("-- ================================================");
        sb.AppendLine($"-- Schema Migration Script{(dryRun ? " (Dry Run)" : string.Empty)}");
        sb.AppendLine($"-- 基準環境：{baseEnvName}");
        sb.AppendLine($"-- 目標環境：{targetEnvName}");
        sb.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- 策略：拿掉外層 transaction，每個批次以 GO 分隔，每張表完成後 CHECKPOINT 釋放 log");
        sb.AppendLine("-- 安全性：所有 DDL 為 idempotent，腳本可重複執行（失敗後修正再跑安全）");
        sb.AppendLine("-- ================================================");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    /// <summary>
    /// 在腳本最前面集中建立缺少的 Schema。
    /// 收集所有 diff 涉及的 schema + 程式物件定義中 FROM/JOIN 引用的 schema，逐個 IF NOT EXISTS 建立。
    /// </summary>
    private static void AppendSchemaCreation(
        StringBuilder sb,
        IList<SchemaDifference> ordered,
        DatabaseSchema baseSchema,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programLookup)
    {
        var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var diff in ordered)
        {
            if (!string.IsNullOrEmpty(diff.Schema))
                schemas.Add(diff.Schema);

            // 程式物件（View/SP/Func/Trigger）的定義裡引用的 schema 也要建
            if (diff.ObjectType is SchemaObjectType.View
                or SchemaObjectType.StoredProcedure
                or SchemaObjectType.Function
                or SchemaObjectType.Trigger)
            {
                var (_, objName) = ParseTwoParts(diff.ObjectName);
                if (programLookup.TryGetValue((diff.Schema, objName, diff.ObjectType), out var obj)
                    && !string.IsNullOrEmpty(obj.Definition))
                {
                    foreach (var (refSchema, _) in ParseViewReferences(obj.Definition))
                        schemas.Add(refSchema);
                }
            }
        }

        schemas.Remove("dbo"); // dbo 一定存在
        if (schemas.Count == 0) return;

        sb.AppendLine("-- ===== Schema 建立 =====");
        foreach (var s in schemas.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{s}')");
            sb.AppendLine($"    EXEC(N'CREATE SCHEMA [{s}]');");
            sb.AppendLine("GO");
        }
        sb.AppendLine();
    }

    private static void AppendCheckpoint(StringBuilder sb)
    {
        sb.AppendLine("CHECKPOINT;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static string? GetTableGroupKey(SchemaDifference diff)
    {
        switch (diff.ObjectType)
        {
            case SchemaObjectType.Table:
                return diff.ObjectName; // [schema].[table]
            case SchemaObjectType.Column:
            case SchemaObjectType.Index:
            case SchemaObjectType.Constraint:
                var (_, table, _) = ParseThreeParts(diff.ObjectName);
                return $"[{diff.Schema}].[{table}]";
            default:
                return null; // 程式物件不分組（每個一批，無 CHECKPOINT）
        }
    }

    private static bool StringEq(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string EscapeSqlString(string s) => s.Replace("'", "''");

    /// <summary>
    /// 向後掃描連續同表的 ADD COLUMN 差異，回傳結束 index（exclusive）。
    /// </summary>
    private static int TryFindAddColumnBatch(IList<SchemaDifference> ordered, int start, string? groupKey)
    {
        if (groupKey == null) return start;
        int end = start;
        while (end < ordered.Count)
        {
            var d = ordered[end];
            if (d.ObjectType != SchemaObjectType.Column) break;
            if (d.DifferenceType != DifferenceType.Added) break;
            if (!StringEq(GetTableGroupKey(d), groupKey)) break;
            end++;
        }
        return end;
    }

    /// <summary>
    /// 將多個同表 ADD COLUMN 合併成單一動態 ALTER TABLE ADD：
    ///   DECLARE @missing NVARCHAR(MAX) = N'';
    ///   IF COL_LENGTH(...) IS NULL SET @missing += N', [col] type ...';  (每欄一行)
    ///   IF LEN(@missing) > 0 EXEC(N'ALTER TABLE ... ADD ' + STUFF(@missing, 1, 2, ''));
    /// 單次 metadata 鎖、單次 log 寫入，且保持 idempotent。
    /// </summary>
    private static void EmitBatchedAddColumns(
        StringBuilder sb,
        IList<SchemaDifference> ordered,
        int start, int end,
        DatabaseSchema baseSchema,
        bool wrapTransaction)
    {
        var first = ordered[start];
        var (_, tableName, _) = ParseThreeParts(first.ObjectName);
        var schema = first.Schema;
        var baseTable = baseSchema.GetTable(schema, tableName);
        if (baseTable == null) return;

        sb.AppendLine($"-- 批次新增 {end - start} 個欄位至 [{schema}].[{tableName}]（單一 ALTER，避免多次 metadata 鎖 / log 寫入）");
        sb.AppendLine($"PRINT N'>> [{schema}].[{tableName}] (批次新增 {end - start} 欄)';");
        if (wrapTransaction) sb.AppendLine("BEGIN TRANSACTION;");

        sb.AppendLine("DECLARE @missing NVARCHAR(MAX) = N'';");
        for (int k = start; k < end; k++)
        {
            var d = ordered[k];
            var (_, _, c) = ParseThreeParts(d.ObjectName);
            var col = baseTable.GetColumn(c);
            if (col == null) continue;
            // timestamp/rowversion 不可手動 ADD，逐個排除
            if (col.DataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                col.DataType.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"-- [略過] {d.ObjectName}：timestamp/rowversion 需手動新增");
                continue;
            }
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var defaultPart = string.IsNullOrEmpty(col.DefaultValue) ? "" : $" DEFAULT {col.DefaultValue}";
            var colDef = $", [{c}] {col.GetFullDataType()} {nullable}{defaultPart}";
            // 嵌入 N'...' 字串內：所有單引號需 → ''
            sb.AppendLine(
                $"IF COL_LENGTH(N'{schema}.{tableName}', N'{c}') IS NULL " +
                $"SET @missing += N'{colDef.Replace("'", "''")}';");
        }
        // 用變數承接最終 SQL，避免 EXEC(literal + function()) 在某些 SQL Server 版本被擋
        sb.AppendLine("IF LEN(@missing) > 0");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    DECLARE @sql NVARCHAR(MAX) = N'ALTER TABLE [{schema}].[{tableName}] ADD ' + STUFF(@missing, 1, 2, N'');");
        sb.AppendLine("    EXEC sp_executesql @sql;");
        sb.AppendLine("END");

        if (wrapTransaction) sb.AppendLine("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    /// <summary>
    /// 為各種 DDL 加上 idempotent guard，使腳本可安全重跑。
    /// </summary>
    private static string ApplyIdempotencyGuard(SchemaDifference diff, string sql)
    {
        switch (diff.ObjectType)
        {
            case SchemaObjectType.Table when diff.DifferenceType == DifferenceType.Added:
                return $"IF OBJECT_ID(N'{diff.ObjectName}', N'U') IS NULL\nBEGIN\n{sql}\nEND";

            case SchemaObjectType.Column when diff.DifferenceType == DifferenceType.Added:
            {
                var (_, t, c) = ParseThreeParts(diff.ObjectName);
                return $"IF COL_LENGTH(N'{diff.Schema}.{t}', N'{c}') IS NULL\n    {sql}";
            }

            case SchemaObjectType.Index when diff.DifferenceType == DifferenceType.Added:
            {
                var (_, t, n) = ParseThreeParts(diff.ObjectName);
                return $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{n}' AND object_id = OBJECT_ID(N'[{diff.Schema}].[{t}]'))\n    {sql}";
            }

            case SchemaObjectType.Constraint when diff.DifferenceType == DifferenceType.Added:
            {
                var (_, t, n) = ParseThreeParts(diff.ObjectName);
                return $"IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'{n}' AND parent_object_id = OBJECT_ID(N'[{diff.Schema}].[{t}]'))\n    {sql}";
            }

            // ALTER COLUMN（Column.Modified）已內含 sys.columns 動態檢查，可重跑
            // 程式物件已改用 CREATE OR ALTER（見 GenerateProgramObjectSql）
            default:
                return sql;
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
    /// 對 View 差異清單做拓撲排序，確保被依賴的 View 先建立。
    /// 依賴判斷使用與 ExpandViewDependencies 相同的 FROM/JOIN regex，
    /// 能同時處理 `[schema].[name]` 與 `schema.name` 兩種寫法。
    /// </summary>
    private static IEnumerable<SchemaDifference> TopologicalSortViews(
        IList<SchemaDifference> viewDiffs,
        Dictionary<(string, string, SchemaObjectType), SchemaProgramObject> programObjectLookup)
    {
        if (viewDiffs.Count == 0) return viewDiffs;

        // 以 schema+name (大小寫不分) 為 key，建立 byName 對照
        static string Key(string schema, string name) =>
            $"{schema}.{name}".ToUpperInvariant();

        var byName = viewDiffs.ToDictionary(
            d => Key(d.Schema, ParseTwoParts(d.ObjectName).name),
            d => d);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SchemaDifference>();

        void Visit(SchemaDifference diff)
        {
            var key = Key(diff.Schema, ParseTwoParts(diff.ObjectName).name);
            if (!visited.Add(key)) return;

            if (programObjectLookup.TryGetValue(
                    (diff.Schema, ParseTwoParts(diff.ObjectName).name, SchemaObjectType.View),
                    out var obj) && obj.Definition != null)
            {
                foreach (var (depSchema, depName) in ParseViewReferences(obj.Definition))
                {
                    var depKey = Key(depSchema, depName);
                    if (depKey == key) continue;
                    if (byName.TryGetValue(depKey, out var depDiff))
                        Visit(depDiff);
                }
            }

            result.Add(diff);
        }

        foreach (var diff in viewDiffs)
            Visit(diff);

        return result;
    }

    /// <summary>
    /// 從 View 定義抽出 FROM / JOIN 後的物件參照（schema, name），同時支援 `[x].[y]` 與 `x.y` 兩種寫法。
    /// </summary>
    private static IEnumerable<(string Schema, string Name)> ParseViewReferences(string definition)
    {
        foreach (Match m in ViewDepRegex.Matches(definition))
        {
            var schema = m.Groups["schema"].Success && m.Groups["schema"].Length > 0
                ? m.Groups["schema"].Value
                : "dbo"; // 無前綴 → 預設 dbo
            yield return (schema, m.Groups["name"].Value);
        }
    }

    // 支援三種形式：[schema].[name] / schema.name / name（後者預設 dbo schema）
    // 同時涵蓋 FROM、JOIN（含 INNER/LEFT/RIGHT/FULL/CROSS JOIN）、APPLY（OUTER/CROSS APPLY）
    private static readonly Regex ViewDepRegex = new(
        @"\b(?:FROM|JOIN|APPLY)\s+(?:\[?(?<schema>\w+)\]?\.)?\[?(?<name>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            // 若欄位有相依索引，需先 DROP 再 ALTER COLUMN 再 RECREATE
            var dependentIndexes = table?.Indexes
                .Where(idx => idx.Columns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)) ||
                              idx.IncludeColumns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                .ToList() ?? [];

            // IsNullable 變更：用動態 SQL 讀取目標資料庫的現有型別，避免因基準/目標精度不同導致溢位
            if ("IsNullable".Equals(diff.PropertyName, StringComparison.OrdinalIgnoreCase))
                // col.IsNullable = false 代表目標狀態為 NOT NULL；取反後傳入 targetIsNotNull
                return GenerateNullabilityChangeSql(schema, tableName, columnName, !col.IsNullable, col.DataType, dependentIndexes);

            var newLength = int.TryParse(diff.SourceValue, out var len) ? len : col.MaxLength;
            var dataType = newLength.HasValue ? $"{col.DataType}({newLength})" : col.GetFullDataType();

            var sb = new StringBuilder();

            // 動態 DROP 目標 DB 上所有依賴此欄位的索引（包括基準未記載的索引）
            sb.AppendLine(GenerateDynamicDropDependentIndexesSql(schema, tableName, columnName));

            // 動態讀取目標欄位的 nullable，避免基準/目標 nullability 不同導致 UPDATE fails 或資料截斷
            // 若 Engine 為 Enterprise/Azure/Managed Instance（3/5/8），自動附加 WITH (ONLINE = ON) 以縮短鎖時間
            {
                var innerSql = $"DECLARE @nl NCHAR(8), @online NVARCHAR(30) = N''''; " +
                               $"IF CAST(SERVERPROPERTY(''EngineEdition'') AS INT) IN (3,5,8) SET @online = N'' WITH (ONLINE = ON)''; " +
                               $"SELECT @nl = CASE WHEN is_nullable = 1 THEN N''NULL'' ELSE N''NOT NULL'' END " +
                               $"FROM sys.columns WHERE object_id = OBJECT_ID(N''[{schema}].[{tableName}]'') AND name = N''{columnName}''; " +
                               $"IF @nl IS NOT NULL EXEC(N''ALTER TABLE [{schema}].[{tableName}] ALTER COLUMN [{columnName}] {dataType} '' + @nl + @online);";
                sb.AppendLine($"EXEC(N'{innerSql}');");
            }

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

        // 統一改成 CREATE OR ALTER（SQL Server 2016+），同時支援 Added/Modified 並讓腳本可重跑
        if (diff.DifferenceType == DifferenceType.Added ||
            diff.DifferenceType == DifferenceType.Modified)
        {
            var def = obj.Definition.Trim().TrimEnd(';');
            def = ProgramObjectCreateOrAlterRegex.Replace(def, "CREATE OR ALTER ", 1);
            return $"EXEC(N'{def.Replace("'", "''")}');";
        }

        return string.Empty;
    }

    private static readonly Regex ProgramObjectCreateOrAlterRegex = new(
        @"^\s*(?:CREATE\s+OR\s+ALTER|CREATE|ALTER)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 產生修改預設值的 T-SQL：先動態 DROP 現有 DEFAULT 約束，再 ADD DEFAULT
    /// </summary>
    /// <summary>
    /// 產生修改 Nullable 的 T-SQL：用動態 SQL 讀取目標資料庫現有欄位型別，避免因基準/目標精度差異造成溢位
    /// </summary>
    private static string GenerateNullabilityChangeSql(
        string schema, string tableName, string columnName, bool targetNotNull,
        string colDataType, List<SchemaIndex> dependentIndexes)
    {
        var nullKeyword = targetNotNull ? "NOT NULL" : "NULL";
        var sb = new StringBuilder();

        // NOT NULL 時先清除 NULL 值
        if (targetNotNull)
        {
            var fillValue = GetNotNullFillValue(colDataType);
            var updateSql = $"UPDATE [{schema}].[{tableName}] SET [{columnName}] = {fillValue} WHERE [{columnName}] IS NULL"
                .Replace("'", "''");
            sb.AppendLine($"IF COL_LENGTH(N'{schema}.{tableName}', N'{columnName}') IS NOT NULL");
            sb.AppendLine($"    EXEC(N'{updateSql}');");
        }

        // 動態 DROP 目標 DB 上所有依賴此欄位的索引（包括基準未記載的索引，例如目標 DB 獨有）
        sb.AppendLine(GenerateDynamicDropDependentIndexesSql(schema, tableName, columnName));

        // 動態 SQL：讀取目標現有型別，保持原有精度，只改 NULL/NOT NULL
        // 若 Engine 為 Enterprise/Azure/Managed Instance（3/5/8），自動附加 WITH (ONLINE = ON) 縮短鎖時間
        var alterInner =
            $"DECLARE @t NVARCHAR(200), @dt NVARCHAR(400), @online NVARCHAR(30) = N''''; " +
            $"IF CAST(SERVERPROPERTY(''EngineEdition'') AS INT) IN (3,5,8) SET @online = N'' WITH (ONLINE = ON)'';" +
            $"SELECT @t = tp.name, @dt = " +
            $"  CASE " +
            $"    WHEN tp.name IN (''nvarchar'',''varchar'',''char'',''nchar'',''binary'',''varbinary'') " +
            $"      THEN tp.name + ''('' + CASE WHEN c.max_length = -1 THEN ''MAX'' ELSE CAST(c.max_length / CASE WHEN tp.name LIKE ''n%'' THEN 2 ELSE 1 END AS NVARCHAR) END + '')'' " +
            $"    WHEN tp.name IN (''decimal'',''numeric'') " +
            $"      THEN tp.name + ''('' + CAST(c.precision AS NVARCHAR) + '','' + CAST(c.scale AS NVARCHAR) + '')'' " +
            $"    WHEN tp.name IN (''datetime2'',''time'',''datetimeoffset'') " +
            $"      THEN tp.name + ''('' + CAST(c.scale AS NVARCHAR) + '')'' " +
            $"    ELSE tp.name " +
            $"  END " +
            $"FROM sys.columns c JOIN sys.types tp ON c.user_type_id = tp.user_type_id " +
            $"WHERE c.object_id = OBJECT_ID(N''[{schema}].[{tableName}]'') AND c.name = N''{columnName}''; " +
            $"IF @t IS NOT NULL " +
            $"  EXEC(N''ALTER TABLE [{schema}].[{tableName}] ALTER COLUMN [{columnName}] '' + @dt + '' {nullKeyword}'' + @online);";

        sb.AppendLine($"EXEC(N'{alterInner}');");

        // RECREATE 相依索引（加欄位存在性檢查）
        foreach (var idx in dependentIndexes)
        {
            var unique = idx.IsUnique ? "UNIQUE " : string.Empty;
            var clustered = idx.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
            var cols = string.Join(", ", idx.Columns.Select(c => $"[{c}]"));
            var include = idx.IncludeColumns.Count > 0
                ? $" INCLUDE ({string.Join(", ", idx.IncludeColumns.Select(c => $"[{c}]"))})"
                : string.Empty;
            var filter = string.IsNullOrEmpty(idx.FilterDefinition) ? string.Empty : $" WHERE {idx.FilterDefinition}";
            var allIdxCols = idx.Columns.Concat(idx.IncludeColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var colChecks = string.Join($"{Environment.NewLine}       AND ",
                allIdxCols.Select(c => $"COL_LENGTH(N'{schema}.{tableName}', N'{c}') IS NOT NULL"));
            sb.AppendLine($"IF {colChecks}");
            sb.AppendLine($"   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{idx.Name}' AND object_id = OBJECT_ID(N'[{schema}].[{tableName}]'))");
            sb.Append($"    CREATE {unique}{clustered}INDEX [{idx.Name}] ON [{schema}].[{tableName}] ({cols}){include}{filter};");
        }

        return sb.ToString().TrimEnd();
    }

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

    /// <summary>
    /// 產生動態 DROP 所有依賴指定欄位的非 PK 索引（含目標 DB 中基準未記載的索引）
    /// </summary>
    private static string GenerateDynamicDropDependentIndexesSql(string schema, string tableName, string columnName)
    {
        // 用 EXEC 包裝以形成獨立變數作用域，避免多欄位 DECLARE @s 衝突
        var inner =
            $"DECLARE @s NVARCHAR(MAX) = N''''; " +
            $"SELECT @s += N''DROP INDEX ['' + i.name + N''] ON [{schema}].[{tableName}];'' " +
            $"FROM sys.indexes i " +
            $"JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id " +
            $"JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
            $"WHERE i.object_id = OBJECT_ID(N''[{schema}].[{tableName}]'') " +
            $"  AND c.name = N''{columnName}'' " +
            $"  AND i.is_primary_key = 0 " +
            $"  AND i.is_unique_constraint = 0 " +
            $"  AND i.type > 0; " +
            $"IF LEN(@s) > 0 EXEC(@s);";
        return $"EXEC(N'{inner}');";
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

    /// <summary>
    /// 比對 View 定義中的 FROM / JOIN 來源，自動把目標缺少但基準有的 Table/View 補入腳本。
    /// 參考 MoldPlan ViewMigrationGenerator 的依賴解析做法。
    /// </summary>
    private static IList<SchemaDifference> ExpandViewDependencies(
        IList<SchemaDifference> selected,
        DatabaseSchema baseSchema,
        DatabaseSchema? targetSchema)
    {
        if (targetSchema == null) return selected;

        var targetObjects = new HashSet<string>(
            targetSchema.Tables.Select(t => $"[{t.Schema}].[{t.Name}]")
                .Concat(targetSchema.Views.Select(v => $"[{v.Schema}].[{v.Name}]")),
            StringComparer.OrdinalIgnoreCase);

        var existingNames = new HashSet<string>(
            selected.Select(d => d.ObjectName),
            StringComparer.OrdinalIgnoreCase);

        var result = new List<SchemaDifference>(selected);
        var pendingViews = new Queue<SchemaProgramObject>();

        // 將選取的（且基準有定義的）View 放入處理佇列
        foreach (var diff in selected.Where(d => d.ObjectType == SchemaObjectType.View))
        {
            var (_, name) = ParseTwoParts(diff.ObjectName);
            var view = baseSchema.Views.FirstOrDefault(v =>
                v.Schema.Equals(diff.Schema, StringComparison.OrdinalIgnoreCase) &&
                v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (view != null) pendingViews.Enqueue(view);
        }

        while (pendingViews.Count > 0)
        {
            var view = pendingViews.Dequeue();
            if (string.IsNullOrEmpty(view.Definition)) continue;

            foreach (var (depSchema, depName) in ParseViewReferences(view.Definition))
            {
                var depFull = $"[{depSchema}].[{depName}]";

                if (targetObjects.Contains(depFull)) continue;     // 目標已有
                if (!existingNames.Add(depFull)) continue;         // 已在補齊清單

                // 嘗試從基準找 Table
                var baseTable = baseSchema.Tables.FirstOrDefault(t =>
                    t.Schema.Equals(depSchema, StringComparison.OrdinalIgnoreCase) &&
                    t.Name.Equals(depName, StringComparison.OrdinalIgnoreCase));
                if (baseTable != null)
                {
                    result.Add(new SchemaDifference
                    {
                        ObjectType = SchemaObjectType.Table,
                        ObjectName = depFull,
                        Schema = depSchema,
                        DifferenceType = DifferenceType.Added,
                        RiskLevel = RiskLevel.Low,
                        Description = $"[自動補齊] 表格 {depFull}：因相依 View 需要"
                    });
                    continue;
                }

                // 嘗試從基準找 View；找到就遞迴展開其相依
                var baseView = baseSchema.Views.FirstOrDefault(v =>
                    v.Schema.Equals(depSchema, StringComparison.OrdinalIgnoreCase) &&
                    v.Name.Equals(depName, StringComparison.OrdinalIgnoreCase));
                if (baseView != null)
                {
                    result.Add(new SchemaDifference
                    {
                        ObjectType = SchemaObjectType.View,
                        ObjectName = depFull,
                        Schema = depSchema,
                        DifferenceType = DifferenceType.Added,
                        RiskLevel = RiskLevel.Low,
                        Description = $"[自動補齊] 檢視表 {depFull}：因相依 View 需要"
                    });
                    pendingViews.Enqueue(baseView);
                }
                // 基準也沒有 → 無法補齊（可能是函數、同義字、外部物件），留給執行階段報錯
            }
        }

        return result;
    }

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
