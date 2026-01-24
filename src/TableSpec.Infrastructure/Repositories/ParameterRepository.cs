using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// 參數查詢 Repository 實作
/// </summary>
public class ParameterRepository : IParameterRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public ParameterRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(
        string schema,
        string objectName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<ParameterInfo>();

        const string sql = @"
SELECT
    p.PARAMETER_NAME AS Name,
    p.DATA_TYPE AS DataType,
    CASE
        WHEN ISNULL(p.CHARACTER_MAXIMUM_LENGTH, 0) > 0 THEN p.CHARACTER_MAXIMUM_LENGTH
        WHEN ISNULL(p.CHARACTER_MAXIMUM_LENGTH, 0) = -1 THEN -1
        ELSE NULL
    END AS Length,
    CASE WHEN p.PARAMETER_MODE = 'OUT' OR p.PARAMETER_MODE = 'INOUT' THEN 1 ELSE 0 END AS IsOutput,
    NULL AS DefaultValue,
    p.ORDINAL_POSITION AS Ordinal
FROM
    INFORMATION_SCHEMA.PARAMETERS p
WHERE
    p.SPECIFIC_SCHEMA = @Schema
    AND p.SPECIFIC_NAME = @ObjectName
ORDER BY
    p.ORDINAL_POSITION";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<ParameterInfo>(sql, new
        {
            Schema = schema,
            ObjectName = objectName
        });
        return result.ToList();
    }

    public async Task<string?> GetDefinitionAsync(
        string schema,
        string objectName,
        CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return null;

        const string sql = @"
SELECT
    OBJECT_DEFINITION(OBJECT_ID(@FullName)) AS Definition";

        await using var connection = new SqlConnection(connectionString);
        var fullName = $"{schema}.{objectName}";
        var result = await connection.QuerySingleOrDefaultAsync<string?>(sql, new { FullName = fullName });
        return result;
    }
}
