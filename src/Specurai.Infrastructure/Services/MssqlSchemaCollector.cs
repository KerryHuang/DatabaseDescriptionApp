using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL Server Schema 收集器（批次查詢版，避免 N+1 問題）
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

        // 批次查詢所有資料，在記憶體分組組裝
        var (tables, columns, indexes, indexColumns, constraints) = await FetchAllTableDataAsync(connection, ct);
        schema.Tables = BuildTables(tables, columns, indexes, indexColumns, constraints);

        schema.Views = await CollectProgramObjectsAsync(connection, ProgramObjectType.View, ct);
        schema.StoredProcedures = await CollectProgramObjectsAsync(connection, ProgramObjectType.StoredProcedure, ct);
        schema.Functions = await CollectProgramObjectsAsync(connection, ProgramObjectType.Function, ct);
        schema.Triggers = await CollectProgramObjectsAsync(connection, ProgramObjectType.Trigger, ct);

        return schema;
    }

    #region 批次表格收集

    private record TableRow(string Schema, string Name);

    private record ColumnRow(
        string TableSchema, string TableName, string Name, string DataType,
        int? MaxLength, byte Precision, byte Scale, bool IsNullable,
        string? DefaultValue, bool IsIdentity, string? Collation);

    private record IndexRow(
        string TableSchema, string TableName, string IndexName,
        string TypeDesc, bool IsUnique, string? FilterDefinition, int IndexId);

    private record IndexColumnRow(
        string TableSchema, string TableName, int IndexId,
        string ColumnName, bool IsIncluded);

    private record ConstraintRow(
        string TableSchema, string TableName, string ConstraintName,
        string ConstraintType, string? Columns, string? ReferencedSchema,
        string? ReferencedTable, string? ReferencedColumns,
        string? OnDelete, string? OnUpdate, string? Definition, string? ColumnName);

    private static async Task<(
        IList<TableRow> Tables,
        IList<ColumnRow> Columns,
        IList<IndexRow> Indexes,
        IList<IndexColumnRow> IndexColumns,
        IList<ConstraintRow> Constraints)>
        FetchAllTableDataAsync(SqlConnection connection, CancellationToken ct)
    {
        // QueryMultiple：單一 round-trip 取回 5 個結果集
        using var multi = await connection.QueryMultipleAsync(@"
SELECT s.name AS Schema, t.name AS Name
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%'
ORDER BY s.name, t.name;

SELECT
    s.name AS TableSchema, tbl.name AS TableName,
    c.name AS Name, tp.name AS DataType,
    CASE
        WHEN tp.name IN ('nvarchar','nchar') AND c.max_length > 0 THEN c.max_length / 2
        WHEN tp.name IN ('varchar','char','varbinary','binary') THEN c.max_length
        WHEN c.max_length = -1 THEN -1
        ELSE NULL
    END AS MaxLength,
    c.precision AS Precision, c.scale AS Scale,
    c.is_nullable AS IsNullable,
    OBJECT_DEFINITION(c.default_object_id) AS DefaultValue,
    c.is_identity AS IsIdentity,
    c.collation_name AS Collation
FROM sys.columns c
JOIN sys.types tp ON c.user_type_id = tp.user_type_id
JOIN sys.tables tbl ON c.object_id = tbl.object_id
JOIN sys.schemas s ON tbl.schema_id = s.schema_id
WHERE tbl.name NOT LIKE '%diagram%'
ORDER BY s.name, tbl.name, c.column_id;

SELECT
    s.name AS TableSchema, t.name AS TableName,
    i.name AS IndexName, i.type_desc AS TypeDesc,
    i.is_unique AS IsUnique, i.filter_definition AS FilterDefinition,
    i.index_id AS IndexId
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%'
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
ORDER BY s.name, t.name, i.name;

SELECT
    s.name AS TableSchema, t.name AS TableName,
    i.index_id AS IndexId,
    c.name AS ColumnName,
    ic.is_included_column AS IsIncluded
FROM sys.index_columns ic
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%'
  AND i.name IS NOT NULL
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
ORDER BY s.name, t.name, i.index_id, ic.key_ordinal;

SELECT s.name AS TableSchema, t.name AS TableName,
    kc.name AS ConstraintName, 'PK' AS ConstraintType,
    STUFF((SELECT ',' + c2.name FROM sys.index_columns ic2
        JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
        WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id ORDER BY ic2.key_ordinal
        FOR XML PATH(''), TYPE).value('.','NVARCHAR(MAX)'),1,1,'') AS Columns,
    NULL AS ReferencedSchema, NULL AS ReferencedTable,
    NULL AS ReferencedColumns, NULL AS OnDelete, NULL AS OnUpdate,
    NULL AS Definition, NULL AS ColumnName
FROM sys.key_constraints kc
JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
JOIN sys.tables t ON kc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE kc.type = 'PK' AND t.name NOT LIKE '%diagram%'
UNION ALL
SELECT ps.name, pt.name, fk.name, 'FK',
    STUFF((SELECT ',' + pc2.name FROM sys.foreign_key_columns fkc2
        JOIN sys.columns pc2 ON fkc2.parent_object_id = pc2.object_id AND fkc2.parent_column_id = pc2.column_id
        WHERE fkc2.constraint_object_id = fk.object_id ORDER BY fkc2.constraint_column_id
        FOR XML PATH(''), TYPE).value('.','NVARCHAR(MAX)'),1,1,''),
    rs.name, rt.name,
    STUFF((SELECT ',' + rc2.name FROM sys.foreign_key_columns fkc2
        JOIN sys.columns rc2 ON fkc2.referenced_object_id = rc2.object_id AND fkc2.referenced_column_id = rc2.column_id
        WHERE fkc2.constraint_object_id = fk.object_id ORDER BY fkc2.constraint_column_id
        FOR XML PATH(''), TYPE).value('.','NVARCHAR(MAX)'),1,1,''),
    fk.delete_referential_action_desc, fk.update_referential_action_desc, NULL, NULL
FROM sys.foreign_keys fk
JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
WHERE pt.name NOT LIKE '%diagram%'
UNION ALL
SELECT s.name, t.name, kc.name, 'UQ',
    STUFF((SELECT ',' + c2.name FROM sys.index_columns ic2
        JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
        WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id ORDER BY ic2.key_ordinal
        FOR XML PATH(''), TYPE).value('.','NVARCHAR(MAX)'),1,1,''),
    NULL, NULL, NULL, NULL, NULL, NULL, NULL
FROM sys.key_constraints kc
JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
JOIN sys.tables t ON kc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE kc.type = 'UQ' AND t.name NOT LIKE '%diagram%'
UNION ALL
SELECT s.name, t.name, cc.name, 'CHK',
    NULL, NULL, NULL, NULL, NULL, NULL, cc.definition, NULL
FROM sys.check_constraints cc
JOIN sys.tables t ON cc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%'
UNION ALL
SELECT s.name, t.name, dc.name, 'DEF',
    NULL, NULL, NULL, NULL, NULL, NULL, dc.definition, c.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
JOIN sys.tables t ON dc.parent_object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.name NOT LIKE '%diagram%';");

        var tableRows = (await multi.ReadAsync<TableRow>()).ToList();
        var columnRows = (await multi.ReadAsync<ColumnRow>()).ToList();
        var indexRows = (await multi.ReadAsync<IndexRow>()).ToList();
        var indexColumnRows = (await multi.ReadAsync<IndexColumnRow>()).ToList();
        var constraintRows = (await multi.ReadAsync<ConstraintRow>()).ToList();

        return (
            tableRows,
            columnRows,
            indexRows,
            indexColumnRows,
            constraintRows
        );
    }

    private static IList<SchemaTable> BuildTables(
        IList<TableRow> tables,
        IList<ColumnRow> columns,
        IList<IndexRow> indexes,
        IList<IndexColumnRow> indexColumns,
        IList<ConstraintRow> constraints)
    {
        var columnsLookup = columns.ToLookup(c => (c.TableSchema, c.TableName));
        var indexesLookup = indexes.ToLookup(i => (i.TableSchema, i.TableName));
        var indexColumnsLookup = indexColumns.ToLookup(ic => (ic.TableSchema, ic.TableName, ic.IndexId));
        var constraintsLookup = constraints.ToLookup(c => (c.TableSchema, c.TableName));

        return tables.Select(t =>
        {
            var key = (t.Schema, t.Name);
            return new SchemaTable
            {
                Schema = t.Schema,
                Name = t.Name,
                Columns = columnsLookup[key].Select(c => new SchemaColumn
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    MaxLength = c.MaxLength,
                    Precision = c.Precision,
                    Scale = c.Scale,
                    IsNullable = c.IsNullable,
                    DefaultValue = c.DefaultValue,
                    IsIdentity = c.IsIdentity,
                    Collation = c.Collation
                }).ToList(),
                Indexes = indexesLookup[key].Select(i =>
                {
                    var icKey = (t.Schema, t.Name, i.IndexId);
                    return new SchemaIndex
                    {
                        Name = i.IndexName,
                        IsClustered = i.TypeDesc == "CLUSTERED",
                        IsUnique = i.IsUnique,
                        FilterDefinition = i.FilterDefinition,
                        Columns = indexColumnsLookup[icKey]
                            .Where(ic => !ic.IsIncluded).Select(ic => ic.ColumnName).ToList(),
                        IncludeColumns = indexColumnsLookup[icKey]
                            .Where(ic => ic.IsIncluded).Select(ic => ic.ColumnName).ToList()
                    };
                }).ToList(),
                Constraints = BuildConstraints(constraintsLookup[key])
            };
        }).ToList();
    }

    private static IList<SchemaConstraint> BuildConstraints(IEnumerable<ConstraintRow> rows)
    {
        return rows.Select(c => c.ConstraintType switch
        {
            "PK" => new SchemaConstraint
            {
                Name = c.ConstraintName,
                ConstraintType = ConstraintType.PrimaryKey,
                Columns = c.Columns?.Split(',').ToList() ?? []
            },
            "FK" => new SchemaConstraint
            {
                Name = c.ConstraintName,
                ConstraintType = ConstraintType.ForeignKey,
                Columns = c.Columns?.Split(',').ToList() ?? [],
                ReferencedTable = $"[{c.ReferencedSchema}].[{c.ReferencedTable}]",
                ReferencedColumns = c.ReferencedColumns?.Split(',').ToList() ?? [],
                OnDeleteAction = c.OnDelete,
                OnUpdateAction = c.OnUpdate
            },
            "UQ" => new SchemaConstraint
            {
                Name = c.ConstraintName,
                ConstraintType = ConstraintType.Unique,
                Columns = c.Columns?.Split(',').ToList() ?? []
            },
            "CHK" => new SchemaConstraint
            {
                Name = c.ConstraintName,
                ConstraintType = ConstraintType.Check,
                Definition = c.Definition
            },
            "DEF" => new SchemaConstraint
            {
                Name = c.ConstraintName,
                ConstraintType = ConstraintType.Default,
                Columns = c.ColumnName != null ? [c.ColumnName] : [],
                Definition = c.Definition
            },
            _ => new SchemaConstraint { Name = c.ConstraintName }
        }).ToList();
    }

    #endregion

    #region 程式物件收集

    private static async Task<IList<SchemaProgramObject>> CollectProgramObjectsAsync(
        SqlConnection connection,
        ProgramObjectType objectType,
        CancellationToken ct)
    {
        var typeFilter = objectType switch
        {
            ProgramObjectType.View => "type = 'V'",
            ProgramObjectType.StoredProcedure => "type = 'P'",
            ProgramObjectType.Function => "type IN ('FN', 'IF', 'TF', 'AF')",
            ProgramObjectType.Trigger => "type = 'TR'",
            _ => throw new ArgumentException($"不支援的程式物件類型：{objectType}")
        };

        var sql = $@"
SELECT s.name AS Schema, o.name AS Name, OBJECT_DEFINITION(o.object_id) AS Definition
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
