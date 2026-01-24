using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

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

    public TableQueryService(
        ITableRepository tableRepository,
        IColumnRepository columnRepository,
        IIndexRepository indexRepository,
        IRelationRepository relationRepository,
        IParameterRepository parameterRepository)
    {
        _tableRepository = tableRepository;
        _columnRepository = columnRepository;
        _indexRepository = indexRepository;
        _relationRepository = relationRepository;
        _parameterRepository = parameterRepository;
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
}
