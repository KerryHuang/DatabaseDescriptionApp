using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 資料表統計文件 ViewModel
/// </summary>
public partial class TableStatisticsDocumentViewModel : DocumentViewModel
{
    private readonly ITableStatisticsService? _service;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "TableStatistics";
    public override string DocumentKey => DocumentType;

    #region 原始資料緩存

    private IReadOnlyList<TableStatisticsInfo> _allStatistics = [];

    #endregion

    #region 狀態屬性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadStatisticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadExactRowCountsCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "請點擊「載入統計」開始";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    #endregion

    #region 篩選屬性

    [ObservableProperty]
    private string? _schemaFilter;

    [ObservableProperty]
    private string? _objectTypeFilter;

    [ObservableProperty]
    private string? _tableNameFilter;

    [ObservableProperty]
    private long _minRowCount;

    [ObservableProperty]
    private int _minColumnCount;

    [ObservableProperty]
    private int _chartTopN = 10;

    #endregion

    #region 篩選選項

    public ObservableCollection<string> SchemaOptions { get; } = ["全部"];

    public ObservableCollection<string> ObjectTypeOptions { get; } =
        ["全部", "TABLE", "VIEW"];

    public long[] MinRowCountOptions { get; } = [0, 100, 1000, 10000, 100000, 1000000];

    public int[] MinColumnCountOptions { get; } = [0, 5, 10, 20, 50];

    public int[] ChartTopNOptions { get; } = [5, 10, 15, 20, 50];

    #endregion

    #region 資料集合

    public ObservableCollection<TableStatisticsInfo> Statistics { get; } = [];

    #endregion

    #region 圖表屬性

    /// <summary>長條圖：Top N 資料表依資料列數排行</summary>
    public ObservableCollection<ISeries> RowCountChartSeries { get; } = [];

    public Axis[] RowCountXAxes { get; } =
    [
        new Axis
        {
            Name = "資料表",
            LabelsRotation = 45,
            TextSize = 11
        }
    ];

    public Axis[] RowCountYAxes { get; } =
    [
        new Axis { Name = "資料列數" }
    ];

    /// <summary>圓餅圖：Schema 分佈</summary>
    public ObservableCollection<ISeries> SchemaDistributionSeries { get; } = [];

    #endregion

    #region 摘要屬性

    [ObservableProperty]
    private int _totalTableCount;

    [ObservableProperty]
    private int _totalViewCount;

    [ObservableProperty]
    private long _totalRowCount;

    [ObservableProperty]
    private decimal _totalSizeMB;

    [ObservableProperty]
    private int _filteredCount;

    #endregion

