using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 欄位查詢 Repository 實作
/// </summary>
public class ColumnRepository : IColumnRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public ColumnRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        string type,
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ColumnInfo>();

        string sql = type switch
        {
            "PROCEDURE" or "FUNCTION" => GetRoutineParametersSql(),
            "VIEW" => GetViewColumnsSql(),
            _ => GetTableColumnsSql()
        };

        await using var connection = new SqlConnection(connectionString);

        if (type is "PROCEDURE" or "FUNCTION")
        {
            var result = await connection.QueryAsync<ColumnInfo>(sql, new
            {
                Schema = schema,
                ObjectName = tableName
            });
            return result.ToList();
        }
        else
        {
            var result = await connection.QueryAsync<ColumnInfo>(sql, new
            {
                Type = type,
                Schema = schema,
                TableName = tableName
            });
            return result.ToList();
        }
    }

    private static string GetTableColumnsSql() => @"
SELECT
    a.TABLE_SCHEMA AS [Schema],
    a.TABLE_NAME AS TableName,
    b.COLUMN_NAME AS ColumnName,
    b.DATA_TYPE AS DataType,
    CASE
        WHEN ISNULL(b.CHARACTER_MAXIMUM_LENGTH, 0) > 0 THEN b.CHARACTER_MAXIMUM_LENGTH
        WHEN ISNULL(b.CHARACTER_MAXIMUM_LENGTH, 0) = -1 THEN -1
        ELSE NULL
    END AS Length,
    b.COLUMN_DEFAULT AS DefaultValue,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE c
            WHERE b.TABLE_SCHEMA = c.TABLE_SCHEMA
                AND b.TABLE_NAME = c.TABLE_NAME
                AND b.COLUMN_NAME = c.COLUMN_NAME
                AND LEFT(c.CONSTRAINT_NAME, 2) = 'PK'
        ) THEN 1
        ELSE 0
    END AS IsPrimaryKey,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE c
            WHERE b.TABLE_SCHEMA = c.TABLE_SCHEMA
                AND b.TABLE_NAME = c.TABLE_NAME
                AND b.COLUMN_NAME = c.COLUMN_NAME
                AND LEFT(c.CONSTRAINT_NAME, 2) IN ('IX', 'UK')
        ) THEN 1
        ELSE 0
    END AS IsUniqueKey,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE c.name = b.COLUMN_NAME
                AND i.object_id = OBJECT_ID(a.TABLE_SCHEMA + '.' + a.TABLE_NAME)
        ) THEN 1
        ELSE 0
    END AS IsIndexed,
    CASE WHEN b.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
    (
        SELECT value
        FROM fn_listextendedproperty(
            NULL,
            'schema', a.TABLE_SCHEMA,
            'table', a.TABLE_NAME,
            'column', DEFAULT
        )
        WHERE name = 'MS_Description'
            AND objtype = 'COLUMN'
            AND objname COLLATE Chinese_Taiwan_Stroke_CI_AS = b.COLUMN_NAME
    ) AS Description
FROM
    INFORMATION_SCHEMA.TABLES a
LEFT JOIN
    INFORMATION_SCHEMA.COLUMNS b ON a.TABLE_SCHEMA = b.TABLE_SCHEMA AND a.TABLE_NAME = b.TABLE_NAME
WHERE
    a.TABLE_TYPE = @Type
    AND a.TABLE_SCHEMA = @Schema
    AND a.TABLE_NAME = @TableName
ORDER BY
    b.ORDINAL_POSITION";

    private static string GetViewColumnsSql() => @"
SELECT
    s.name AS [Schema],
    v.name AS TableName,
    c.name AS ColumnName,
    t.name AS DataType,
    CASE
        WHEN c.max_length > 0 THEN c.max_length
        WHEN c.max_length = -1 THEN -1
        ELSE NULL
    END AS Length,
    NULL AS DefaultValue,
    0 AS IsPrimaryKey,
    0 AS IsUniqueKey,
    0 AS IsIndexed,
    c.is_nullable AS IsNullable,
    CAST(ep.value AS NVARCHAR(MAX)) AS Description
FROM
    sys.views v
