using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 資料表統計 Repository 實作
/// </summary>
public class TableStatisticsRepository : ITableStatisticsRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public TableStatisticsRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TableStatisticsInfo>> GetAllTableStatisticsAsync(
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableStatisticsInfo>();

        await using var connection = new SqlConnection(connectionString);

        const string sql = @"
-- 資料表統計
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    'TABLE' AS ObjectType,
    ISNULL(ps.row_count, 0) AS ApproximateRowCount,
    ISNULL(col_count.ColumnCount, 0) AS ColumnCount,
    ISNULL(idx_count.IndexCount, 0) AS IndexCount,
    ISNULL(fk_count.ForeignKeyCount, 0) AS ForeignKeyCount,
    ISNULL(CAST(ps.data_size_kb / 1024.0 AS DECIMAL(18, 2)), 0) AS DataSizeMB,
    ISNULL(CAST(ps.index_size_kb / 1024.0 AS DECIMAL(18, 2)), 0) AS IndexSizeMB,
    ISNULL(CAST((ps.data_size_kb + ps.index_size_kb) / 1024.0 AS DECIMAL(18, 2)), 0) AS TotalSizeMB
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
-- 概估資料列數與空間
LEFT JOIN (
    SELECT
        object_id,
        SUM(CASE WHEN index_id IN (0, 1) THEN row_count ELSE 0 END) AS row_count,
        SUM(CASE WHEN index_id IN (0, 1) THEN in_row_data_page_count ELSE 0 END) * 8 AS data_size_kb,
        SUM(CASE WHEN index_id > 1 THEN used_page_count ELSE 0 END) * 8 AS index_size_kb
    FROM sys.dm_db_partition_stats
    GROUP BY object_id
) ps ON t.object_id = ps.object_id
-- 欄位數
LEFT JOIN (
    SELECT object_id, COUNT(*) AS ColumnCount
    FROM sys.columns
    GROUP BY object_id
) col_count ON t.object_id = col_count.object_id
-- 索引數（排除 heap，index_id = 0）
LEFT JOIN (
    SELECT object_id, COUNT(*) AS IndexCount
    FROM sys.indexes
    WHERE index_id > 0
    GROUP BY object_id
) idx_count ON t.object_id = idx_count.object_id
-- 外鍵數
LEFT JOIN (
    SELECT parent_object_id, COUNT(*) AS ForeignKeyCount
    FROM sys.foreign_keys
    GROUP BY parent_object_id
) fk_count ON t.object_id = fk_count.parent_object_id

UNION ALL

-- 檢視表統計
SELECT
    s.name AS SchemaName,
    v.name AS TableName,
    'VIEW' AS ObjectType,
    0 AS ApproximateRowCount,
    ISNULL(col_count.ColumnCount, 0) AS ColumnCount,
    ISNULL(idx_count.IndexCount, 0) AS IndexCount,
    0 AS ForeignKeyCount,
    0 AS DataSizeMB,
    0 AS IndexSizeMB,
    0 AS TotalSizeMB
FROM sys.views v
INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
LEFT JOIN (
    SELECT object_id, COUNT(*) AS ColumnCount
    FROM sys.columns
    GROUP BY object_id
) col_count ON v.object_id = col_count.object_id
-- 索引化檢視表可能有索引
LEFT JOIN (
    SELECT object_id, COUNT(*) AS IndexCount
    FROM sys.indexes
    WHERE index_id > 0
    GROUP BY object_id
) idx_count ON v.object_id = idx_count.object_id

ORDER BY SchemaName, TableName;";

        var result = await connection.QueryAsync<TableStatisticsInfo>(
            new CommandDefinition(sql, cancellationToken: ct));
        return result.ToList();
    }

    /// <inheritdoc/>
    public async Task<long> GetExactRowCountAsync(
        string schemaName, string tableName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return 0;

        // 驗證名稱不含特殊字元，防止 SQL Injection
        if (schemaName.Contains(']') || tableName.Contains(']'))
            throw new ArgumentException("Schema 或 Table 名稱包含不合法字元");

        await using var connection = new SqlConnection(connectionString);

        // 使用方括號引號保護名稱
        var sql = $"SELECT COUNT_BIG(*) FROM [{schemaName}].[{tableName}]";

        var result = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, commandTimeout: 300, cancellationToken: ct));
        return result;
    }
}
