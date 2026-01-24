using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Application.Services;

/// <summary>
/// Schema 比對服務介面
/// </summary>
public interface ISchemaCompareService
{
    /// <summary>
    /// 比對兩個 Schema
    /// </summary>
    /// <param name="baseSchema">基準 Schema</param>
    /// <param name="targetSchema">目標 Schema</param>
    /// <returns>比對結果</returns>
    Task<SchemaComparison> CompareAsync(DatabaseSchema baseSchema, DatabaseSchema targetSchema);

    /// <summary>
    /// 比對基準與多個目標 Schema
    /// </summary>
    /// <param name="baseSchema">基準 Schema</param>
    /// <param name="targetSchemas">目標 Schema 清單</param>
    /// <returns>比對結果清單</returns>
    Task<IList<SchemaComparison>> CompareMultipleAsync(DatabaseSchema baseSchema, IList<DatabaseSchema> targetSchemas);
}
