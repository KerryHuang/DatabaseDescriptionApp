using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 使用狀態分析 Repository 實作
/// </summary>
public class UsageAnalysisRepository : IUsageAnalysisRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public UsageAnalysisRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<TableUsageInfo>> GetTableUsageAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableUsageInfo>();

        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    ISNULL(SUM(ius.user_seeks), 0) AS UserSeeks,
    ISNULL(SUM(ius.user_scans), 0) AS UserScans,
    ISNULL(SUM(ius.user_lookups), 0) AS UserLookups,
    ISNULL(SUM(ius.user_updates), 0) AS UserUpdates,
    MAX(ius.last_user_seek) AS LastUserSeek,
    MAX(ius.last_user_scan) AS LastUserScan,
    MAX(ius.last_user_update) AS LastUserUpdate
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.dm_db_index_usage_stats ius
    ON ius.object_id = t.object_id
    AND ius.database_id = DB_ID()
GROUP BY s.name, t.name
ORDER BY s.name, t.name";

        try
        {
            await using var connection = new SqlConnection(connectionString);
            var result = await connection.QueryAsync<TableUsageInfo>(
                new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
            return result.ToList();
        }
        catch
        {
            return Array.Empty<TableUsageInfo>();
        }
    }

    public async Task<IReadOnlyList<ColumnUsageStatus>> GetColumnUsageStatusAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ColumnUsageStatus>();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            // 第一步：取得查詢計畫中引用的欄位
            progress?.Report("正在分析查詢計畫引用...");
            var referencedColumns = await GetReferencedColumnsAsync(connection, ct);

            // 第二步：取得所有欄位基本資訊（含 PK/FK/Identity/Default）
            progress?.Report("正在取得欄位資訊...");
            var columnInfos = await GetColumnInfosAsync(connection, ct);

            // 第三步：逐表檢查 NULL / 預設值
            var results = new List<ColumnUsageStatus>();
            var tables = columnInfos.GroupBy(c => new { c.SchemaName, c.TableName }).ToList();

            for (var i = 0; i < tables.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var tableGroup = tables[i];
                progress?.Report($"正在掃描 {i + 1}/{tables.Count} 張表：[{tableGroup.Key.SchemaName}].[{tableGroup.Key.TableName}]...");

                var nullDefaultResults = await CheckNullAndDefaultAsync(
                    connection, tableGroup.Key.SchemaName, tableGroup.Key.TableName,
                    tableGroup.ToList(), ct);

                foreach (var col in tableGroup)
                {
                    var isReferenced = referencedColumns.Contains(
                        $"{col.SchemaName}.{col.TableName}.{col.ColumnName}");

                    var ndResult = nullDefaultResults.GetValueOrDefault(col.ColumnName);

                    results.Add(new ColumnUsageStatus
                    {
                        SchemaName = col.SchemaName,
                        TableName = col.TableName,
                        ColumnName = col.ColumnName,
                        DataType = col.DataType,
                        IsReferencedInQueries = isReferenced,
                        IsAllNull = ndResult?.IsAllNull ?? false,
                        IsAllDefault = ndResult?.IsAllDefault ?? false,
                        DefaultValue = col.DefaultValue,
                        IsPrimaryKey = col.IsPrimaryKey,
                        IsForeignKey = col.IsForeignKey,
                        IsIdentity = col.IsIdentity
                    });
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<ColumnUsageStatus>();
        }
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("連線字串為空");

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
    }

    #region 私有方法

    private static async Task<HashSet<string>> GetReferencedColumnsAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    OBJECT_SCHEMA_NAME(st.objectid) AS SchemaName,
    OBJECT_NAME(st.objectid) AS TableName,
    c.value('(@Column)[1]', 'NVARCHAR(128)') AS ColumnName
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
CROSS APPLY qp.query_plan.nodes('//ColumnReference') AS ref(c)
WHERE st.objectid IS NOT NULL
    AND st.dbid = DB_ID()";

        try
        {
            var result = await connection.QueryAsync<(string SchemaName, string TableName, string ColumnName)>(
                new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));

            return result
                .Where(r => !string.IsNullOrEmpty(r.SchemaName) && !string.IsNullOrEmpty(r.TableName) && !string.IsNullOrEmpty(r.ColumnName))
                .Select(r => $"{r.SchemaName}.{r.TableName}.{r.ColumnName}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<IReadOnlyList<ColumnInfoRaw>> GetColumnInfosAsync(
        SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    CASE
        WHEN tp.name IN ('nvarchar','nchar') THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length/2 AS VARCHAR) END + ')'
        WHEN tp.name IN ('varchar','char','varbinary') THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
        WHEN tp.name IN ('decimal','numeric') THEN tp.name + '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
        ELSE tp.name
    END AS DataType,
    CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
    CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey,
    c.is_identity AS IsIdentity,
    dc.definition AS DefaultValue
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types tp ON c.user_type_id = tp.user_type_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM sys.index_columns ic
    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
