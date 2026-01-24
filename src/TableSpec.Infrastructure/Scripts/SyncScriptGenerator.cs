using System.Text;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Infrastructure.Scripts;

/// <summary>
/// SQL 同步腳本產生器
/// </summary>
public class SyncScriptGenerator
{
    #region 單一差異腳本產生

    /// <summary>
    /// 產生表格的同步腳本
    /// </summary>
    public SyncScript GenerateScript(SchemaDifference diff, SchemaTable table)
    {
        var apply = new StringBuilder();
        var rollback = new StringBuilder();

        if (diff.DifferenceType == DifferenceType.Added)
        {
            // 產生 CREATE TABLE 語法
            apply.AppendLine($"CREATE TABLE {table.FullName} (");

            var columnDefs = new List<string>();
            foreach (var column in table.Columns)
            {
                columnDefs.Add($"    {GenerateColumnDefinition(column)}");
            }
            apply.AppendLine(string.Join(",\n", columnDefs));
            apply.AppendLine(");");

            // 回滾腳本
            rollback.AppendLine($"DROP TABLE {table.FullName};");
        }

        return new SyncScript
        {
            TargetEnvironment = string.Empty,
            GeneratedAt = DateTime.Now,
            ApplyScript = apply.ToString(),
            RollbackScript = rollback.ToString(),
            Differences = new List<SchemaDifference> { diff }
        };
    }

    /// <summary>
    /// 產生欄位的同步腳本
    /// </summary>
    public SyncScript GenerateScript(SchemaDifference diff, SchemaColumn column, string tableName)
    {
        var apply = new StringBuilder();
        var rollback = new StringBuilder();

        if (diff.DifferenceType == DifferenceType.Added)
        {
            // 產生 ALTER TABLE ADD COLUMN 語法
            apply.AppendLine($"ALTER TABLE {tableName}");
            apply.Append($"ADD {GenerateColumnDefinition(column)}");

            if (!column.IsNullable && !string.IsNullOrEmpty(column.DefaultValue))
            {
                apply.Append($" DEFAULT {column.DefaultValue}");
            }
            apply.AppendLine(";");

            // 回滾腳本
            rollback.AppendLine($"ALTER TABLE {tableName}");
            rollback.AppendLine($"DROP COLUMN [{column.Name}];");
        }
        else if (diff.DifferenceType == DifferenceType.Modified)
        {
            // 產生 ALTER TABLE ALTER COLUMN 語法
            apply.AppendLine($"ALTER TABLE {tableName}");
            apply.AppendLine($"ALTER COLUMN {GenerateColumnDefinition(column)};");

            // 回滾腳本 - 還原原本的定義
            if (diff.PropertyName == "MaxLength" && diff.TargetValue != null)
            {
                var originalColumn = new SchemaColumn
                {
                    Name = column.Name,
                    DataType = column.DataType,
                    MaxLength = int.TryParse(diff.TargetValue, out var len) ? len : null,
                    IsNullable = column.IsNullable
                };
                rollback.AppendLine($"ALTER TABLE {tableName}");
                rollback.AppendLine($"ALTER COLUMN {GenerateColumnDefinition(originalColumn)};");
            }
        }

        return new SyncScript
        {
            TargetEnvironment = string.Empty,
            GeneratedAt = DateTime.Now,
            ApplyScript = apply.ToString(),
            RollbackScript = rollback.ToString(),
            Differences = new List<SchemaDifference> { diff }
        };
    }

    /// <summary>
    /// 產生索引的同步腳本
    /// </summary>
    public SyncScript GenerateScript(SchemaDifference diff, SchemaIndex index, string tableName)
    {
        var apply = new StringBuilder();
        var rollback = new StringBuilder();

        if (diff.DifferenceType == DifferenceType.Added)
        {
            // 產生 CREATE INDEX 語法
            var indexType = index.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
            var unique = index.IsUnique ? "UNIQUE " : "";

            apply.Append($"CREATE {unique}{indexType} INDEX [{index.Name}]");
            apply.AppendLine();
            apply.Append($"ON {tableName} ([{string.Join("], [", index.Columns)}])");

            // Include 欄位
            if (index.IncludeColumns != null && index.IncludeColumns.Count > 0)
            {
                apply.AppendLine();
                apply.Append($"INCLUDE ([{string.Join("], [", index.IncludeColumns)}])");
            }

            // 篩選條件
            if (!string.IsNullOrEmpty(index.FilterDefinition))
            {
                apply.AppendLine();
                apply.Append($"WHERE {index.FilterDefinition}");
            }

            apply.AppendLine(";");

            // 回滾腳本
            rollback.AppendLine($"DROP INDEX [{index.Name}] ON {tableName};");
        }

        return new SyncScript
        {
            TargetEnvironment = string.Empty,
            GeneratedAt = DateTime.Now,
            ApplyScript = apply.ToString(),
            RollbackScript = rollback.ToString(),
            Differences = new List<SchemaDifference> { diff }
        };
    }

