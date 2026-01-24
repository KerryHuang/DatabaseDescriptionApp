using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 參數查詢 Repository（用於 SP 和 Function）
/// </summary>
public interface IParameterRepository
{
    /// <summary>
    /// 取得指定物件的所有參數
    /// </summary>
    Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(
        string schema,
        string objectName,
        CancellationToken ct = default);

    /// <summary>
    /// 取得 Stored Procedure 或 Function 的程式碼定義
    /// </summary>
    Task<string?> GetDefinitionAsync(
        string schema,
        string objectName,
        CancellationToken ct = default);
}
