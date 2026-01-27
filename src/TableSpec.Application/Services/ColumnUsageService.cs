using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// 欄位使用統計服務實作
/// </summary>
public class ColumnUsageService : IColumnUsageService
{
    private readonly IColumnUsageRepository _repository;

    public ColumnUsageService(IColumnUsageRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColumnUsageStatistics>> GetStatisticsAsync(CancellationToken ct = default)
    {
        var usages = await _repository.GetAllColumnUsagesAsync(ct);
        return BuildStatistics(usages);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColumnUsageStatistics>> GetFilteredStatisticsAsync(
        string? searchText = null,
        bool showOnlyInconsistent = false,
        int minUsageCount = 1,
        CancellationToken ct = default)
    {
        var statistics = await GetStatisticsAsync(ct);

        var filtered = statistics.AsEnumerable();

        // 搜尋欄位名稱
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(s =>
                s.ColumnName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        // 只顯示不一致
        if (showOnlyInconsistent)
        {
            filtered = filtered.Where(s => !s.IsFullyConsistent);
        }

        // 最小出現次數
        if (minUsageCount > 1)
        {
            filtered = filtered.Where(s => s.UsageCount >= minUsageCount);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 建立統計資料
    /// </summary>
    private static List<ColumnUsageStatistics> BuildStatistics(IReadOnlyList<ColumnUsageDetail> usages)
    {
        var result = new List<ColumnUsageStatistics>();

        // 按欄位名稱分組（不區分大小寫）
        var groups = usages.GroupBy(u => u.ColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var columnUsages = group.ToList();
            var usageCount = columnUsages.Count;

            // 計算主要型別（出現最多次的）
            var primaryType = columnUsages
                .GroupBy(u => u.BaseType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();

            var primaryBaseType = primaryType.Key;

            // 取得主要資料型別（完整型別）
            var primaryDataType = primaryType
                .GroupBy(u => u.DataType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            // 取得主要長度
            var primaryMaxLength = primaryType.First().MaxLength;

            // 取得主要可空性（出現最多次的）
            var primaryIsNullable = columnUsages
                .GroupBy(u => u.IsNullable)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;

            // 判斷一致性
            var isTypeConsistent = columnUsages
                .Select(u => u.BaseType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

            var isLengthConsistent = columnUsages
                .Where(u => u.BaseType.Equals(primaryBaseType, StringComparison.OrdinalIgnoreCase))
                .Select(u => u.MaxLength)
                .Distinct()
                .Count() <= 1;

            // 如果型別不一致，長度一致性無意義（設為 false）
            if (!isTypeConsistent)
            {
                isLengthConsistent = false;
            }

            var isNullabilityConsistent = columnUsages
                .Select(u => u.IsNullable)
                .Distinct()
                .Count() == 1;

            // 標記每個使用項目是否與主要型態有差異
            foreach (var usage in columnUsages)
            {
                usage.HasDifference =
                    !usage.BaseType.Equals(primaryBaseType, StringComparison.OrdinalIgnoreCase) ||
                    (usage.BaseType.Equals(primaryBaseType, StringComparison.OrdinalIgnoreCase) &&
                     usage.MaxLength != primaryMaxLength) ||
                    usage.IsNullable != primaryIsNullable;
            }

            result.Add(new ColumnUsageStatistics
            {
                ColumnName = group.Key,
                UsageCount = usageCount,
                IsTypeConsistent = isTypeConsistent,
                IsLengthConsistent = isLengthConsistent,
                IsNullabilityConsistent = isNullabilityConsistent,
                PrimaryDataType = primaryDataType,
                PrimaryBaseType = primaryBaseType,
                PrimaryMaxLength = primaryMaxLength,
                PrimaryIsNullable = primaryIsNullable,
                Usages = columnUsages
            });
        }

        // 按出現次數降序排列
        return result.OrderByDescending(s => s.UsageCount).ToList();
    }
}
