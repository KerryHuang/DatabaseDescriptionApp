using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 效能診斷文件 ViewModel
/// </summary>
public partial class PerformanceDiagnosticsDocumentViewModel : DocumentViewModel
{
    private readonly IPerformanceDiagnosticsService? _service;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "PerformanceDiagnostics";
    public override string DocumentKey => DocumentType;

    #region 原始資料緩存

    private IReadOnlyList<WaitStatistic> _allWaitStatistics = [];
    private IReadOnlyList<ExpensiveQuery> _allExpensiveQueries = [];
    private IReadOnlyList<ExpensiveQuery> _allExpensiveProcedures = [];
    private IReadOnlyList<ExpensiveQuery> _allCpuIntensiveQueries = [];
    private IReadOnlyList<IndexStatus> _allIndexStatuses = [];
    private IReadOnlyList<MissingIndex> _allMissingIndexes = [];
    private IReadOnlyList<StatisticsInfo> _allStatistics = [];
    private IReadOnlyList<ErrorLogEntry> _allErrorLogs = [];

    #endregion

    #region 狀態屬性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunWaitStatisticsAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunExpensiveQueriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunExpensiveProceduresCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCpuIntensiveQueriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunIndexAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunMissingIndexAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunStatisticsAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunErrorLogAnalysisCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "請選擇分頁並點擊「執行查詢」開始分析";

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _hasWaitStatistics;

    [ObservableProperty]
    private bool _hasExpensiveQueries;

    [ObservableProperty]
    private bool _hasExpensiveProcedures;

    [ObservableProperty]
    private bool _hasCpuIntensiveQueries;

    [ObservableProperty]
    private bool _hasIndexStatuses;

    [ObservableProperty]
    private bool _hasMissingIndexes;

    [ObservableProperty]
    private bool _hasStatistics;

    [ObservableProperty]
    private bool _hasErrorLogs;

    #endregion

    #region 資料集合

    public ObservableCollection<WaitStatistic> WaitStatistics { get; } = [];
    public ObservableCollection<ExpensiveQuery> ExpensiveQueries { get; } = [];
    public ObservableCollection<ExpensiveQuery> ExpensiveProcedures { get; } = [];
    public ObservableCollection<ExpensiveQuery> CpuIntensiveQueries { get; } = [];
    public ObservableCollection<IndexStatus> IndexStatuses { get; } = [];
    public ObservableCollection<MissingIndex> MissingIndexes { get; } = [];
    public ObservableCollection<StatisticsInfo> Statistics { get; } = [];
    public ObservableCollection<ErrorLogEntry> ErrorLogs { get; } = [];

    #endregion

    #region 選取項目

    [ObservableProperty]
    private ExpensiveQuery? _selectedExpensiveQuery;

    [ObservableProperty]
    private ExpensiveQuery? _selectedExpensiveProcedure;

    [ObservableProperty]
    private ExpensiveQuery? _selectedCpuIntensiveQuery;

    [ObservableProperty]
    private MissingIndex? _selectedMissingIndex;

    [ObservableProperty]
    private ErrorLogEntry? _selectedErrorLog;

    #endregion

    #region 等候事件篩選屬性

    [ObservableProperty]
    private string? _waitTypeFilter;

    [ObservableProperty]
    private decimal? _minWaitPercentage;

    [ObservableProperty]
    private bool _showHighSignalWaitOnly;

    [ObservableProperty]
    private int _waitStatisticsTopN = 20;

    /// <summary>等候事件顯示筆數選項</summary>
    public ObservableCollection<int> WaitStatisticsTopNOptions { get; } = [10, 20, 50, 100];

    /// <summary>最小百分比選項</summary>
    public ObservableCollection<decimal> MinPercentageOptions { get; } = [0, 0.1m, 0.5m, 1, 5];

    #endregion

    #region 耗時查詢篩選屬性

    [ObservableProperty]
    private string? _expensiveQueryDatabaseFilter;

    [ObservableProperty]
    private string? _expensiveQueryTableFilter;

    [ObservableProperty]
    private int _topN = 10;

    /// <summary>顯示筆數選項</summary>
    public ObservableCollection<int> TopNOptions { get; } = [5, 10, 20, 50, 100];

    /// <summary>資料庫選項（動態填充）</summary>
    public ObservableCollection<string> DatabaseOptions { get; } = ["全部"];

    #endregion

    #region 索引分析篩選屬性

