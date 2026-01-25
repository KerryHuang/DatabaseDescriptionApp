using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
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

    #region 狀態屬性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunWaitStatisticsAnalysisCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunExpensiveQueryAnalysisCommand))]
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
    private ExpensiveQuery? _selectedCpuIntensiveQuery;

    [ObservableProperty]
    private MissingIndex? _selectedMissingIndex;

    [ObservableProperty]
    private ErrorLogEntry? _selectedErrorLog;

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

            var results = await _service.GetWaitStatisticsAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WaitStatistics.Clear();
                foreach (var item in results)
                    WaitStatistics.Add(item);
                HasWaitStatistics = WaitStatistics.Count > 0;
            });

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
    private async Task RunExpensiveQueryAnalysisAsync()
    {
        if (_service == null) return;

        try
        {
            IsLoading = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "查詢耗時語法中...";
            var ct = _cancellationTokenSource.Token;

            // 並行查詢三種耗時資料
            var queriesTask = _service.GetTopExpensiveQueriesAsync(5, ct);
            var proceduresTask = _service.GetTopExpensiveProceduresAsync(5, ct);
            var cpuTask = _service.GetTopCpuIntensiveQueriesAsync(5, ct);

            await Task.WhenAll(queriesTask, proceduresTask, cpuTask);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ExpensiveQueries.Clear();
                foreach (var item in queriesTask.Result)
                    ExpensiveQueries.Add(item);
                HasExpensiveQueries = ExpensiveQueries.Count > 0;

                ExpensiveProcedures.Clear();
                foreach (var item in proceduresTask.Result)
                    ExpensiveProcedures.Add(item);
                HasExpensiveProcedures = ExpensiveProcedures.Count > 0;

                CpuIntensiveQueries.Clear();
                foreach (var item in cpuTask.Result)
                    CpuIntensiveQueries.Add(item);
                HasCpuIntensiveQueries = CpuIntensiveQueries.Count > 0;
            });

            StatusMessage = $"耗時語法查詢完成 - 查詢: {ExpensiveQueries.Count}, 預存程序: {ExpensiveProcedures.Count}, CPU: {CpuIntensiveQueries.Count}";
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

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IndexStatuses.Clear();
                foreach (var item in results)
                    IndexStatuses.Add(item);
                HasIndexStatuses = IndexStatuses.Count > 0;
            });

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

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MissingIndexes.Clear();
                foreach (var item in results)
                    MissingIndexes.Add(item);
                HasMissingIndexes = MissingIndexes.Count > 0;
            });

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

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Statistics.Clear();
                foreach (var item in results)
                    Statistics.Add(item);
                HasStatistics = Statistics.Count > 0;
            });

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
            StatusMessage = "讀取錯誤記錄中...";

            var results = await _service.GetErrorLogAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ErrorLogs.Clear();
                foreach (var item in results)
                    ErrorLogs.Add(item);
                HasErrorLogs = ErrorLogs.Count > 0;
            });

            StatusMessage = $"錯誤記錄讀取完成，共 {results.Count} 筆";
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
}
