using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 欄位型態查詢與更新 Repository 介面
/// </summary>
public interface IColumnTypeRepository
{
    /// <summary>
    /// 取得指定欄位名稱在所有資料表中的型態資訊
    /// </summary>
    /// <param name="columnName">欄位名稱（精確比對）</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>欄位型態資訊清單</returns>
    Task<IReadOnlyList<ColumnTypeInfo>> GetColumnTypesAsync(
        string columnName, CancellationToken ct = default);

    /// <summary>
    /// 取得欄位的所有約束
    /// </summary>
    /// <param name="schema">Schema 名稱</param>
    /// <param name="table">資料表名稱</param>
    /// <param name="column">欄位名稱</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>約束資訊清單</returns>
    Task<IReadOnlyList<ConstraintInfo>> GetColumnConstraintsAsync(
        string schema, string table, string column, CancellationToken ct = default);

    /// <summary>
    /// 更新欄位長度（含約束處理）
    /// </summary>
    /// <param name="schema">Schema 名稱</param>
    /// <param name="table">資料表名稱</param>
    /// <param name="column">欄位名稱</param>
    /// <param name="newLength">新的長度</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>是否成功</returns>
    Task<bool> UpdateColumnLengthAsync(
        string schema, string table, string column,
        int newLength, CancellationToken ct = default);

    /// <summary>
    /// 取得欄位目前的最大資料長度
    /// </summary>
    /// <param name="schema">Schema 名稱</param>
    /// <param name="table">資料表名稱</param>
    /// <param name="column">欄位名稱</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>目前資料的最大長度</returns>
    Task<int> GetMaxDataLengthAsync(
        string schema, string table, string column, CancellationToken ct = default);
}
