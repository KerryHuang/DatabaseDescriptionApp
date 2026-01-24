using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// SQL Server Schema 收集器
/// </summary>
public class MssqlSchemaCollector : ISchemaCollector
{
    /// <inheritdoc />
    public async Task<DatabaseSchema> CollectAsync(
        string connectionString,
        string connectionName,
        CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var schema = new DatabaseSchema
        {
            ConnectionName = connectionName,
            DatabaseName = connection.Database,
            ServerName = connection.DataSource,
            CollectedAt = DateTime.Now
        };

        // 收集表格
        schema.Tables = await CollectTablesAsync(connection, ct);

        // 收集程式物件
        schema.Views = await CollectProgramObjectsAsync(connection, ProgramObjectType.View, ct);
        schema.StoredProcedures = await CollectProgramObjectsAsync(connection, ProgramObjectType.StoredProcedure, ct);
        schema.Functions = await CollectProgramObjectsAsync(connection, ProgramObjectType.Function, ct);
        schema.Triggers = await CollectProgramObjectsAsync(connection, ProgramObjectType.Trigger, ct);

        return schema;
    }

    #region 表格收集

    private async Task<IList<SchemaTable>> CollectTablesAsync(SqlConnection connection, CancellationToken ct)
    {
        const string tableSql = @"
SELECT
    s.name AS [Schema],
    t.name AS Name
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%'
ORDER BY s.name, t.name";

        var tables = (await connection.QueryAsync<(string Schema, string Name)>(tableSql)).ToList();
        var result = new List<SchemaTable>();

        foreach (var (tableSchema, tableName) in tables)
        {
            var table = new SchemaTable
            {
                Schema = tableSchema,
                Name = tableName,
                Columns = await CollectColumnsAsync(connection, tableSchema, tableName, ct),
                Indexes = await CollectIndexesAsync(connection, tableSchema, tableName, ct),
                Constraints = await CollectConstraintsAsync(connection, tableSchema, tableName, ct)
            };
            result.Add(table);
        }

        return result;
    }

    #endregion

    #region 欄位收集

    private async Task<IList<SchemaColumn>> CollectColumnsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    c.name AS Name,
    t.name AS DataType,
    CASE
        WHEN t.name IN ('nvarchar', 'nchar') AND c.max_length > 0 THEN c.max_length / 2
        WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary') THEN c.max_length
        WHEN c.max_length = -1 THEN -1
        ELSE NULL
    END AS MaxLength,
    c.precision AS [Precision],
    c.scale AS Scale,
    c.is_nullable AS IsNullable,
    OBJECT_DEFINITION(c.default_object_id) AS DefaultValue,
    c.is_identity AS IsIdentity,
    c.collation_name AS Collation
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
JOIN sys.tables tbl ON c.object_id = tbl.object_id
JOIN sys.schemas s ON tbl.schema_id = s.schema_id
WHERE s.name = @Schema AND tbl.name = @TableName
ORDER BY c.column_id";

        var columns = await connection.QueryAsync<SchemaColumn>(sql, new { Schema = schema, TableName = tableName });
        return columns.ToList();
    }

    #endregion

    #region 索引收集

    private async Task<IList<SchemaIndex>> CollectIndexesAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    i.name AS Name,
    i.type_desc AS TypeDesc,
    i.is_unique AS IsUnique,
    i.filter_definition AS FilterDefinition,
    i.index_id AS IndexId
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema
  AND t.name = @TableName
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
ORDER BY i.name";

        var indexInfos = await connection.QueryAsync<(string Name, string TypeDesc, bool IsUnique, string? FilterDefinition, int IndexId)>(
            sql, new { Schema = schema, TableName = tableName });

        var result = new List<SchemaIndex>();

        foreach (var info in indexInfos)
        {
            var columns = await GetIndexColumnsAsync(connection, schema, tableName, info.IndexId, false, ct);
            var includeColumns = await GetIndexColumnsAsync(connection, schema, tableName, info.IndexId, true, ct);

            result.Add(new SchemaIndex
            {
                Name = info.Name,
                IsClustered = info.TypeDesc == "CLUSTERED",
                IsUnique = info.IsUnique,
                Columns = columns,
                IncludeColumns = includeColumns,
                FilterDefinition = info.FilterDefinition
            });
        }

        return result;
    }

    private async Task<IList<string>> GetIndexColumnsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        int indexId,
        bool includeColumns,
        CancellationToken ct)
    {
        const string sql = @"
SELECT c.name
FROM sys.index_columns ic
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema
  AND t.name = @TableName
  AND i.index_id = @IndexId
  AND ic.is_included_column = @IsIncluded
ORDER BY ic.key_ordinal";

        var columns = await connection.QueryAsync<string>(sql, new
        {
            Schema = schema,
            TableName = tableName,
            IndexId = indexId,
            IsIncluded = includeColumns
        });

        return columns.ToList();
    }

    #endregion

    #region 約束收集

    private async Task<IList<SchemaConstraint>> CollectConstraintsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        var result = new List<SchemaConstraint>();

        // 收集主鍵
        var pk = await CollectPrimaryKeyAsync(connection, schema, tableName, ct);
        if (pk != null) result.Add(pk);

        // 收集外鍵
        result.AddRange(await CollectForeignKeysAsync(connection, schema, tableName, ct));

        // 收集唯一約束
        result.AddRange(await CollectUniqueConstraintsAsync(connection, schema, tableName, ct));

        // 收集 Check 約束
        result.AddRange(await CollectCheckConstraintsAsync(connection, schema, tableName, ct));

        // 收集 Default 約束
        result.AddRange(await CollectDefaultConstraintsAsync(connection, schema, tableName, ct));

        return result;
    }

    private async Task<SchemaConstraint?> CollectPrimaryKeyAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    kc.name AS Name,
    STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
