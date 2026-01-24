using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 資料庫物件查詢 Repository 實作
/// </summary>
public class TableRepository : ITableRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public TableRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<TableInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableInfo>();

        const string sql = @"
SELECT *
FROM (
    -- 查詢資料表的描述資訊
    SELECT
        'BASE TABLE' AS Type,
        s.name AS [Schema],
        t.name AS Name,
        CAST(ep.value AS NVARCHAR(MAX)) AS Description
    FROM
        sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    LEFT JOIN sys.extended_properties ep
        ON ep.major_id = t.object_id
        AND ep.minor_id = 0
        AND ep.name = 'MS_Description'

    UNION ALL

    -- 查詢檢視表的描述資訊
    SELECT
        'VIEW' AS Type,
        s.name AS [Schema],
        v.name AS Name,
        CAST(ep.value AS NVARCHAR(MAX)) AS Description
    FROM
        sys.views v
    JOIN sys.schemas s ON v.schema_id = s.schema_id
    LEFT JOIN sys.extended_properties ep
        ON ep.major_id = v.object_id
        AND ep.minor_id = 0
        AND ep.name = 'MS_Description'

    UNION ALL

    -- 查詢 Stored Procedure 的描述資訊
    SELECT
        'PROCEDURE' AS Type,
        s.name AS [Schema],
        p.name AS Name,
        CAST(ep.value AS NVARCHAR(MAX)) AS Description
    FROM
        sys.procedures p
    JOIN sys.schemas s ON p.schema_id = s.schema_id
    LEFT JOIN sys.extended_properties ep
        ON ep.major_id = p.object_id
        AND ep.minor_id = 0
        AND ep.name = 'MS_Description'

    UNION ALL

    -- 查詢 Function 的描述資訊
    SELECT
        'FUNCTION' AS Type,
        s.name AS [Schema],
        o.name AS Name,
        CAST(ep.value AS NVARCHAR(MAX)) AS Description
    FROM
        sys.objects o
    JOIN sys.schemas s ON o.schema_id = s.schema_id
    LEFT JOIN sys.extended_properties ep
        ON ep.major_id = o.object_id
        AND ep.minor_id = 0
        AND ep.name = 'MS_Description'
    WHERE o.type IN ('FN', 'IF', 'TF', 'AF')  -- Scalar, Inline Table, Table-valued, Aggregate functions
) T
WHERE T.Name NOT LIKE '%diagram%'
ORDER BY
    T.Type,
    T.[Schema],
    T.Name";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<TableInfo>(sql);
        return result.ToList();
    }

    public async Task<IReadOnlyList<TableInfo>> GetByTypeAsync(string type, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableInfo>();

        string sql = type switch
        {
            "BASE TABLE" => @"
SELECT
    'BASE TABLE' AS Type,
    s.name AS [Schema],
    t.name AS Name,
    CAST(ep.value AS NVARCHAR(MAX)) AS Description
FROM
    sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
WHERE t.name NOT LIKE '%diagram%'
ORDER BY s.name, t.name",

            "VIEW" => @"
SELECT
    'VIEW' AS Type,
    s.name AS [Schema],
    v.name AS Name,
    CAST(ep.value AS NVARCHAR(MAX)) AS Description
FROM
    sys.views v
JOIN sys.schemas s ON v.schema_id = s.schema_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = v.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
ORDER BY s.name, v.name",

            "PROCEDURE" => @"
SELECT
    'PROCEDURE' AS Type,
    s.name AS [Schema],
    p.name AS Name,
    CAST(ep.value AS NVARCHAR(MAX)) AS Description
FROM
    sys.procedures p
JOIN sys.schemas s ON p.schema_id = s.schema_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = p.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
ORDER BY s.name, p.name",

            "FUNCTION" => @"
SELECT
    'FUNCTION' AS Type,
    s.name AS [Schema],
    o.name AS Name,
    CAST(ep.value AS NVARCHAR(MAX)) AS Description
FROM
    sys.objects o
JOIN sys.schemas s ON o.schema_id = s.schema_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
WHERE o.type IN ('FN', 'IF', 'TF', 'AF')
ORDER BY s.name, o.name",

            _ => throw new ArgumentException($"Unknown type: {type}")
        };

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<TableInfo>(sql);
        return result.ToList();
    }
}