    [ObservableProperty]
    private string? _indexDatabaseFilter;

    [ObservableProperty]
    private string? _indexTableFilter;

    [ObservableProperty]
    private string? _indexTypeFilter;

    [ObservableProperty]
    private decimal _minFragmentationPercent;

    [ObservableProperty]
    private bool _showUnusedOnly;

    [ObservableProperty]
    private bool _showFragmentedOnly;

    /// <summary>索引類型選項</summary>
    public ObservableCollection<string> IndexTypeOptions { get; } =
        ["全部", "CLUSTERED", "NONCLUSTERED", "HEAP"];

    /// <summary>碎片化百分比選項</summary>
    public ObservableCollection<decimal> FragmentationOptions { get; } =
        [0, 5, 10, 30, 50];

    #endregion

    #region 缺少索引篩選屬性

    [ObservableProperty]
    private string? _missingIndexTableFilter;

    [ObservableProperty]
    private decimal? _minImprovementMeasure;

    /// <summary>改善指標選項</summary>
    public ObservableCollection<decimal> ImprovementMeasureOptions { get; } =
        [0, 10000, 100000, 1000000];

    #endregion

    #region 統計資訊篩選屬性

    [ObservableProperty]
    private string? _statisticsTableFilter;

    [ObservableProperty]
    private string? _statisticsTypeFilter;

    [ObservableProperty]
    private long _minModificationCounter;

    [ObservableProperty]
    private bool _showOutdatedOnly;

    [ObservableProperty]
    private int _outdatedDays = 7;

    /// <summary>統計類型選項</summary>
    public ObservableCollection<string> StatisticsTypeOptions { get; } =
        ["全部", "Index Statistic", "Auto Created", "User Created"];

    /// <summary>過時天數選項</summary>
    public ObservableCollection<int> OutdatedDaysOptions { get; } = [1, 3, 7, 14, 30];

    #endregion

    #region 錯誤記錄篩選屬性

    [ObservableProperty]
    private int _errorLogQueryDays = 7;

    /// <summary>查詢天數選項</summary>
    public ObservableCollection<int> ErrorLogDaysOptions { get; } = [1, 3, 7, 14, 30];

    [ObservableProperty]
    private DateTime? _errorLogStartDate;

    [ObservableProperty]
    private DateTime? _errorLogEndDate;

    [ObservableProperty]
    private string? _errorLogKeyword;

    [ObservableProperty]
    private bool _showErrorsOnly;

    #endregion

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public PerformanceDiagnosticsDocumentViewModel()
    {
        Title = "效能診斷";
        Icon = "⚡";
    }

    /// <summary>
    /// 執行時建構函式
    /// </summary>
    public PerformanceDiagnosticsDocumentViewModel(IPerformanceDiagnosticsService service) : this()
    {
        _service = service;
    }

    /// <summary>
    /// 初始化（不自動載入資料）
    /// </summary>
    public Task InitializeAsync()
    {
        // 不自動載入，等待使用者手動觸發
        return Task.CompletedTask;
    }

    #region 命令

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunWaitStatisticsAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢等候事件中...";

            // 傳入 top 和 minPercentage 參數
            var minPct = MinWaitPercentage ?? 0;
            var results = await _service.GetWaitStatisticsAsync(WaitStatisticsTopN, minPct, _cancellationTokenSource.Token);

            // 儲存到緩存並套用篩選
            _allWaitStatistics = results;
            ApplyWaitStatisticsFilter();

            StatusMessage = $"等候事件查詢完成，共 {results.Count} 筆";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "查詢已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunExpensiveQueriesAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢耗時查詢中...";

            var results = await _service.GetTopExpensiveQueriesAsync(100, _cancellationTokenSource.Token);

            _allExpensiveQueries = results;
            UpdateDatabaseOptionsFromAll();
            ApplyExpensiveQueriesOnlyFilter();

            StatusMessage = $"耗時查詢完成，共 {results.Count} 筆";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "查詢已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunExpensiveProceduresAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢耗時預存程序中...";

            var results = await _service.GetTopExpensiveProceduresAsync(100, _cancellationTokenSource.Token);

            _allExpensiveProcedures = results;
            UpdateDatabaseOptionsFromAll();
            ApplyExpensiveProceduresOnlyFilter();

            StatusMessage = $"耗時預存程序查詢完成，共 {results.Count} 筆";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "查詢已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunCpuIntensiveQueriesAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢耗 CPU 查詢中...";

