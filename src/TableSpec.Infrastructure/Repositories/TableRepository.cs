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
        T.TABLE_TYPE AS Type,
        T.TABLE_SCHEMA AS [Schema],
        T.TABLE_NAME AS Name,
        CAST(P.value AS NVARCHAR(MAX)) AS Description
    FROM
        INFORMATION_SCHEMA.TABLES T
    LEFT JOIN
        sys.schemas S ON T.TABLE_SCHEMA = S.name
    LEFT JOIN
        sys.objects O ON T.TABLE_NAME = O.name AND S.schema_id = O.schema_id
    LEFT JOIN
        sys.extended_properties P ON O.object_id = P.major_id AND P.minor_id = 0 AND P.name = 'MS_Description'

    UNION ALL

    -- 查詢 Stored Procedure 和 Function 的描述資訊
    SELECT
        T.ROUTINE_TYPE AS Type,
        T.ROUTINE_SCHEMA AS [Schema],
        T.ROUTINE_NAME AS Name,
        CAST(P.value AS NVARCHAR(MAX)) AS Description
    FROM
        INFORMATION_SCHEMA.ROUTINES T
    LEFT JOIN
        sys.schemas S ON T.ROUTINE_SCHEMA = S.name
    LEFT JOIN
        sys.objects O ON T.ROUTINE_NAME = O.name AND S.schema_id = O.schema_id
    LEFT JOIN
        sys.extended_properties P ON O.object_id = P.major_id AND P.minor_id = 0 AND P.name = 'MS_Description'
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

        string sql;

        if (type is "PROCEDURE" or "FUNCTION")
        {
            sql = @"
SELECT
    T.ROUTINE_TYPE AS Type,
    T.ROUTINE_SCHEMA AS [Schema],
    T.ROUTINE_NAME AS Name,
    CAST(P.value AS NVARCHAR(MAX)) AS Description
FROM
    INFORMATION_SCHEMA.ROUTINES T
LEFT JOIN
    sys.schemas S ON T.ROUTINE_SCHEMA = S.name
LEFT JOIN
    sys.objects O ON T.ROUTINE_NAME = O.name AND S.schema_id = O.schema_id
LEFT JOIN
    sys.extended_properties P ON O.object_id = P.major_id AND P.minor_id = 0 AND P.name = 'MS_Description'
WHERE
    T.ROUTINE_TYPE = @Type
ORDER BY
    T.ROUTINE_SCHEMA,
    T.ROUTINE_NAME";
        }
        else
        {
            sql = @"
SELECT
    T.TABLE_TYPE AS Type,
    T.TABLE_SCHEMA AS [Schema],
    T.TABLE_NAME AS Name,
    CAST(P.value AS NVARCHAR(MAX)) AS Description
FROM
    INFORMATION_SCHEMA.TABLES T
LEFT JOIN
    sys.schemas S ON T.TABLE_SCHEMA = S.name
LEFT JOIN
    sys.objects O ON T.TABLE_NAME = O.name AND S.schema_id = O.schema_id
LEFT JOIN
    sys.extended_properties P ON O.object_id = P.major_id AND P.minor_id = 0 AND P.name = 'MS_Description'
WHERE
    T.TABLE_TYPE = @Type
    AND T.TABLE_NAME NOT LIKE '%diagram%'
ORDER BY
    T.TABLE_SCHEMA,
    T.TABLE_NAME";
        }

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<TableInfo>(sql, new { Type = type });
        return result.ToList();
    }
}
