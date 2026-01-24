using System.Data;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

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

    public async Task<List<ColumnSearchResult>> SearchColumnsAsync(string columnName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return [];

        const string sql = @"
            -- 搜尋 Tables 的欄位
            SELECT
                c.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'TABLE' AS ObjectType,
                TYPE_NAME(c.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'nvarchar', 'char', 'nchar')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
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
                AND c.name LIKE '%' + @ColumnName + '%'

            UNION ALL

            -- 搜尋 Views 的欄位
            SELECT
                c.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'VIEW' AS ObjectType,
                TYPE_NAME(c.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'nvarchar', 'char', 'nchar')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
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
                AND c.name LIKE '%' + @ColumnName + '%'

            UNION ALL

            -- 搜尋 Stored Procedures 的參數
            SELECT
                p.name AS ColumnName,
                o.name AS ObjectName,
                SCHEMA_NAME(o.schema_id) AS SchemaName,
                'PROCEDURE' AS ObjectType,
                TYPE_NAME(p.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(p.user_type_id) IN ('varchar', 'nvarchar', 'char', 'nchar')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(p.precision AS VARCHAR) + ',' + CAST(p.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                NULL AS Description
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            WHERE o.type = 'P'
                AND p.name LIKE '%' + @ColumnName + '%'
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
                        WHEN TYPE_NAME(p.user_type_id) IN ('varchar', 'nvarchar', 'char', 'nchar')
                            THEN '(' + CASE WHEN p.max_length = -1 THEN 'MAX' ELSE CAST(p.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(p.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(p.precision AS VARCHAR) + ',' + CAST(p.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType,
                NULL AS Description
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            WHERE o.type IN ('FN', 'IF', 'TF')
                AND p.name LIKE '%' + @ColumnName + '%'
                AND p.name <> ''

            ORDER BY ObjectType, SchemaName, ObjectName, ColumnName";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        command.Parameters.AddWithValue("@ColumnName", columnName);

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
}
