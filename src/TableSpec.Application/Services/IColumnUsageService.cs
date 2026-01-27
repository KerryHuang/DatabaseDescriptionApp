using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 欄位使用統計服務介面
/// </summary>
public interface IColumnUsageService
{
    /// <summary>
    /// 取得所有欄位的使用統計
    /// </summary>
    /// <param name="ct">取消權杖</param>
    /// <returns>欄位使用統計清單</returns>
    Task<IReadOnlyList<ColumnUsageStatistics>> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得篩選後的欄位使用統計
    /// </summary>
    /// <param name="searchText">搜尋文字（欄位名稱）</param>
    /// <param name="showOnlyInconsistent">只顯示不一致的欄位</param>
    /// <param name="minUsageCount">最小出現次數</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>篩選後的欄位使用統計清單</returns>
    Task<IReadOnlyList<ColumnUsageStatistics>> GetFilteredStatisticsAsync(
        string? searchText = null,
        bool showOnlyInconsistent = false,
        int minUsageCount = 1,
        CancellationToken ct = default);
}
