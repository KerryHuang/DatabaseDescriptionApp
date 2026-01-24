using TableSpec.Domain.Entities.SchemaCompare;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// Schema 收集器介面
/// </summary>
public interface ISchemaCollector
{
    /// <summary>
    /// 收集資料庫 Schema
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="connectionName">連線名稱</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>資料庫 Schema 快照</returns>
    Task<DatabaseSchema> CollectAsync(string connectionString, string connectionName, CancellationToken ct = default);
}