    /// <summary>
    /// 產生程式物件的同步腳本
    /// </summary>
    public SyncScript GenerateScript(SchemaDifference diff, SchemaProgramObject programObject)
    {
        var apply = new StringBuilder();
        var rollback = new StringBuilder();

        var definition = programObject.Definition ?? string.Empty;

        if (diff.DifferenceType == DifferenceType.Added)
        {
            // 直接使用原本的 CREATE 語法
            apply.AppendLine(definition);

            // 回滾腳本
            var dropKeyword = GetDropKeyword(programObject.ObjectType);
            rollback.AppendLine($"DROP {dropKeyword} {programObject.FullName};");
        }
        else if (diff.DifferenceType == DifferenceType.Modified)
        {
            // 將 CREATE 改為 ALTER
            var alteredDefinition = ConvertCreateToAlter(definition, programObject.ObjectType);
            apply.AppendLine(alteredDefinition);

            // 修改時沒有簡單的回滾方式，需要保存原本的定義
            rollback.AppendLine("-- 需要手動提供原本的定義以進行回滾");
        }

        return new SyncScript
        {
            TargetEnvironment = string.Empty,
            GeneratedAt = DateTime.Now,
            ApplyScript = apply.ToString(),
            RollbackScript = rollback.ToString(),
            Differences = new List<SchemaDifference> { diff }
        };
    }

    #endregion

    #region 批次腳本產生

    /// <summary>
    /// 產生批次同步腳本
    /// </summary>
    public SyncScript GenerateBatchScript(
        string targetEnvironment,
        IList<(SchemaDifference Diff, object Context, string? TableName)> items)
    {
        var apply = new StringBuilder();
        var rollback = new StringBuilder();
        var differences = new List<SchemaDifference>();

        apply.AppendLine($"-- 同步腳本：{targetEnvironment}");
        apply.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        apply.AppendLine();

        rollback.AppendLine($"-- 回滾腳本：{targetEnvironment}");
        rollback.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        rollback.AppendLine();

        foreach (var (diff, context, tableName) in items)
        {
            var script = context switch
            {
                SchemaTable table => GenerateScript(diff, table),
                SchemaColumn column => GenerateScript(diff, column, tableName!),
                SchemaIndex index => GenerateScript(diff, index, tableName!),
                SchemaProgramObject programObject => GenerateScript(diff, programObject),
                _ => throw new ArgumentException($"不支援的物件類型：{context.GetType().Name}")
            };

            apply.AppendLine(script.ApplyScript);
            apply.AppendLine("GO");
            apply.AppendLine();

            if (!string.IsNullOrEmpty(script.RollbackScript))
            {
                rollback.AppendLine(script.RollbackScript);
                rollback.AppendLine("GO");
                rollback.AppendLine();
            }

            differences.Add(diff);
        }

        return new SyncScript
        {
            TargetEnvironment = targetEnvironment,
            GeneratedAt = DateTime.Now,
            ApplyScript = apply.ToString(),
            RollbackScript = rollback.ToString(),
            Differences = differences
        };
    }

    #endregion

    #region 輔助方法

    /// <summary>
    /// 產生欄位定義
    /// </summary>
    private static string GenerateColumnDefinition(SchemaColumn column)
    {
        var sb = new StringBuilder();
        sb.Append($"[{column.Name}] {column.GetFullDataType()}");

        if (column.IsIdentity)
        {
            sb.Append(" IDENTITY(1,1)");
        }

        sb.Append(column.IsNullable ? " NULL" : " NOT NULL");

        return sb.ToString();
    }

    /// <summary>
    /// 取得 DROP 關鍵字
    /// </summary>
    private static string GetDropKeyword(ProgramObjectType objectType)
    {
        return objectType switch
        {
            ProgramObjectType.View => "VIEW",
            ProgramObjectType.StoredProcedure => "PROCEDURE",
            ProgramObjectType.Function => "FUNCTION",
            ProgramObjectType.Trigger => "TRIGGER",
            _ => throw new ArgumentException($"不支援的程式物件類型：{objectType}")
        };
    }

    /// <summary>
    /// 將 CREATE 語法轉換為 ALTER
    /// </summary>
    private static string ConvertCreateToAlter(string definition, ProgramObjectType objectType)
    {
        var keyword = objectType switch
        {
            ProgramObjectType.View => "VIEW",
            ProgramObjectType.StoredProcedure => "PROCEDURE",
            ProgramObjectType.Function => "FUNCTION",
            ProgramObjectType.Trigger => "TRIGGER",
            _ => throw new ArgumentException($"不支援的程式物件類型：{objectType}")
        };

        // 使用正則表達式替換 CREATE 為 ALTER（忽略大小寫）
        return System.Text.RegularExpressions.Regex.Replace(
            definition,
            $@"\bCREATE\s+{keyword}\b",
            $"ALTER {keyword}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    #endregion
}
