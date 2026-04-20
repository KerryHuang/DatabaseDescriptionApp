using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// T-SQL Migration 腳本產生器（純函數，無 I/O 相依）
/// </summary>
public interface ISqlScriptGenerator
{
    /// <summary>
    /// 根據選取的差異清單產生 T-SQL Migration 腳本
    /// </summary>
    /// <param name="selectedDifferences">使用者選取要執行的差異</param>
    /// <param name="baseSchema">基準 DatabaseSchema（用於查詢完整物件結構）</param>
    /// <param name="baseEnvName">基準環境名稱（用於腳本標頭）</param>
    /// <param name="targetEnvName">目標環境名稱（用於腳本標頭）</param>
    SyncScript Generate(
        IList<SchemaDifference> selectedDifferences,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName);
}