    #region 建構函式

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public TableStatisticsDocumentViewModel()
    {
        Title = "資料表統計";
        Icon = "📈";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public TableStatisticsDocumentViewModel(ITableStatisticsService service) : this()
    {
        _service = service;
    }

    #endregion

    #region 命令

    private bool CanRunCommand => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task LoadStatisticsAsync()
    {
        if (_service == null) return;

        IsLoading = true;
        StatusMessage = "正在載入資料表統計...";
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        try
        {
            var statistics = await _service.GetAllTableStatisticsAsync(ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allStatistics = statistics;

                // 動態填充 Schema 選項
                SchemaOptions.Clear();
                SchemaOptions.Add("全部");
                foreach (var schema in statistics.Select(s => s.SchemaName).Distinct().OrderBy(s => s))
                {
                    SchemaOptions.Add(schema);
                }

                // 重置篩選
                SchemaFilter = "全部";
                ObjectTypeFilter = "全部";
                TableNameFilter = string.Empty;
                MinRowCount = 0;
                MinColumnCount = 0;

                ApplyFilter();

                HasData = statistics.Count > 0;
                StatusMessage = $"已載入 {statistics.Count} 個物件的統計資訊";
            });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消載入";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task LoadExactRowCountsAsync()
    {
        if (_service == null || _allStatistics.Count == 0) return;

        IsLoading = true;
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        try
        {
            // 僅計算篩選後可見的 TABLE 類型物件
            var tablesToCount = Statistics
                .Where(s => s.ObjectType == "TABLE")
                .ToList();

            var total = tablesToCount.Count;
            var completed = 0;

            foreach (var table in tablesToCount)
            {
                ct.ThrowIfCancellationRequested();

                completed++;
                ProgressMessage = $"正在計算 [{table.SchemaName}].[{table.TableName}] ({completed}/{total})...";

                try
                {
                    var exactCount = await _service.GetExactRowCountAsync(
                        table.SchemaName, table.TableName, ct);
                    table.ExactRowCount = exactCount;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 單一表格計算失敗不影響其他表格
                    table.ExactRowCount = null;
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 重新套用篩選以更新顯示
                ApplyFilter();
                StatusMessage = $"已完成 {completed} 個資料表的精確計數";
                ProgressMessage = string.Empty;
            });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消精確計數";
            ProgressMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"精確計數失敗: {ex.Message}";
            ProgressMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "正在取消...";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SchemaFilter = "全部";
        ObjectTypeFilter = "全部";
        TableNameFilter = string.Empty;
        MinRowCount = 0;
        MinColumnCount = 0;
    }

    #endregion

    #region 篩選變更通知

    partial void OnSchemaFilterChanged(string? value) => ApplyFilter();
    partial void OnObjectTypeFilterChanged(string? value) => ApplyFilter();
    partial void OnTableNameFilterChanged(string? value) => ApplyFilter();
    partial void OnMinRowCountChanged(long value) => ApplyFilter();
    partial void OnMinColumnCountChanged(int value) => ApplyFilter();
    partial void OnChartTopNChanged(int value) => UpdateCharts(Statistics.ToList());

    #endregion

    #region 篩選邏輯

    private void ApplyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var filtered = _allStatistics.AsEnumerable();

            // Schema 篩選
            if (!string.IsNullOrEmpty(SchemaFilter) && SchemaFilter != "全部")
            {
                filtered = filtered.Where(s =>
                    s.SchemaName.Equals(SchemaFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 物件類型篩選
            if (!string.IsNullOrEmpty(ObjectTypeFilter) && ObjectTypeFilter != "全部")
            {
                filtered = filtered.Where(s =>
                    s.ObjectType.Equals(ObjectTypeFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 名稱搜尋
            if (!string.IsNullOrEmpty(TableNameFilter))
            {
                filtered = filtered.Where(s =>
                    s.TableName.Contains(TableNameFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 資料列數下限
            if (MinRowCount > 0)
            {
                filtered = filtered.Where(s => s.DisplayRowCount >= MinRowCount);
            }

            // 欄位數下限
            if (MinColumnCount > 0)
            {
                filtered = filtered.Where(s => s.ColumnCount >= MinColumnCount);
            }

            var result = filtered.ToList();

            Statistics.Clear();
            foreach (var item in result)
            {
                Statistics.Add(item);
            }

            // 更新摘要
            TotalTableCount = result.Count(s => s.ObjectType == "TABLE");
            TotalViewCount = result.Count(s => s.ObjectType == "VIEW");
            TotalRowCount = result.Sum(s => s.DisplayRowCount);
            TotalSizeMB = result.Sum(s => s.TotalSizeMB);
            FilteredCount = result.Count;

            // 更新圖表
            UpdateCharts(result);
        });
    }

    #endregion

    #region 圖表更新

    private void UpdateCharts(List<TableStatisticsInfo> data)
    {
        UpdateRowCountChart(data);
        UpdateSchemaDistributionChart(data);
    }

    private void UpdateRowCountChart(List<TableStatisticsInfo> data)
    {
        RowCountChartSeries.Clear();

        var topTables = data
            .OrderByDescending(s => s.DisplayRowCount)
            .Take(ChartTopN)
            .ToList();

        if (topTables.Count == 0) return;

        var labels = topTables.Select(s => $"{s.SchemaName}.{s.TableName}").ToArray();
        var values = topTables.Select(s => (double)s.DisplayRowCount).ToArray();

        RowCountXAxes[0].Labels = labels;

        RowCountChartSeries.Add(new ColumnSeries<double>
        {
            Name = "資料列數",
            Values = values,
            Fill = new SolidColorPaint(SKColors.DodgerBlue),
            MaxBarWidth = 40
        });
    }

    private void UpdateSchemaDistributionChart(List<TableStatisticsInfo> data)
    {
        SchemaDistributionSeries.Clear();

        var schemaGroups = data
            .GroupBy(s => s.SchemaName)
            .Select(g => new { Schema = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        if (schemaGroups.Count == 0) return;

        // 使用不同顏色
        var colors = new[]
        {
            SKColors.DodgerBlue, SKColors.OrangeRed, SKColors.MediumSeaGreen,
            SKColors.Gold, SKColors.MediumPurple, SKColors.Coral,
            SKColors.Teal, SKColors.HotPink, SKColors.SlateBlue,
            SKColors.DarkCyan
        };

        for (var i = 0; i < schemaGroups.Count; i++)
        {
            var group = schemaGroups[i];
            var color = colors[i % colors.Length];

            SchemaDistributionSeries.Add(new PieSeries<int>
            {
                Name = group.Schema,
                Values = [group.Count],
                Fill = new SolidColorPaint(color)
            });
        }
    }

    #endregion
}