FROM sys.key_constraints kc
JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON kc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema AND t.name = @TableName AND kc.type = 'PK'
GROUP BY kc.name";

        var pk = await connection.QueryFirstOrDefaultAsync<(string Name, string Columns)?>(
            sql, new { Schema = schema, TableName = tableName });

        if (pk == null) return null;

        return new SchemaConstraint
        {
            Name = pk.Value.Name,
            ConstraintType = ConstraintType.PrimaryKey,
            Columns = pk.Value.Columns.Split(',').ToList()
        };
    }

    private async Task<IList<SchemaConstraint>> CollectForeignKeysAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    fk.name AS Name,
    STRING_AGG(pc.name, ',') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS Columns,
    rs.name AS ReferencedSchema,
    rt.name AS ReferencedTable,
    STRING_AGG(rc.name, ',') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS ReferencedColumns,
    fk.delete_referential_action_desc AS OnDelete,
    fk.update_referential_action_desc AS OnUpdate
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
WHERE ps.name = @Schema AND pt.name = @TableName
GROUP BY fk.name, rs.name, rt.name, fk.delete_referential_action_desc, fk.update_referential_action_desc";

        var fks = await connection.QueryAsync<(string Name, string Columns, string ReferencedSchema, string ReferencedTable, string ReferencedColumns, string OnDelete, string OnUpdate)>(
            sql, new { Schema = schema, TableName = tableName });

        return fks.Select(fk => new SchemaConstraint
        {
            Name = fk.Name,
            ConstraintType = ConstraintType.ForeignKey,
            Columns = fk.Columns.Split(',').ToList(),
            ReferencedTable = $"[{fk.ReferencedSchema}].[{fk.ReferencedTable}]",
            ReferencedColumns = fk.ReferencedColumns.Split(',').ToList(),
            OnDeleteAction = fk.OnDelete,
            OnUpdateAction = fk.OnUpdate
        }).ToList();
    }

    private async Task<IList<SchemaConstraint>> CollectUniqueConstraintsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    kc.name AS Name,
    STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
FROM sys.key_constraints kc
JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON kc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema AND t.name = @TableName AND kc.type = 'UQ'
GROUP BY kc.name";

        var uqs = await connection.QueryAsync<(string Name, string Columns)>(
            sql, new { Schema = schema, TableName = tableName });

        return uqs.Select(uq => new SchemaConstraint
        {
            Name = uq.Name,
            ConstraintType = ConstraintType.Unique,
            Columns = uq.Columns.Split(',').ToList()
        }).ToList();
    }

    private async Task<IList<SchemaConstraint>> CollectCheckConstraintsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    cc.name AS Name,
    cc.definition AS Definition
FROM sys.check_constraints cc
JOIN sys.tables t ON cc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema AND t.name = @TableName";

        var checks = await connection.QueryAsync<(string Name, string Definition)>(
            sql, new { Schema = schema, TableName = tableName });

        return checks.Select(c => new SchemaConstraint
        {
            Name = c.Name,
            ConstraintType = ConstraintType.Check,
            Definition = c.Definition
        }).ToList();
    }

    private async Task<IList<SchemaConstraint>> CollectDefaultConstraintsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    dc.name AS Name,
    c.name AS ColumnName,
    dc.definition AS Definition
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
JOIN sys.tables t ON dc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @Schema AND t.name = @TableName";

        var defaults = await connection.QueryAsync<(string Name, string ColumnName, string Definition)>(
            sql, new { Schema = schema, TableName = tableName });

        return defaults.Select(d => new SchemaConstraint
        {
            Name = d.Name,
            ConstraintType = ConstraintType.Default,
            Columns = new List<string> { d.ColumnName },
            Definition = d.Definition
        }).ToList();
    }

    #endregion

    #region 程式物件收集

    private async Task<IList<SchemaProgramObject>> CollectProgramObjectsAsync(
        SqlConnection connection,
        ProgramObjectType objectType,
        CancellationToken ct)
    {
        var (typeFilter, objType) = objectType switch
        {
            ProgramObjectType.View => ("type = 'V'", "VIEW"),
            ProgramObjectType.StoredProcedure => ("type = 'P'", "PROCEDURE"),
            ProgramObjectType.Function => ("type IN ('FN', 'IF', 'TF', 'AF')", "FUNCTION"),
            ProgramObjectType.Trigger => ("type = 'TR'", "TRIGGER"),
            _ => throw new ArgumentException($"不支援的程式物件類型：{objectType}")
        };

        var sql = $@"
SELECT
    s.name AS [Schema],
    o.name AS Name,
    OBJECT_DEFINITION(o.object_id) AS Definition
FROM sys.objects o
JOIN sys.schemas s ON o.schema_id = s.schema_id
WHERE {typeFilter}
ORDER BY s.name, o.name";

        var objects = await connection.QueryAsync<(string Schema, string Name, string? Definition)>(sql);

        return objects.Select(o => new SchemaProgramObject
        {
            Schema = o.Schema,
            Name = o.Name,
            ObjectType = objectType,
            Definition = o.Definition
        }).ToList();
    }

    #endregion
}
