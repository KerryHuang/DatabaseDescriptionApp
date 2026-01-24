using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 欄位型態查詢與更新 Repository 實作
/// </summary>
public class ColumnTypeRepository : IColumnTypeRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public ColumnTypeRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<ColumnTypeInfo>> GetColumnTypesAsync(
        string columnName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ColumnTypeInfo>();

        const string sql = @"
            SELECT
                c.name AS ColumnName,
                SCHEMA_NAME(t.schema_id) AS SchemaName,
                t.name AS TableName,
                TYPE_NAME(c.user_type_id) AS BaseType,
                c.max_length AS MaxLength,
                c.precision AS Precision,
                c.scale AS Scale,
                c.is_nullable AS IsNullable,
                TYPE_NAME(c.user_type_id) +
                    CASE
                        WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'char', 'varbinary', 'binary')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar', 'nchar')
                            THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS VARCHAR) END + ')'
                        WHEN TYPE_NAME(c.user_type_id) IN ('decimal', 'numeric')
                            THEN '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataType
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            WHERE c.name = @ColumnName
            ORDER BY SchemaName, TableName";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;
        command.Parameters.AddWithValue("@ColumnName", columnName);

        var results = new List<ColumnTypeInfo>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var info = new ColumnTypeInfo
            {
                ColumnName = reader.GetString(0),
                SchemaName = reader.GetString(1),
                TableName = reader.GetString(2),
                BaseType = reader.GetString(3),
                MaxLength = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                IsNullable = reader.GetBoolean(7),
                DataType = reader.GetString(8)
            };
            results.Add(info);
        }

        // 為每個欄位載入約束
        foreach (var info in results)
        {
            var constraints = await GetColumnConstraintsAsync(
                info.SchemaName, info.TableName, info.ColumnName, ct);
            info.Constraints = constraints.ToList();
        }

        return results;
    }

    public async Task<IReadOnlyList<ConstraintInfo>> GetColumnConstraintsAsync(
        string schema, string table, string column, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ConstraintInfo>();

        var constraints = new List<ConstraintInfo>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 1. 取得 PK 約束
        await LoadPrimaryKeyConstraintsAsync(connection, schema, table, column, constraints, ct);

        // 2. 取得 FK 約束（此欄位作為外鍵）
        await LoadForeignKeyConstraintsAsync(connection, schema, table, column, constraints, ct);

        // 3. 取得被參考的 FK 約束（其他資料表參考此欄位）
        await LoadReferencedForeignKeyConstraintsAsync(connection, schema, table, column, constraints, ct);

        // 4. 取得 Index 約束
        await LoadIndexConstraintsAsync(connection, schema, table, column, constraints, ct);

        return constraints;
    }

    private static async Task LoadPrimaryKeyConstraintsAsync(
        SqlConnection connection, string schema, string table, string column,
        List<ConstraintInfo> constraints, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                kc.name AS ConstraintName,
                i.is_unique AS IsUnique,
                CASE i.type WHEN 1 THEN 1 ELSE 0 END AS IsClustered
            FROM sys.key_constraints kc
            INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id
                AND kc.unique_index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON kc.parent_object_id = t.object_id
            INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE kc.type = 'PK'
                AND SCHEMA_NAME(t.schema_id) = @Schema
                AND t.name = @Table
                AND c.name = @Column";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);
        command.Parameters.AddWithValue("@Column", column);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var constraintName = reader.GetString(0);
            var isUnique = Convert.ToBoolean(reader.GetValue(1));
            var isClustered = Convert.ToBoolean(reader.GetValue(2));

            constraints.Add(new ConstraintInfo
            {
                ConstraintName = constraintName,
                Type = ConstraintType.PrimaryKey,
                SchemaName = schema,
                TableName = table,
                Columns = [column],
                IsUnique = isUnique,
                IsClustered = isClustered,
                DropSql = $"ALTER TABLE [{schema}].[{table}] DROP CONSTRAINT [{constraintName}]",
                CreateSql = $"ALTER TABLE [{schema}].[{table}] ADD CONSTRAINT [{constraintName}] PRIMARY KEY {(isClustered ? "CLUSTERED" : "NONCLUSTERED")} ([{column}])"
            });
        }
    }

    private static async Task LoadForeignKeyConstraintsAsync(
        SqlConnection connection, string schema, string table, string column,
        List<ConstraintInfo> constraints, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                fk.name AS ConstraintName,
                SCHEMA_NAME(rt.schema_id) AS ReferencedSchema,
                rt.name AS ReferencedTable,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
            INNER JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
            WHERE SCHEMA_NAME(t.schema_id) = @Schema
                AND t.name = @Table
                AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = @Column";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);
        command.Parameters.AddWithValue("@Column", column);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var constraintName = reader.GetString(0);
            var referencedSchema = reader.GetString(1);
            var referencedTable = reader.GetString(2);
            var referencedColumn = reader.GetString(3);

            constraints.Add(new ConstraintInfo
            {
                ConstraintName = constraintName,
                Type = ConstraintType.ForeignKey,
                SchemaName = schema,
                TableName = table,
                Columns = [column],
                ReferencedSchema = referencedSchema,
                ReferencedTable = referencedTable,
                ReferencedColumn = referencedColumn,
                DropSql = $"ALTER TABLE [{schema}].[{table}] DROP CONSTRAINT [{constraintName}]",
                CreateSql = $"ALTER TABLE [{schema}].[{table}] ADD CONSTRAINT [{constraintName}] FOREIGN KEY ([{column}]) REFERENCES [{referencedSchema}].[{referencedTable}] ([{referencedColumn}])"
            });
        }
    }

    private static async Task LoadReferencedForeignKeyConstraintsAsync(
        SqlConnection connection, string schema, string table, string column,
        List<ConstraintInfo> constraints, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                fk.name AS ConstraintName,
                SCHEMA_NAME(pt.schema_id) AS ParentSchema,
                pt.name AS ParentTable,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ParentColumn
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
            INNER JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
            WHERE SCHEMA_NAME(rt.schema_id) = @Schema
                AND rt.name = @Table
                AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = @Column";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);
        command.Parameters.AddWithValue("@Column", column);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var constraintName = reader.GetString(0);
            var parentSchema = reader.GetString(1);
            var parentTable = reader.GetString(2);
            var parentColumn = reader.GetString(3);

            constraints.Add(new ConstraintInfo
            {
                ConstraintName = constraintName,
                Type = ConstraintType.ForeignKey,
                SchemaName = parentSchema,
                TableName = parentTable,
                Columns = [parentColumn],
                ReferencedSchema = schema,
                ReferencedTable = table,
                ReferencedColumn = column,
                DropSql = $"ALTER TABLE [{parentSchema}].[{parentTable}] DROP CONSTRAINT [{constraintName}]",
                CreateSql = $"ALTER TABLE [{parentSchema}].[{parentTable}] ADD CONSTRAINT [{constraintName}] FOREIGN KEY ([{parentColumn}]) REFERENCES [{schema}].[{table}] ([{column}])"
            });
        }
    }

    private static async Task LoadIndexConstraintsAsync(
        SqlConnection connection, string schema, string table, string column,
        List<ConstraintInfo> constraints, CancellationToken ct)
    {
        // 使用 FOR XML PATH 語法來聚合欄位名稱（相容於 SQL Server 2008+）
        const string sql = @"
            SELECT
                i.name AS IndexName,
                i.is_unique AS IsUnique,
                CASE i.type WHEN 1 THEN 1 ELSE 0 END AS IsClustered,
                STUFF((
                    SELECT ',' + c2.name
                    FROM sys.index_columns ic2
                    INNER JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
                    WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                    ORDER BY ic2.key_ordinal
                    FOR XML PATH('')
                ), 1, 1, '') AS Columns
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE i.is_primary_key = 0
                AND i.type > 0
                AND SCHEMA_NAME(t.schema_id) = @Schema
                AND t.name = @Table
                AND EXISTS (
                    SELECT 1 FROM sys.index_columns ic3
                    INNER JOIN sys.columns c3 ON ic3.object_id = c3.object_id AND ic3.column_id = c3.column_id
                    WHERE ic3.object_id = i.object_id AND ic3.index_id = i.index_id AND c3.name = @Column
                )";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);
        command.Parameters.AddWithValue("@Column", column);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var indexName = reader.GetString(0);
            var isUnique = Convert.ToBoolean(reader.GetValue(1));
            var isClustered = Convert.ToBoolean(reader.GetValue(2));
            var columnsStr = reader.IsDBNull(3) ? column : reader.GetString(3);
            var columns = columnsStr.Split(',').ToList();

            var columnList = string.Join("], [", columns);
            var indexType = isUnique ? "UNIQUE " : "";
            indexType += isClustered ? "CLUSTERED" : "NONCLUSTERED";

            constraints.Add(new ConstraintInfo
            {
                ConstraintName = indexName,
                Type = isUnique ? ConstraintType.UniqueIndex : ConstraintType.NonClusteredIndex,
                SchemaName = schema,
                TableName = table,
                Columns = columns,
                IsUnique = isUnique,
                IsClustered = isClustered,
                DropSql = $"DROP INDEX [{indexName}] ON [{schema}].[{table}]",
                CreateSql = $"CREATE {indexType} INDEX [{indexName}] ON [{schema}].[{table}] ([{columnList}])"
            });
        }
    }

    public async Task<bool> UpdateColumnLengthAsync(
        string schema, string table, string column,
        int newLength, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 取得欄位資訊
        var columnInfo = await GetColumnInfoAsync(connection, schema, table, column, ct);
        if (columnInfo == null)
            throw new InvalidOperationException($"找不到欄位 [{schema}].[{table}].[{column}]");

        if (!columnInfo.IsLengthChangeable)
            throw new InvalidOperationException($"欄位型態 {columnInfo.BaseType} 不支援長度變更");

        // 取得約束
        var constraints = await GetColumnConstraintsAsync(schema, table, column, ct);

        // 開始交易
        await using var transaction = connection.BeginTransaction();

        try
        {
            // 1. 依序 DROP 約束（FK → Index → PK）
            foreach (var constraint in constraints.OrderByDescending(c => (int)c.Type))
            {
                await ExecuteNonQueryAsync(connection, transaction, constraint.DropSql, ct);
            }

            // 2. ALTER COLUMN
            var lengthSpec = newLength == -1 ? "MAX" : newLength.ToString();
            var nullableSpec = columnInfo.IsNullable ? "NULL" : "NOT NULL";

            // 對於 nvarchar/nchar，長度是字元數而不是位元組數
            var alterSql = $"ALTER TABLE [{schema}].[{table}] ALTER COLUMN [{column}] {columnInfo.BaseType}({lengthSpec}) {nullableSpec}";
            await ExecuteNonQueryAsync(connection, transaction, alterSql, ct);

            // 3. 依序 CREATE 約束（PK → Index → FK）
            foreach (var constraint in constraints.OrderBy(c => (int)c.Type))
            {
                await ExecuteNonQueryAsync(connection, transaction, constraint.CreateSql, ct);
            }

            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> GetMaxDataLengthAsync(
        string schema, string table, string column, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return 0;

        var sql = $"SELECT ISNULL(MAX(LEN([{column}])), 0) FROM [{schema}].[{table}]";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 60;

        var result = await command.ExecuteScalarAsync(ct);
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    private static async Task<ColumnTypeInfo?> GetColumnInfoAsync(
        SqlConnection connection, string schema, string table, string column, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                c.name AS ColumnName,
                SCHEMA_NAME(t.schema_id) AS SchemaName,
                t.name AS TableName,
                TYPE_NAME(c.user_type_id) AS BaseType,
                c.max_length AS MaxLength,
                c.precision AS Precision,
                c.scale AS Scale,
                c.is_nullable AS IsNullable
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            WHERE SCHEMA_NAME(t.schema_id) = @Schema
                AND t.name = @Table
                AND c.name = @Column";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);
        command.Parameters.AddWithValue("@Column", column);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new ColumnTypeInfo
            {
                ColumnName = reader.GetString(0),
                SchemaName = reader.GetString(1),
                TableName = reader.GetString(2),
                BaseType = reader.GetString(3),
                MaxLength = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                IsNullable = reader.GetBoolean(7)
            };
        }

        return null;
    }

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync(ct);
    }
}
