using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// 資料表查詢服務實作
/// </summary>
public class TableQueryService : ITableQueryService
{
    private readonly ITableRepository _tableRepository;
    private readonly IColumnRepository _columnRepository;
    private readonly IIndexRepository _indexRepository;
    private readonly IRelationRepository _relationRepository;
    private readonly IParameterRepository _parameterRepository;
    private readonly ISqlQueryRepository _sqlQueryRepository;

    public TableQueryService(
        ITableRepository tableRepository,
        IColumnRepository columnRepository,
        IIndexRepository indexRepository,
        IRelationRepository relationRepository,
        IParameterRepository parameterRepository,
        ISqlQueryRepository sqlQueryRepository)
    {
        _tableRepository = tableRepository;
        _columnRepository = columnRepository;
        _indexRepository = indexRepository;
        _relationRepository = relationRepository;
        _parameterRepository = parameterRepository;
        _sqlQueryRepository = sqlQueryRepository;
    }

    public Task<IReadOnlyList<TableInfo>> GetAllTablesAsync(CancellationToken ct = default)
        => _tableRepository.GetAllAsync(ct);

    public Task<IReadOnlyList<TableInfo>> GetTablesByTypeAsync(string type, CancellationToken ct = default)
        => _tableRepository.GetByTypeAsync(type, ct);

    public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        string type,
        string schema,
        string tableName,
        CancellationToken ct = default)
        => _columnRepository.GetColumnsAsync(type, schema, tableName, ct);

    public Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
        => _indexRepository.GetIndexesAsync(schema, tableName, ct);

    public Task DropIndexAsync(
        string schema,
        string tableName,
        string indexName,
        CancellationToken ct = default)
        => _indexRepository.DropIndexAsync(schema, tableName, indexName, ct);

    public Task<IReadOnlyList<RelationInfo>> GetRelationsAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
        => _relationRepository.GetRelationsAsync(schema, tableName, ct);

    public Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(
        string schema,
        string objectName,
        CancellationToken ct = default)
        => _parameterRepository.GetParametersAsync(schema, objectName, ct);

    public Task<string?> GetDefinitionAsync(
        string schema,
        string objectName,
        CancellationToken ct = default)
        => _parameterRepository.GetDefinitionAsync(schema, objectName, ct);

    public async Task<string?> GetCreateTableSqlAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        // 跳脫單引號以中和字串字面值注入（三處內插皆位於單引號字串內）。
        var s = schema.Replace("'", "''");
        var t = tableName.Replace("'", "''");

        // 以 FOR XML PATH + STUFF 串接各欄位定義（相容 SQL Server 2005+，
        // 避免 `SELECT @v = @v + ... ORDER BY` 變數累加反模式只取到單一欄位的問題）。
        var sql = $@"
            DECLARE @tableName NVARCHAR(256) = '[{s}].[{t}]';
            DECLARE @cols NVARCHAR(MAX) = STUFF((
                SELECT ',' + CHAR(13) + CHAR(10) +
                    '    [' + c.COLUMN_NAME + '] ' +
                    c.DATA_TYPE +
                    CASE
                        WHEN c.DATA_TYPE IN ('varchar','nvarchar','char','nchar')
                            THEN '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) END + ')'
                        WHEN c.DATA_TYPE IN ('decimal','numeric')
                            THEN '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR) + ')'
                        ELSE ''
                    END +
                    CASE WHEN c.IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE ' NULL' END +
                    CASE WHEN c.COLUMN_DEFAULT IS NOT NULL THEN ' DEFAULT ' + c.COLUMN_DEFAULT ELSE '' END
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = '{s}' AND c.TABLE_NAME = '{t}'
                ORDER BY c.ORDINAL_POSITION
                FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 3, '');

            SELECT CASE WHEN @cols IS NULL THEN NULL
                ELSE 'CREATE TABLE ' + @tableName + ' (' + CHAR(13) + CHAR(10) + @cols + CHAR(13) + CHAR(10) + ');'
            END AS CreateTableScript;
        ";

        var dataTable = await _sqlQueryRepository.ExecuteQueryAsync(sql, ct);
        if (dataTable.Rows.Count == 0)
            return null;

        var value = dataTable.Rows[0][0];
        return value == DBNull.Value ? null : value?.ToString();
    }

    public Task UpdateColumnDescriptionAsync(
        string schema,
        string objectName,
        string columnName,
        string? description,
        string objectType = "TABLE",
        CancellationToken ct = default)
        => _columnRepository.UpdateColumnDescriptionAsync(schema, objectName, columnName, description, objectType, ct);

    public Task UpdateTableDescriptionAsync(
        string type,
        string schema,
        string objectName,
        string? description,
        CancellationToken ct = default)
        => _tableRepository.UpdateTableDescriptionAsync(type, schema, objectName, description, ct);
}
