using System.Data;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL 查詢 Repository 實作
/// </summary>
public class SqlQueryRepository : ISqlQueryRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public SqlQueryRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<DataTable> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await ExecuteQueryAsync(sql, connectionString, ct);
    }

    public async Task<DataTable> ExecuteQueryAsync(string sql, string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;

        using var reader = await command.ExecuteReaderAsync(ct);
        var dataTable = new DataTable();
        dataTable.Load(reader);

        return dataTable;
    }

    public async Task<Dictionary<string, string>> GetColumnDescriptionsAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return new Dictionary<string, string>();

        return await GetColumnDescriptionsAsync(connectionString, ct);
    }

    public async Task<Dictionary<string, string>> GetColumnDescriptionsAsync(string connectionString, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                SCHEMA_NAME(t.schema_id) + '.' + t.name + '.' + c.name AS FullName,
                c.name AS ColumnName,
                CAST(ep.value AS NVARCHAR(MAX)) AS Description
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = c.object_id
                AND ep.minor_id = c.column_id
                AND ep.name = 'MS_Description'
            WHERE ep.value IS NOT NULL
            UNION ALL
            SELECT
                SCHEMA_NAME(v.schema_id) + '.' + v.name + '.' + c.name AS FullName,
                c.name AS ColumnName,
                CAST(ep.value AS NVARCHAR(MAX)) AS Description
            FROM sys.columns c
            INNER JOIN sys.views v ON c.object_id = v.object_id
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = c.object_id
                AND ep.minor_id = c.column_id
                AND ep.name = 'MS_Description'
            WHERE ep.value IS NOT NULL";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var columnName = reader.GetString(1);
            var description = reader.GetString(2);

            // 以欄位名稱為 key，讓查詢結果可以直接比對
            if (!result.ContainsKey(columnName))
            {
                result[columnName] = description;
            }
        }

        return result;
    }

    public async Task<List<ColumnSearchResult>> SearchColumnsAsync(string columnName, bool exactMatch = false, string? tableName = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return [];

        // 依搜尋模式決定 WHERE 條件：精確比對用 =，模糊搜尋用 LIKE
        var hasColumnFilter = !string.IsNullOrWhiteSpace(columnName);
        var columnCondition = hasColumnFilter
            ? (exactMatch ? "AND c.name = @ColumnName" : "AND c.name LIKE '%' + @ColumnName + '%'")
            : "";
        var paramCondition = hasColumnFilter
            ? (exactMatch ? "AND p.name = @ColumnName" : "AND p.name LIKE '%' + @ColumnName + '%'")
            : "";

        // 資料表名稱篩選條件（模糊搜尋）
        var hasTableFilter = !string.IsNullOrWhiteSpace(tableName);
        var objectNameCondition = hasTableFilter
            ? "AND o.name LIKE '%' + @TableName + '%'"
            : "";

        var sql = $@"
            -- 搜尋 Tables 的欄位
            SELECT
                c.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'TABLE' AS ObjectType,
                TYPE_NAME(c.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'char')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar', 'nchar')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                CAST(ep.value AS NVARCHAR(MAX)) AS Description
            FROM sys.columns c
            INNER JOIN sys.objects o ON c.object_id = o.object_id
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = c.object_id
                AND ep.minor_id = c.column_id
                AND ep.name = 'MS_Description'
            WHERE o.type = 'U'
                {columnCondition}
                {objectNameCondition}

            UNION ALL

            -- 搜尋 Views 的欄位
            SELECT
                c.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'VIEW' AS ObjectType,
                TYPE_NAME(c.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'char')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar', 'nchar')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                CAST(ep.value AS NVARCHAR(MAX)) AS Description
            FROM sys.columns c
            INNER JOIN sys.objects o ON c.object_id = o.object_id
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = c.object_id
                AND ep.minor_id = c.column_id
                AND ep.name = 'MS_Description'
            WHERE o.type = 'V'
                {columnCondition}
                {objectNameCondition}

            UNION ALL

            -- 搜尋 Stored Procedures 的參數
            SELECT
                p.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'PROCEDURE' AS ObjectType,
                TYPE_NAME(p.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(p.user_type_id) IN ('varchar', 'char')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('nvarchar', 'nchar')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length / 2 AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(p.precision AS VARCHAR) + ',' + CAST(p.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                NULL AS Description
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            WHERE o.type = 'P'
                {paramCondition}
                {objectNameCondition}
                AND p.name <> ''

            UNION ALL

            -- 搜尋 Functions 的參數
            SELECT
                p.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                CASE o.type
                    WHEN 'FN' THEN 'FUNCTION (Scalar)'
                    WHEN 'IF' THEN 'FUNCTION (Inline TVF)'
                    WHEN 'TF' THEN 'FUNCTION (Table-valued)'
                    ELSE 'FUNCTION'
                END AS ObjectType,
                TYPE_NAME(p.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(p.user_type_id) IN ('varchar', 'char')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('nvarchar', 'nchar')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length / 2 AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(p.precision AS VARCHAR) + ',' + CAST(p.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                NULL AS Description
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            WHERE o.type IN ('FN', 'IF', 'TF')
                {paramCondition}
                {objectNameCondition}
                AND p.name <> ''

            ORDER BY ObjectType, SchemaName, ObjectName, ColumnName";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        if (hasColumnFilter)
            command.Parameters.AddWithValue("@ColumnName", columnName);
        if (hasTableFilter)
            command.Parameters.AddWithValue("@TableName", tableName!);

        var results = new List<ColumnSearchResult>();
        using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(new ColumnSearchResult
            {
                ColumnName = reader.GetString(0),
                ObjectName = reader.GetString(1),
                SchemaName = reader.GetString(2),
                ObjectType = reader.GetString(3),
                DataType = reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return results;
    }

    public async Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await ExecuteQueryWithSchemaAsync(sql, connectionString, ct);
    }

    public async Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.KeyInfo, ct);
        var columns = MapColumnMetadata(reader.GetSchemaTable());

        // 手動填列：避免 DataTable.Load 因 KeyInfo 自動加上主鍵/唯一約束，
        // 導致 OUTER JOIN 含 NULL 鍵值或重複鍵值的結果載入失敗
        var table = new DataTable();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (table.Columns.Contains(name))
                name = $"{name}_{i}";
            table.Columns.Add(name, reader.GetFieldType(i));
        }

        while (await reader.ReadAsync(ct))
        {
            var row = table.NewRow();
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            table.Rows.Add(row);
        }

        return new QueryResultWithSchema { Table = table, Columns = columns };
    }

    /// <summary>
    /// 將 GetSchemaTable() 的結果對映為欄位中繼資料。
    /// 唯讀判定：identity、驅動回報唯讀、運算式欄位、無來源欄、byte[]（timestamp/rowversion）
    /// </summary>
    internal static List<QueryColumnMetadata> MapColumnMetadata(DataTable? schemaTable)
    {
        if (schemaTable == null)
            return [];

        var result = new List<QueryColumnMetadata>();
        foreach (DataRow row in schemaTable.Rows)
        {
            var clrType = row.Table.Columns.Contains("DataType") ? row["DataType"] as Type ?? typeof(object) : typeof(object);
            var baseColumn = AsString(row, "BaseColumnName");
            var isReadOnly = AsBool(row, "IsAutoIncrement")
                || AsBool(row, "IsReadOnly")
                || AsBool(row, "IsExpression")
                || string.IsNullOrEmpty(baseColumn)
                || clrType == typeof(byte[]);

            result.Add(new QueryColumnMetadata
            {
                ColumnName = AsString(row, "ColumnName") ?? string.Empty,
                BaseSchema = AsString(row, "BaseSchemaName"),
                BaseTable = AsString(row, "BaseTableName"),
                BaseColumn = baseColumn,
                IsKey = AsBool(row, "IsKey"),
                IsReadOnly = isReadOnly,
                ClrType = clrType
            });
        }
        return result;

        static string? AsString(DataRow row, string column) =>
            row.Table.Columns.Contains(column) && row[column] is string { Length: > 0 } s ? s : null;

        static bool AsBool(DataRow row, string column) =>
            row.Table.Columns.Contains(column) && row[column] is true;
    }
}