LEFT JOIN (
    SELECT DISTINCT fkc.parent_object_id, fkc.parent_column_id
    FROM sys.foreign_key_columns fkc
) fk ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
ORDER BY s.name, t.name, c.column_id";

        var result = await connection.QueryAsync<ColumnInfoRaw>(
            new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300));
        return result.ToList();
    }

    private static async Task<Dictionary<string, NullDefaultResult>> CheckNullAndDefaultAsync(
        SqlConnection connection, string schemaName, string tableName,
        List<ColumnInfoRaw> columns, CancellationToken ct)
    {
        var results = new Dictionary<string, NullDefaultResult>();

        var checkableColumns = columns.Where(c => !c.IsIdentity).ToList();
        if (checkableColumns.Count == 0)
            return results;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("SELECT");
            sb.AppendLine("    COUNT(*) AS TotalRows,");

            var parts = new List<string>();
            foreach (var col in checkableColumns)
            {
                var escapedName = col.ColumnName.Replace("]", "]]");
                parts.Add($"    SUM(CASE WHEN [{escapedName}] IS NULL THEN 1 ELSE 0 END) AS [{escapedName}_NullCount]");

                if (!string.IsNullOrEmpty(col.DefaultValue))
                {
                    var defaultVal = col.DefaultValue.Trim('(', ')');
                    parts.Add($"    SUM(CASE WHEN CAST([{escapedName}] AS NVARCHAR(MAX)) = N'{defaultVal.Replace("'", "''")}' THEN 1 ELSE 0 END) AS [{escapedName}_DefaultCount]");
                }
            }

            sb.AppendLine(string.Join(",\n", parts));
            sb.AppendLine($"FROM [{schemaName.Replace("]", "]]")}].[{tableName.Replace("]", "]]")}]");

            var row = await connection.QueryFirstOrDefaultAsync(
                new CommandDefinition(sb.ToString(), cancellationToken: ct, commandTimeout: 300));

            if (row == null) return results;

            var rowDict = (IDictionary<string, object>)row;
            var totalRows = Convert.ToInt64(rowDict["TotalRows"]);

            foreach (var col in checkableColumns)
            {
                var nullCount = rowDict.ContainsKey($"{col.ColumnName}_NullCount")
                    ? Convert.ToInt64(rowDict[$"{col.ColumnName}_NullCount"])
                    : 0L;
                var defaultCount = rowDict.ContainsKey($"{col.ColumnName}_DefaultCount")
                    ? Convert.ToInt64(rowDict[$"{col.ColumnName}_DefaultCount"])
                    : 0L;

                results[col.ColumnName] = new NullDefaultResult
                {
                    IsAllNull = totalRows == 0 || nullCount == totalRows,
                    IsAllDefault = totalRows > 0 && !string.IsNullOrEmpty(col.DefaultValue) && (nullCount + defaultCount) == totalRows
                };
            }
        }
        catch
        {
            // 查詢失敗時不影響其他表的掃描
        }

        return results;
    }

    #endregion

    #region 內部類別

    private class ColumnInfoRaw
    {
        public string SchemaName { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public bool IsPrimaryKey { get; init; }
        public bool IsForeignKey { get; init; }
        public bool IsIdentity { get; init; }
        public string? DefaultValue { get; init; }
    }

    private class NullDefaultResult
    {
        public bool IsAllNull { get; init; }
        public bool IsAllDefault { get; init; }
    }

    #endregion
}
