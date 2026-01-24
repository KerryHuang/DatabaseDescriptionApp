using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 一致性等級
/// </summary>
public enum ConsistencyLevel
{
    /// <summary>完全一致（綠色）</summary>
    Consistent = 0,
    /// <summary>警告：少數不一致（黃色）</summary>
    Warning = 1,
    /// <summary>嚴重：多種型態或高比例不一致（紅色）</summary>
    Severe = 2
}

/// <summary>
/// 欄位型態群組 ViewModel（用於顯示一致性）
/// </summary>
public partial class ColumnTypeGroupViewModel : ViewModelBase
{
    private static readonly StringComparer TypeComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// 欄位名稱
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// 此欄位名稱在不同資料表中的所有型態資訊
    /// </summary>
    public ObservableCollection<ColumnTypeInfo> Columns { get; } = [];

    /// <summary>
    /// 一致性等級
    /// </summary>
    public ConsistencyLevel Level => CalculateLevel();

    /// <summary>
    /// 一致性等級文字
    /// </summary>
    public string LevelText => Level switch
    {
        ConsistencyLevel.Consistent => "一致",
        ConsistencyLevel.Warning => "警告",
        ConsistencyLevel.Severe => "嚴重",
        _ => "未知"
    };

    /// <summary>
    /// 主要型態（出現最多的型態）
    /// </summary>
    public string PrimaryType => GetPrimaryType();

    /// <summary>
    /// 資料表總數
    /// </summary>
    public int TableCount => Columns.Count;

    /// <summary>
    /// 不一致的欄位數量
    /// </summary>
    public int InconsistentCount => Columns.Count(c => !TypeComparer.Equals(c.DataType, PrimaryType));

    /// <summary>
    /// 不同型態的數量
    /// </summary>
    public int DistinctTypeCount => Columns.Select(c => c.DataType).Distinct(TypeComparer).Count();

    /// <summary>
    /// 型態摘要（如 "varchar(50) x 5, varchar(100) x 2"）
    /// </summary>
    public string TypeSummary => GetTypeSummary();

    /// <summary>
    /// 計算一致性等級
    /// </summary>
    private ConsistencyLevel CalculateLevel()
    {
        if (Columns.Count == 0)
            return ConsistencyLevel.Consistent;

        var distinctTypes = Columns.Select(c => c.DataType).Distinct(TypeComparer).Count();

        // 完全一致
        if (distinctTypes == 1)
            return ConsistencyLevel.Consistent;

        // 計算不一致的比例
        var primaryType = PrimaryType;
        var primaryTypeCount = Columns.Count(c => TypeComparer.Equals(c.DataType, primaryType));
        var inconsistentCount = Columns.Count - primaryTypeCount;
        var inconsistentRatio = (double)inconsistentCount / Columns.Count;

        // 嚴重：3種以上型態，或不一致比例超過 30%
        if (distinctTypes >= 3 || inconsistentRatio > 0.3)
            return ConsistencyLevel.Severe;

        // 警告：2種型態且不一致比例低
        return ConsistencyLevel.Warning;
    }

    /// <summary>
    /// 取得主要型態（出現次數最多的）
    /// </summary>
    private string GetPrimaryType()
    {
        if (Columns.Count == 0)
            return string.Empty;

        return Columns
            .GroupBy(c => c.DataType, TypeComparer)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    /// <summary>
    /// 取得型態摘要
    /// </summary>
    private string GetTypeSummary()
    {
        if (Columns.Count == 0)
            return string.Empty;

        var groups = Columns
            .GroupBy(c => c.DataType, TypeComparer)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} × {g.Count()}");

        return string.Join(", ", groups);
    }

    /// <summary>
    /// 通知所有計算屬性更新，並設定每個欄位的一致性狀態
    /// </summary>
    public void RefreshCalculatedProperties()
    {
        // 設定每個欄位的一致性狀態
        var primaryType = PrimaryType;
        foreach (var column in Columns)
        {
            column.IsConsistent = TypeComparer.Equals(column.DataType, primaryType);
        }

        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(PrimaryType));
        OnPropertyChanged(nameof(TableCount));
        OnPropertyChanged(nameof(InconsistentCount));
        OnPropertyChanged(nameof(DistinctTypeCount));
        OnPropertyChanged(nameof(TypeSummary));
    }
}
