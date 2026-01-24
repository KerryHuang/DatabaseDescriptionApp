using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 索引查詢 Repository 實作
/// </summary>
public class IndexRepository : IIndexRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public IndexRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<IndexInfo>();

        const string sql = @"
SELECT
    i.name AS Name,
    CASE
        WHEN i.type = 1 THEN 'Clustered'
        WHEN i.type = 2 THEN 'NonClustered'
        WHEN i.type = 3 THEN 'XML'
        WHEN i.type = 4 THEN 'Spatial'
        WHEN i.type = 5 THEN 'Clustered columnstore'
        WHEN i.type = 6 THEN 'NonClustered columnstore'
        WHEN i.type = 7 THEN 'NonClustered hash'
        ELSE 'Other'
    END AS Type,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    STUFF((
        SELECT ', ' + c.name
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
        ORDER BY ic.key_ordinal
        FOR XML PATH('')
    ), 1, 2, '') AS ColumnsString
FROM
    sys.indexes i
INNER JOIN
    sys.tables t ON i.object_id = t.object_id
INNER JOIN
    sys.schemas s ON t.schema_id = s.schema_id
WHERE
    s.name = @Schema
    AND t.name = @TableName
    AND i.name IS NOT NULL
ORDER BY
    i.is_primary_key DESC,
    i.name";

        await using var connection = new SqlConnection(connectionString);
        var rawResult = await connection.QueryAsync<IndexRawDto>(sql, new
        {
            Schema = schema,
            TableName = tableName
        });

        return rawResult.Select(r => new IndexInfo
        {
            Name = r.Name,
            Type = r.Type,
            IsUnique = r.IsUnique,
            IsPrimaryKey = r.IsPrimaryKey,
            Columns = r.ColumnsString?.Split(", ", StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>()
        }).ToList();
    }

    private class IndexRawDto
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public string? ColumnsString { get; init; }
    }
}