JOIN sys.schemas s ON v.schema_id = s.schema_id
JOIN sys.columns c ON v.object_id = c.object_id
JOIN sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = v.object_id
    AND ep.minor_id = c.column_id
    AND ep.name = 'MS_Description'
WHERE
    s.name = @Schema
    AND v.name = @TableName
ORDER BY
    c.column_id";

    private static string GetRoutineParametersSql() => @"
SELECT
    a.SPECIFIC_SCHEMA AS [Schema],
    a.SPECIFIC_NAME AS TableName,
    a.PARAMETER_NAME AS ColumnName,
    a.DATA_TYPE AS DataType,
    CASE
        WHEN ISNULL(a.CHARACTER_MAXIMUM_LENGTH, 0) > 0 THEN a.CHARACTER_MAXIMUM_LENGTH
        WHEN ISNULL(a.CHARACTER_MAXIMUM_LENGTH, 0) = -1 THEN -1
        ELSE NULL
    END AS Length,
    NULL AS DefaultValue,
    0 AS IsPrimaryKey,
    0 AS IsUniqueKey,
    0 AS IsIndexed,
    1 AS IsNullable,
    a.PARAMETER_MODE AS Description
FROM
    INFORMATION_SCHEMA.PARAMETERS a
WHERE
    a.SPECIFIC_SCHEMA = @Schema
    AND a.SPECIFIC_NAME = @ObjectName
ORDER BY
    a.ORDINAL_POSITION";

    public async Task UpdateColumnDescriptionAsync(
        string schema,
        string objectName,
        string columnName,
        string? description,
        string objectType = "TABLE",
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 驗證 objectType 只能是 TABLE 或 VIEW
        var level1Type = objectType.ToUpperInvariant() switch
        {
            "TABLE" => "TABLE",
            "VIEW" => "VIEW",
            _ => "TABLE"
        };

        // 先檢查是否已存在說明
        const string checkSql = @"
            SELECT COUNT(*)
            FROM sys.extended_properties ep
            INNER JOIN sys.objects o ON ep.major_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
            WHERE s.name = @Schema
                AND o.name = @ObjectName
                AND c.name = @ColumnName
                AND ep.name = 'MS_Description'";

        var exists = await connection.ExecuteScalarAsync<int>(checkSql, new
        {
            Schema = schema,
            ObjectName = objectName,
            ColumnName = columnName
        });

        if (string.IsNullOrEmpty(description))
        {
            // 如果說明為空，刪除現有的說明
            if (exists > 0)
            {
                var dropSql = $@"
                    EXEC sp_dropextendedproperty
                        @name = N'MS_Description',
                        @level0type = N'SCHEMA', @level0name = @Schema,
                        @level1type = N'{level1Type}', @level1name = @ObjectName,
                        @level2type = N'COLUMN', @level2name = @ColumnName";

                await connection.ExecuteAsync(dropSql, new
                {
                    Schema = schema,
                    ObjectName = objectName,
                    ColumnName = columnName
                });
            }
        }
        else if (exists > 0)
        {
            // 更新現有的說明
            var updateSql = $@"
                EXEC sp_updateextendedproperty
                    @name = N'MS_Description',
                    @value = @Description,
                    @level0type = N'SCHEMA', @level0name = @Schema,
                    @level1type = N'{level1Type}', @level1name = @ObjectName,
                    @level2type = N'COLUMN', @level2name = @ColumnName";

            await connection.ExecuteAsync(updateSql, new
            {
                Description = description,
                Schema = schema,
                ObjectName = objectName,
                ColumnName = columnName
            });
        }
        else
        {
            // 新增說明
            var addSql = $@"
                EXEC sp_addextendedproperty
                    @name = N'MS_Description',
                    @value = @Description,
                    @level0type = N'SCHEMA', @level0name = @Schema,
                    @level1type = N'{level1Type}', @level1name = @ObjectName,
                    @level2type = N'COLUMN', @level2name = @ColumnName";

            await connection.ExecuteAsync(addSql, new
            {
                Description = description,
                Schema = schema,
                ObjectName = objectName,
                ColumnName = columnName
            });
        }
    }
}
