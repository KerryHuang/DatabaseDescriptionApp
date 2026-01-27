using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 欄位使用統計 Repository 實作
/// </summary>
public class ColumnUsageRepository : IColumnUsageRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public ColumnUsageRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColumnUsageDetail>> GetAllColumnUsagesAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ColumnUsageDetail>();

        const string sql = @"
-- 取得 TABLE 的欄位
SELECT
    c.name AS ColumnName,
    s.name AS SchemaName,
    t.name AS ObjectName,
    'TABLE' AS ObjectType,
    CASE
        WHEN tp.name IN ('char', 'varchar', 'nchar', 'nvarchar', 'binary', 'varbinary')
            THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(
                CASE WHEN tp.name IN ('nchar', 'nvarchar') THEN c.max_length / 2 ELSE c.max_length END
            AS VARCHAR) END + ')'
        WHEN tp.name IN ('decimal', 'numeric')
            THEN tp.name + '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
        WHEN tp.name IN ('datetime2', 'datetimeoffset', 'time')
            THEN tp.name + '(' + CAST(c.scale AS VARCHAR) + ')'
        ELSE tp.name
    END AS DataType,
    tp.name AS BaseType,
    CASE WHEN tp.name IN ('nchar', 'nvarchar') THEN c.max_length / 2 ELSE c.max_length END AS MaxLength,
    c.precision AS [Precision],
    c.scale AS Scale,
    c.is_nullable AS IsNullable
FROM sys.columns c
INNER JOIN sys.tables t ON c.object_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id

UNION ALL

-- 取得 VIEW 的欄位
SELECT
    c.name AS ColumnName,
    s.name AS SchemaName,
    v.name AS ObjectName,
    'VIEW' AS ObjectType,
    CASE
        WHEN tp.name IN ('char', 'varchar', 'nchar', 'nvarchar', 'binary', 'varbinary')
            THEN tp.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(
                CASE WHEN tp.name IN ('nchar', 'nvarchar') THEN c.max_length / 2 ELSE c.max_length END
            AS VARCHAR) END + ')'
        WHEN tp.name IN ('decimal', 'numeric')
            THEN tp.name + '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
        WHEN tp.name IN ('datetime2', 'datetimeoffset', 'time')
            THEN tp.name + '(' + CAST(c.scale AS VARCHAR) + ')'
        ELSE tp.name
    END AS DataType,
    tp.name AS BaseType,
    CASE WHEN tp.name IN ('nchar', 'nvarchar') THEN c.max_length / 2 ELSE c.max_length END AS MaxLength,
    c.precision AS [Precision],
    c.scale AS Scale,
    c.is_nullable AS IsNullable
FROM sys.columns c
INNER JOIN sys.views v ON c.object_id = v.object_id
INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id

ORDER BY ColumnName, SchemaName, ObjectName";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<ColumnUsageDetail>(sql);
        return result.ToList();
    }
}
