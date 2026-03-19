using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 欄位統計文件 ViewModel
/// </summary>
public partial class ColumnUsageDocumentViewModel : DocumentViewModel
{
    private readonly IColumnUsageService? _columnUsageService;
    private readonly ColumnUsageExcelExporter? _excelExporter;
    private static int _instanceCount;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _minUsageCount = 2;

    [ObservableProperty]
    private bool _showOnlyInconsistent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _totalColumnCount;

    [ObservableProperty]
    private int _inconsistentCount;

    [ObservableProperty]
    private ColumnUsageStatistics? _selectedStatistics;

    [ObservableProperty]
    private bool _showOnlyDifferent;

    /// <summary>
    /// 統計結果集合
    /// </summary>
    public ObservableCollection<ColumnUsageStatistics> Statistics { get; } = [];

    /// <summary>
    /// 篩選後的使用明細
    /// </summary>
    public ObservableCollection<ColumnUsageDetail> FilteredUsages { get; } = [];

    /// <summary>
    /// 最小出現次數選項
    /// </summary>
    public int[] MinUsageCountOptions { get; } = [1, 2, 3, 5, 10];

    public override string DocumentType => "ColumnUsage";

    public override string DocumentKey => $"{DocumentType}:{_instanceId}";

    private readonly int _instanceId;

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public ColumnUsageDocumentViewModel()
    {
        _instanceId = ++_instanceCount;
        Title = $"欄位統計 {_instanceId}";
        Icon = "📊";
    }

    /// <summary>
    /// 執行時建構函式
    /// </summary>
    public ColumnUsageDocumentViewModel(
        IColumnUsageService columnUsageService,
        ColumnUsageExcelExporter excelExporter)
    {
        _columnUsageService = columnUsageService;
        _excelExporter = excelExporter;
        _instanceId = ++_instanceCount;
        Title = $"欄位統計 {_instanceId}";
        Icon = "📊";
        CanClose = true;
    }

    partial void OnSearchTextChanged(string value)
    {
        // 搜尋文字變更時自動重新篩選
        _ = ApplyFilterAsync();
    }

    partial void OnMinUsageCountChanged(int value)
    {
        // 最小次數變更時自動重新篩選
        _ = ApplyFilterAsync();
    }

    partial void OnShowOnlyInconsistentChanged(bool value)
    {
        // 篩選條件變更時自動重新篩選
        _ = ApplyFilterAsync();
    }

    partial void OnSelectedStatisticsChanged(ColumnUsageStatistics? value)
    {
        // 選擇變更時更新明細篩選
        UpdateFilteredUsages();
    }

    partial void OnShowOnlyDifferentChanged(bool value)
    {
        // 只顯示差異變更時更新明細篩選
        UpdateFilteredUsages();
    }

    /// <summary>
    /// 更新篩選後的使用明細
    /// </summary>
    private void UpdateFilteredUsages()
    {
        FilteredUsages.Clear();

        if (SelectedStatistics == null)
            return;

        var usages = ShowOnlyDifferent
            ? SelectedStatistics.Usages.Where(u => u.HasDifference)
            : SelectedStatistics.Usages;

        foreach (var usage in usages)
        {
            FilteredUsages.Add(usage);
        }
    }

    /// <summary>
    /// 載入統計資料
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_columnUsageService == null)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在載入欄位統計...";
            Statistics.Clear();

            var statistics = await _columnUsageService.GetFilteredStatisticsAsync(
                searchText: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                showOnlyInconsistent: ShowOnlyInconsistent,
                minUsageCount: MinUsageCount);

            foreach (var stat in statistics)
            {
                Statistics.Add(stat);
            }

            TotalColumnCount = Statistics.Count;
            InconsistentCount = Statistics.Count(s => !s.IsFullyConsistent);

            StatusMessage = $"已載入 {TotalColumnCount} 個欄位統計（不一致 {InconsistentCount} 個）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入錯誤：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 套用篩選條件
    /// </summary>
    private async Task ApplyFilterAsync()
    {
        if (_columnUsageService == null || IsLoading)
            return;

        await LoadAsync();
    }

    /// <summary>
    /// 重新整理
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// 匯出到 Excel
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_excelExporter == null || Statistics.Count == 0)
        {
            StatusMessage = "沒有資料可匯出";
            return;
        }

        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null)
        {
            StatusMessage = "無法取得儲存服務";
            return;
        }

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "匯出欄位統計",
                SuggestedFileName = $"欄位統計_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                FileTypeChoices =
                [
                    new FilePickerFileType("Excel 檔案") { Patterns = ["*.xlsx"] }
                ]
            });

            if (file == null)
                return;

            StatusMessage = "正在匯出...";

            await _excelExporter.ExportAsync(file.Path.LocalPath, Statistics.ToList());

            StatusMessage = $"已匯出至 {file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯出錯誤：{ex.Message}";
        }
    }

    /// <summary>
    /// 清除篩選條件
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = string.Empty;
        MinUsageCount = 2;
        ShowOnlyInconsistent = false;
    }
}
