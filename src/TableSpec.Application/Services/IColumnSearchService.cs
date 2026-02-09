using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 欄位搜尋服務介面（支援多資料庫搜尋）
/// </summary>
public interface IColumnSearchService
{
    /// <summary>
    /// 對多個資料庫連線同時搜尋欄位
    /// </summary>
    /// <param name="columnName">欄位名稱關鍵字</param>
    /// <param name="profileIds">要搜尋的連線設定 ID 清單</param>
    /// <param name="progress">進度回報</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>所有資料庫的搜尋結果（已填入 DatabaseName，按資料庫名稱排序）</returns>
    Task<List<ColumnSearchResult>> SearchColumnsMultiAsync(
        string columnName,
        IReadOnlyList<Guid> profileIds,
        bool exactMatch = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