            var results = await _service.GetTopCpuIntensiveQueriesAsync(100, _cancellationTokenSource.Token);

            _allCpuIntensiveQueries = results;
            UpdateDatabaseOptionsFromAll();
            ApplyCpuIntensiveQueriesOnlyFilter();

            StatusMessage = $"耗 CPU 查詢完成，共 {results.Count} 筆";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "查詢已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunIndexAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "分析索引狀態中...";

            var progress = new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() => ProgressMessage = msg);
            });

            var results = await _service.GetIndexStatusAsync(progress, _cancellationTokenSource.Token);

            // 儲存到緩存並套用篩選
            _allIndexStatuses = results;
            ApplyIndexStatusesFilter();

            StatusMessage = $"索引分析完成，共 {results.Count} 個索引";
            ProgressMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "索引分析已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"索引分析失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            ProgressMessage = string.Empty;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunMissingIndexAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "分析缺少索引中...";

            var results = await _service.GetMissingIndexesAsync(_cancellationTokenSource.Token);

            // 儲存到緩存並套用篩選
            _allMissingIndexes = results;
            ApplyMissingIndexesFilter();

            StatusMessage = $"缺少索引分析完成，共 {results.Count} 個建議";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "缺少索引分析已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"缺少索引分析失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunStatisticsAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢統計資訊中...";

            var results = await _service.GetStatisticsInfoAsync(_cancellationTokenSource.Token);

            // 儲存到緩存並套用篩選
            _allStatistics = results;
            ApplyStatisticsFilter();

            StatusMessage = $"統計資訊查詢完成，共 {results.Count} 筆";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "查詢已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunErrorLogAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = $"讀取最近 {ErrorLogQueryDays} 天的錯誤記錄中...";

            var results = await _service.GetErrorLogAsync(ErrorLogQueryDays, _cancellationTokenSource.Token);

            // 儲存到緩存並套用篩選
            _allErrorLogs = results;
            ApplyErrorLogsFilter();

            StatusMessage = $"錯誤記錄讀取完成，共 {results.Count} 筆（最近 {ErrorLogQueryDays} 天）";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "讀取已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"讀取失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "正在取消...";
    }

    [RelayCommand]
    private async Task CopyCreateIndexStatementAsync()
    {
        if (SelectedMissingIndex == null) return;

        try
        {
            await CopyToClipboardAsync(SelectedMissingIndex.CreateIndexStatement);
            StatusMessage = "已複製建立索引語法到剪貼簿";
        }
        catch (Exception ex)
        {
            StatusMessage = $"複製失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyQueryPlanAsync()
    {
        if (SelectedCpuIntensiveQuery?.QueryPlan == null) return;

        try
        {
            await CopyToClipboardAsync(SelectedCpuIntensiveQuery.QueryPlan);
            StatusMessage = "已複製執行計畫到剪貼簿";
        }
        catch (Exception ex)
        {
            StatusMessage = $"複製失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyErrorLogAsync()
    {
        if (SelectedErrorLog == null) return;

        try
        {
            var text = $"[{SelectedErrorLog.LogDate:yyyy-MM-dd HH:mm:ss}] [{SelectedErrorLog.ProcessInfo}] {SelectedErrorLog.Text}";
            await CopyToClipboardAsync(text);
            StatusMessage = "已複製錯誤訊息到剪貼簿";
        }
        catch (Exception ex)
        {
            StatusMessage = $"複製失敗: {ex.Message}";
        }
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private bool CanRunAnalysis() => !IsLoading;

    #endregion

    #region 篩選方法

    // ========== 等候事件篩選 ==========
    partial void OnWaitTypeFilterChanged(string? value) => ApplyWaitStatisticsFilter();
    partial void OnMinWaitPercentageChanged(decimal? value) => ApplyWaitStatisticsFilter();
    partial void OnShowHighSignalWaitOnlyChanged(bool value) => ApplyWaitStatisticsFilter();

    private void ApplyWaitStatisticsFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            WaitStatistics.Clear();
            var filtered = _allWaitStatistics.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(WaitTypeFilter))
                filtered = filtered.Where(w =>
                    w.WaitType.Contains(WaitTypeFilter, StringComparison.OrdinalIgnoreCase));

            if (MinWaitPercentage.HasValue)
                filtered = filtered.Where(w => w.Percentage >= MinWaitPercentage.Value);

            if (ShowHighSignalWaitOnly)
                filtered = filtered.Where(w => w.SignalWaitSeconds > w.ResourceWaitSeconds);

            foreach (var item in filtered)
                WaitStatistics.Add(item);

            HasWaitStatistics = WaitStatistics.Count > 0;
            UpdateFilteredStatusMessage("等候事件", _allWaitStatistics.Count, WaitStatistics.Count);
        });
    }

    [RelayCommand]
    private void ClearWaitStatisticsFilter()
    {
        WaitTypeFilter = null;
        MinWaitPercentage = null;
        ShowHighSignalWaitOnly = false;
        WaitStatisticsTopN = 20;
    }

    // ========== 耗時查詢篩選 ==========
    partial void OnExpensiveQueryDatabaseFilterChanged(string? value) => ApplyExpensiveQueriesFilter();
    partial void OnExpensiveQueryTableFilterChanged(string? value) => ApplyExpensiveQueriesFilter();
    partial void OnTopNChanged(int value) => ApplyExpensiveQueriesFilter();

    /// <summary>套用所有三個區塊的篩選</summary>
    private void ApplyExpensiveQueriesFilter()
    {
        ApplyExpensiveQueriesOnlyFilter();
        ApplyExpensiveProceduresOnlyFilter();
        ApplyCpuIntensiveQueriesOnlyFilter();
    }

    /// <summary>僅套用耗時查詢篩選</summary>
    private void ApplyExpensiveQueriesOnlyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ExpensiveQueries.Clear();
            var filtered = _allExpensiveQueries.AsEnumerable();
            if (!string.IsNullOrEmpty(ExpensiveQueryDatabaseFilter) && ExpensiveQueryDatabaseFilter != "全部")
                filtered = filtered.Where(q => q.DatabaseName == ExpensiveQueryDatabaseFilter);
            if (!string.IsNullOrEmpty(ExpensiveQueryTableFilter))
                filtered = filtered.Where(q => q.QueryText.Contains(ExpensiveQueryTableFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var item in filtered.Take(TopN))
                ExpensiveQueries.Add(item);
            HasExpensiveQueries = ExpensiveQueries.Count > 0;
        });
    }

    /// <summary>僅套用耗時預存程序篩選</summary>
    private void ApplyExpensiveProceduresOnlyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ExpensiveProcedures.Clear();
            var filtered = _allExpensiveProcedures.AsEnumerable();
            if (!string.IsNullOrEmpty(ExpensiveQueryDatabaseFilter) && ExpensiveQueryDatabaseFilter != "全部")
                filtered = filtered.Where(p => p.DatabaseName == ExpensiveQueryDatabaseFilter);
            if (!string.IsNullOrEmpty(ExpensiveQueryTableFilter))
                filtered = filtered.Where(p => p.QueryText.Contains(ExpensiveQueryTableFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var item in filtered.Take(TopN))
                ExpensiveProcedures.Add(item);
            HasExpensiveProcedures = ExpensiveProcedures.Count > 0;
        });
    }

    /// <summary>僅套用耗 CPU 查詢篩選</summary>
    private void ApplyCpuIntensiveQueriesOnlyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            CpuIntensiveQueries.Clear();
            var filtered = _allCpuIntensiveQueries.AsEnumerable();
            if (!string.IsNullOrEmpty(ExpensiveQueryDatabaseFilter) && ExpensiveQueryDatabaseFilter != "全部")
                filtered = filtered.Where(c => c.DatabaseName == ExpensiveQueryDatabaseFilter);
            if (!string.IsNullOrEmpty(ExpensiveQueryTableFilter))
                filtered = filtered.Where(c => c.QueryText.Contains(ExpensiveQueryTableFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var item in filtered.Take(TopN))
                CpuIntensiveQueries.Add(item);
            HasCpuIntensiveQueries = CpuIntensiveQueries.Count > 0;
        });
    }

    /// <summary>從所有緩存更新資料庫選項</summary>
    private void UpdateDatabaseOptionsFromAll()
    {
        var allDatabases = _allExpensiveQueries.Select(q => q.DatabaseName)
            .Concat(_allExpensiveProcedures.Select(p => p.DatabaseName))
            .Concat(_allCpuIntensiveQueries.Select(c => c.DatabaseName));
        UpdateDatabaseOptions(allDatabases);
    }

    [RelayCommand]
    private void ClearExpensiveQueriesFilter()
    {
        ExpensiveQueryDatabaseFilter = null;
        ExpensiveQueryTableFilter = null;
        TopN = 10;
    }

    /// <summary>顯示選取的 SQL 語法</summary>
    [RelayCommand]
    private async Task ShowSelectedSqlAsync()
    {
        var query = SelectedExpensiveQuery ?? SelectedExpensiveProcedure ?? SelectedCpuIntensiveQuery;
        if (query == null) return;

        var dialog = new Window
        {
            Title = "SQL 語法",
            Width = 800,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new SelectableTextBlock
                {
                    Text = query.QueryText,
                    FontFamily = new Avalonia.Media.FontFamily("Consolas, Microsoft JhengHei"),
                    Margin = new Avalonia.Thickness(10),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };

        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            await dialog.ShowDialog(desktop.MainWindow!);
        }
    }

    // ========== 索引分析篩選 ==========
    partial void OnIndexDatabaseFilterChanged(string? value) => ApplyIndexStatusesFilter();
    partial void OnIndexTableFilterChanged(string? value) => ApplyIndexStatusesFilter();
    partial void OnIndexTypeFilterChanged(string? value) => ApplyIndexStatusesFilter();
    partial void OnMinFragmentationPercentChanged(decimal value) => ApplyIndexStatusesFilter();
    partial void OnShowUnusedOnlyChanged(bool value) => ApplyIndexStatusesFilter();
    partial void OnShowFragmentedOnlyChanged(bool value) => ApplyIndexStatusesFilter();

    private void ApplyIndexStatusesFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IndexStatuses.Clear();
            var filtered = _allIndexStatuses.AsEnumerable();

            if (!string.IsNullOrEmpty(IndexDatabaseFilter) && IndexDatabaseFilter != "全部")
                filtered = filtered.Where(i => i.DatabaseName == IndexDatabaseFilter);

            if (!string.IsNullOrWhiteSpace(IndexTableFilter))
                filtered = filtered.Where(i =>
                    i.TableName.Contains(IndexTableFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(IndexTypeFilter) && IndexTypeFilter != "全部")
                filtered = filtered.Where(i => i.IndexType == IndexTypeFilter);

            if (MinFragmentationPercent > 0)
                filtered = filtered.Where(i => i.FragmentationPercent >= MinFragmentationPercent);

            if (ShowUnusedOnly)
                filtered = filtered.Where(i =>
                    i.UserSeeks == 0 && i.UserScans == 0 && i.UserLookups == 0 && i.UserUpdates > 0);

            if (ShowFragmentedOnly)
                filtered = filtered.Where(i => i.FragmentationPercent > 30);

            foreach (var item in filtered)
                IndexStatuses.Add(item);

            HasIndexStatuses = IndexStatuses.Count > 0;
            UpdateFilteredStatusMessage("索引", _allIndexStatuses.Count, IndexStatuses.Count);
        });
    }

    [RelayCommand]
    private void ClearIndexStatusesFilter()
    {
        IndexDatabaseFilter = null;
        IndexTableFilter = null;
        IndexTypeFilter = null;
        MinFragmentationPercent = 0;
        ShowUnusedOnly = false;
        ShowFragmentedOnly = false;
    }

    // ========== 缺少索引篩選 ==========
    partial void OnMissingIndexTableFilterChanged(string? value) => ApplyMissingIndexesFilter();
    partial void OnMinImprovementMeasureChanged(decimal? value) => ApplyMissingIndexesFilter();

    private void ApplyMissingIndexesFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            MissingIndexes.Clear();
            var filtered = _allMissingIndexes.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(MissingIndexTableFilter))
                filtered = filtered.Where(m =>
                    m.TableName.Contains(MissingIndexTableFilter, StringComparison.OrdinalIgnoreCase));

            if (MinImprovementMeasure.HasValue && MinImprovementMeasure.Value > 0)
                filtered = filtered.Where(m => m.ImprovementMeasure >= MinImprovementMeasure.Value);

            foreach (var item in filtered)
                MissingIndexes.Add(item);

            HasMissingIndexes = MissingIndexes.Count > 0;
            UpdateFilteredStatusMessage("缺少索引建議", _allMissingIndexes.Count, MissingIndexes.Count);
        });
    }

    [RelayCommand]
    private void ClearMissingIndexesFilter()
    {
        MissingIndexTableFilter = null;
        MinImprovementMeasure = null;
    }

    // ========== 統計資訊篩選 ==========
    partial void OnStatisticsTableFilterChanged(string? value) => ApplyStatisticsFilter();
    partial void OnStatisticsTypeFilterChanged(string? value) => ApplyStatisticsFilter();
    partial void OnMinModificationCounterChanged(long value) => ApplyStatisticsFilter();
    partial void OnShowOutdatedOnlyChanged(bool value) => ApplyStatisticsFilter();
    partial void OnOutdatedDaysChanged(int value) => ApplyStatisticsFilter();

    private void ApplyStatisticsFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Statistics.Clear();
            var filtered = _allStatistics.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(StatisticsTableFilter))
                filtered = filtered.Where(s =>
                    s.TableName.Contains(StatisticsTableFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(StatisticsTypeFilter) && StatisticsTypeFilter != "全部")
                filtered = filtered.Where(s => s.StatisticType == StatisticsTypeFilter);

            if (MinModificationCounter > 0)
                filtered = filtered.Where(s => s.ModificationCounter >= MinModificationCounter);

            if (ShowOutdatedOnly)
            {
                var cutoffDate = DateTime.Now.AddDays(-OutdatedDays);
                filtered = filtered.Where(s =>
                    s.LastUpdated.HasValue && s.LastUpdated.Value < cutoffDate);
            }

            foreach (var item in filtered)
                Statistics.Add(item);

            HasStatistics = Statistics.Count > 0;
            UpdateFilteredStatusMessage("統計資訊", _allStatistics.Count, Statistics.Count);
        });
    }

    [RelayCommand]
    private void ClearStatisticsFilter()
    {
        StatisticsTableFilter = null;
        StatisticsTypeFilter = null;
        MinModificationCounter = 0;
        ShowOutdatedOnly = false;
        OutdatedDays = 7;
    }

    // ========== 錯誤記錄篩選 ==========
    partial void OnErrorLogStartDateChanged(DateTime? value) => ApplyErrorLogsFilter();
    partial void OnErrorLogEndDateChanged(DateTime? value) => ApplyErrorLogsFilter();
    partial void OnErrorLogKeywordChanged(string? value) => ApplyErrorLogsFilter();
    partial void OnShowErrorsOnlyChanged(bool value) => ApplyErrorLogsFilter();

    private void ApplyErrorLogsFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ErrorLogs.Clear();
            var filtered = _allErrorLogs.AsEnumerable();

            if (ErrorLogStartDate.HasValue)
                filtered = filtered.Where(e => e.LogDate >= ErrorLogStartDate.Value);

            if (ErrorLogEndDate.HasValue)
                filtered = filtered.Where(e => e.LogDate <= ErrorLogEndDate.Value.AddDays(1));

            if (!string.IsNullOrWhiteSpace(ErrorLogKeyword))
                filtered = filtered.Where(e =>
                    e.Text != null && e.Text.Contains(ErrorLogKeyword, StringComparison.OrdinalIgnoreCase));

            if (ShowErrorsOnly)
                filtered = filtered.Where(e =>
                    e.Text != null && (
                    e.Text.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    e.Text.Contains("錯誤", StringComparison.OrdinalIgnoreCase) ||
                    e.Text.Contains("fail", StringComparison.OrdinalIgnoreCase)));

            foreach (var item in filtered)
                ErrorLogs.Add(item);

            HasErrorLogs = ErrorLogs.Count > 0;
            UpdateFilteredStatusMessage("錯誤記錄", _allErrorLogs.Count, ErrorLogs.Count);
        });
    }

    [RelayCommand]
    private void ClearErrorLogsFilter()
    {
        ErrorLogStartDate = null;
        ErrorLogEndDate = null;
        ErrorLogKeyword = null;
        ShowErrorsOnly = false;
    }

    // ========== 輔助方法 ==========
    private void UpdateFilteredStatusMessage(string dataType, int totalCount, int filteredCount)
    {
        if (totalCount == filteredCount)
            StatusMessage = $"{dataType}查詢完成，共 {totalCount} 筆";
        else
            StatusMessage = $"{dataType}篩選完成，顯示 {filteredCount} / {totalCount} 筆";
    }

    private void UpdateDatabaseOptions(IEnumerable<string?> databaseNames)
    {
        var distinct = databaseNames
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        Dispatcher.UIThread.Post(() =>
        {
            DatabaseOptions.Clear();
            DatabaseOptions.Add("全部");
            foreach (var db in distinct)
                if (db != null) DatabaseOptions.Add(db);
        });
    }

    #endregion
}
