using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 關聯查詢 Repository 實作
/// </summary>
public class RelationRepository : IRelationRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public RelationRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<RelationInfo>> GetRelationsAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<RelationInfo>();

        const string sql = @"
-- Outgoing: 本表參考其他表 (FK 在本表)
SELECT
    fk.name AS ConstraintName,
    SCHEMA_NAME(tp.schema_id) + '.' + tp.name AS FromTable,
    cp.name AS FromColumn,
    SCHEMA_NAME(tr.schema_id) + '.' + tr.name AS ToTable,
    cr.name AS ToColumn,
    0 AS Type  -- Outgoing
FROM
    sys.foreign_keys fk
INNER JOIN
    sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN
    sys.tables tp ON fkc.parent_object_id = tp.object_id
INNER JOIN
    sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
INNER JOIN
    sys.tables tr ON fkc.referenced_object_id = tr.object_id
INNER JOIN
    sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
WHERE
    SCHEMA_NAME(tp.schema_id) = @Schema
    AND tp.name = @TableName

UNION ALL

-- Incoming: 其他表參考本表 (被其他表的 FK 參考)
SELECT
    fk.name AS ConstraintName,
    SCHEMA_NAME(tp.schema_id) + '.' + tp.name AS FromTable,
    cp.name AS FromColumn,
    SCHEMA_NAME(tr.schema_id) + '.' + tr.name AS ToTable,
    cr.name AS ToColumn,
    1 AS Type  -- Incoming
FROM
    sys.foreign_keys fk
INNER JOIN
    sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN
    sys.tables tp ON fkc.parent_object_id = tp.object_id
INNER JOIN
    sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
INNER JOIN
    sys.tables tr ON fkc.referenced_object_id = tr.object_id
INNER JOIN
    sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
WHERE
    SCHEMA_NAME(tr.schema_id) = @Schema
    AND tr.name = @TableName

ORDER BY
    Type,
    ConstraintName";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<RelationRawDto>(sql, new
        {
            Schema = schema,
            TableName = tableName
        });

        return result.Select(r => new RelationInfo
        {
            ConstraintName = r.ConstraintName,
            FromTable = r.FromTable,
            FromColumn = r.FromColumn,
            ToTable = r.ToTable,
            ToColumn = r.ToColumn,
            Type = (RelationType)r.Type
        }).ToList();
    }

    private class RelationRawDto
    {
        public required string ConstraintName { get; init; }
        public required string FromTable { get; init; }
        public required string FromColumn { get; init; }
        public required string ToTable { get; init; }
        public required string ToColumn { get; init; }
        public int Type { get; init; }
    }
}
