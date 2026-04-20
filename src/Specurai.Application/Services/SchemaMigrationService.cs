using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 分析協調服務實作
/// </summary>
public class SchemaMigrationService : ISchemaMigrationService
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _schemaCompareService;

    public SchemaMigrationService(
        ISchemaCollector schemaCollector,
        ISchemaCompareService schemaCompareService)
    {
        _schemaCollector = schemaCollector;
        _schemaCompareService = schemaCompareService;
    }

    public async Task<MigrationAnalysis> AnalyzeAsync(
        string baseConnectionString,
        string targetConnectionString,
        string baseEnvName,
        string targetEnvName,
        CancellationToken ct = default)
    {
        var baseTask = _schemaCollector.CollectAsync(baseConnectionString, baseEnvName, ct);
        var targetTask = _schemaCollector.CollectAsync(targetConnectionString, targetEnvName, ct);
        await Task.WhenAll(baseTask, targetTask);

        var baseSchema = await baseTask;
        var targetSchema = await targetTask;
        var comparison = await _schemaCompareService.CompareAsync(baseSchema, targetSchema);

        return new MigrationAnalysis
        {
            BaseSchema = baseSchema,
            TargetSchema = targetSchema,
            Comparison = comparison
        };
    }
}
