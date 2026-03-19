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
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 健康監控文件 ViewModel（MDI Document）
/// </summary>
public partial class HealthMonitoringDocumentViewModel : DocumentViewModel
{
    private readonly IHealthMonitoringService? _healthMonitoringService;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "HealthMonitoring";
    public override string DocumentKey => DocumentType; // 只允許開啟一個實例

    #region 安裝狀態

    [ObservableProperty]
    private HealthMonitoringInstallStatus? _installStatus;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _showSetupPanel = true;

    [ObservableProperty]
    private bool _showDashboard;

    #endregion

    #region 移除選項

    [ObservableProperty]
    private bool _keepHistoryData;

    [ObservableProperty]
    private bool _removeJobsOnly;

    #endregion

    #region 處理狀態

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteHealthCheckCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    [ObservableProperty]
    private int _selectedTabIndex;

    #endregion

    #region 資料集合

    public ObservableCollection<HealthStatusSummary> StatusSummaries { get; } = [];
    public ObservableCollection<HealthMetric> CurrentMetrics { get; } = [];
    public ObservableCollection<HealthLogEntry> RecentAlerts { get; } = [];
    public ObservableCollection<MonitoringCategory> Categories { get; } = [];

    #endregion

    #region 趨勢圖表

    [ObservableProperty]
    private string _selectedTrendCheckType = "Memory";

    [ObservableProperty]
    private string _selectedTrendMetricName = "Memory Usage %";

    public ObservableCollection<string> TrendCheckTypes { get; } = ["Memory", "CPU", "Disk", "Connections", "TempDB"];
    public ObservableCollection<string> TrendMetricNames { get; } = ["Memory Usage %"];

    [ObservableProperty]
    private bool _hasTrendData;

    [ObservableProperty]
    private string _trendNoDataMessage = "尚無趨勢資料，請先執行健康檢查";

    public ObservableCollection<ISeries> TrendSeries { get; } = [];

    public ICartesianAxis[] TrendXAxes { get; } =
    [
        new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("MM/dd HH:mm"))
        {
            Name = "時間",
            LabelsRotation = 45
        }
    ];

    public Axis[] TrendYAxes { get; } =
    [
        new Axis
        {
            Name = "值"
        }
    ];

    #endregion

    /// <summary>
    /// 匯出 SQL 腳本回呼（由 View 層設定，參數為 SQL 內容，回傳儲存路徑或 null）
    /// </summary>
    public Func<string, Task<string?>>? SaveFileCallback { get; set; }

    #region 告警篩選

    [ObservableProperty]
    private int _alertDays = 7;

    [ObservableProperty]
    private string? _alertCheckTypeFilter;

    // 狀態篩選（多選）
    [ObservableProperty]
    private bool _filterStatusOk;

    [ObservableProperty]
    private bool _filterStatusWarning = true; // 預設勾選

    [ObservableProperty]
    private bool _filterStatusCritical = true; // 預設勾選

    [ObservableProperty]
    private bool _hasAlerts;

    [ObservableProperty]
    private string _alertsEmptyMessage = "無告警記錄";

    // 保存從資料庫取得的原始告警資料
    private IReadOnlyList<HealthLogEntry> _allAlerts = [];

    public ObservableCollection<int> AlertDaysOptions { get; } = [1, 3, 7, 14, 30];

    public ObservableCollection<string> AlertCheckTypeOptions { get; } = ["全部", "Memory", "CPU", "Disk", "Connections", "TempDB", "Blocking", "Deadlock", "Backup", "Jobs", "LongQuery", "Performance", "Transaction"];

    #endregion

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public HealthMonitoringDocumentViewModel()
    {
        Title = "健康監控";
        Icon = "🩺";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public HealthMonitoringDocumentViewModel(
        IHealthMonitoringService healthMonitoringService,
        IConnectionManager connectionManager)
    {
        _healthMonitoringService = healthMonitoringService;
        _connectionManager = connectionManager;

        Title = "健康監控";
        Icon = "🩺";
        CanClose = true;

        // 初始化
        _ = LoadInstallStatusAsync();
    }

    #region 初始化與載入

    private async Task LoadInstallStatusAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            StatusMessage = "檢查安裝狀態...";
            InstallStatus = await _healthMonitoringService.GetInstallStatusAsync();
            IsInstalled = InstallStatus.IsFullyInstalled;

            if (IsInstalled)
            {
                ShowSetupPanel = false;
                ShowDashboard = true;
                await LoadDashboardDataAsync();
            }
            else
            {
                ShowSetupPanel = true;
                ShowDashboard = false;
            }

            StatusMessage = IsInstalled ? "已安裝健康監控系統" : "尚未安裝健康監控系統";
        }
        catch (Exception ex)
        {
            StatusMessage = $"檢查安裝狀態失敗: {ex.Message}";
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            // 載入狀態摘要
            var summaries = await _healthMonitoringService.GetStatusSummaryAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusSummaries.Clear();
                foreach (var summary in summaries)
                {
                    StatusSummaries.Add(summary);
                }
            });

            // 載入目前指標
            var metrics = await _healthMonitoringService.GetCurrentMetricsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentMetrics.Clear();
                foreach (var metric in metrics)
                {
                    CurrentMetrics.Add(metric);
                }
            });

            // 載入最近告警（使用篩選邏輯）
            _allAlerts = await _healthMonitoringService.GetRecentAlertsAsync(AlertDays);
            ApplyAlertsFilter();

            // 載入監控類別
            var categories = await _healthMonitoringService.GetCategoriesAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Categories.Clear();
                foreach (var category in categories)
                {
                    Categories.Add(category);
                }
            });

            // 載入趨勢資料
            await LoadTrendDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入資料失敗: {ex.Message}";
        }
    }

    private async Task LoadTrendDataAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            var trendData = await _healthMonitoringService.GetTrendDataAsync(
                SelectedTrendCheckType,
                SelectedTrendMetricName,
                30);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TrendSeries.Clear();
                HasTrendData = trendData.Count > 0;

                if (trendData.Count > 0)
                {
                    var values = trendData
                        .Where(d => d.MetricValue.HasValue)
                        .Select(d => new DateTimePoint(d.CheckTime, (double)d.MetricValue!.Value))
                        .ToList();

                    var thresholdValue = trendData.FirstOrDefault(d => d.ThresholdValue.HasValue)?.ThresholdValue;

                    TrendSeries.Add(new LineSeries<DateTimePoint>
                    {
                        Name = SelectedTrendMetricName,
                        Values = values,
                        Fill = null,
                        Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                        GeometrySize = 6,
                        GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 2)
                    });

                    if (thresholdValue.HasValue)
                    {
                        var minTime = trendData.Min(d => d.CheckTime);
                        var maxTime = trendData.Max(d => d.CheckTime);

                        TrendSeries.Add(new LineSeries<DateTimePoint>
                        {
                            Name = "閾值",
                            Values =
                            [
                                new DateTimePoint(minTime, (double)thresholdValue.Value),
                                new DateTimePoint(maxTime, (double)thresholdValue.Value)
                            ],
                            Fill = null,
                            Stroke = new SolidColorPaint(SKColors.OrangeRed, 2) { PathEffect = new DashEffect([5, 5]) },
                            GeometrySize = 0
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasTrendData = false;
                TrendNoDataMessage = $"載入趨勢資料失敗: {ex.Message}";
            });
        }
    }

    partial void OnSelectedTrendCheckTypeChanged(string value)
    {
        // 更新可用的指標名稱
        TrendMetricNames.Clear();

        switch (value)
        {
            case "Memory":
                TrendMetricNames.Add("Memory Usage %");
                TrendMetricNames.Add("Page Life Expectancy");
                break;
            case "CPU":
                TrendMetricNames.Add("SQL Server CPU %");
                TrendMetricNames.Add("System Idle %");
                break;
            case "Disk":
                TrendMetricNames.Add("Free Space MB - C:");
                TrendMetricNames.Add("Free Space MB - D:");
                break;
            case "Connections":
                TrendMetricNames.Add("Total Connections");
                TrendMetricNames.Add("Active Connections");
                break;
            case "TempDB":
                TrendMetricNames.Add("TempDB Usage %");
                break;
        }

        if (TrendMetricNames.Count > 0)
        {
            SelectedTrendMetricName = TrendMetricNames[0];
        }
    }

    partial void OnSelectedTrendMetricNameChanged(string value)
    {
        _ = LoadTrendDataAsync();
    }

    partial void OnAlertDaysChanged(int value)
    {
        // 天數變更需要重新從資料庫載入
        _ = LoadAlertsWithFilterAsync();
    }

    #endregion

    #region 面板切換

    [RelayCommand]
    private void SwitchToSetup()
    {
        ShowSetupPanel = true;
        ShowDashboard = false;
    }

    [RelayCommand]
    private void SwitchToDashboard()
    {
        if (IsInstalled)
        {
            ShowSetupPanel = false;
            ShowDashboard = true;
        }
    }

    [RelayCommand]
    private async Task NavigateToAlertsAsync(string? checkType)
    {
        // 切換到看板
        ShowSetupPanel = false;
        ShowDashboard = true;

        // 設定篩選條件
        AlertCheckTypeFilter = checkType;

        // 切換到告警分頁（索引 2）
        SelectedTabIndex = 2;

        // 重新載入告警資料
        await LoadAlertsWithFilterAsync();
    }

    private async Task LoadAlertsWithFilterAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            // 從資料庫取得所有記錄
            _allAlerts = await _healthMonitoringService.GetRecentAlertsAsync(AlertDays);

            // 套用篩選
            ApplyAlertsFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入告警失敗: {ex.Message}";
        }
    }

    private void ApplyAlertsFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RecentAlerts.Clear();

            // 建立選取的狀態清單
            var selectedStatuses = new List<string>();
            if (FilterStatusOk) selectedStatuses.Add("OK");
            if (FilterStatusWarning) selectedStatuses.Add("WARNING");
            if (FilterStatusCritical) selectedStatuses.Add("CRITICAL");

            // 若沒選任何狀態，顯示空結果
            if (selectedStatuses.Count == 0)
            {
                HasAlerts = false;
                AlertsEmptyMessage = "請至少選擇一個狀態";
                return;
            }

            // 套用類型篩選
            var filteredByType = string.IsNullOrEmpty(AlertCheckTypeFilter) || AlertCheckTypeFilter == "全部"
                ? _allAlerts
                : _allAlerts.Where(a => a.CheckType == AlertCheckTypeFilter);

            // 套用狀態篩選
            var filteredAlerts = filteredByType
                .Where(a => !string.IsNullOrEmpty(a.Status) && selectedStatuses.Contains(a.Status))
                .ToList();

            foreach (var alert in filteredAlerts)
            {
                RecentAlerts.Add(alert);
            }

            HasAlerts = filteredAlerts.Count > 0;
            if (!HasAlerts)
            {
                var typeText = string.IsNullOrEmpty(AlertCheckTypeFilter) || AlertCheckTypeFilter == "全部"
                    ? "所有類型"
                    : AlertCheckTypeFilter;
                var statusText = string.Join("、", selectedStatuses);
                AlertsEmptyMessage = $"過去 {AlertDays} 天內 {typeText} 無 {statusText} 記錄";
            }
        });
    }

    partial void OnAlertCheckTypeFilterChanged(string? value)
    {
        ApplyAlertsFilter();
    }

    partial void OnFilterStatusOkChanged(bool value)
    {
        ApplyAlertsFilter();
    }

    partial void OnFilterStatusWarningChanged(bool value)
    {
        ApplyAlertsFilter();
    }

    partial void OnFilterStatusCriticalChanged(bool value)
    {
        ApplyAlertsFilter();
    }

    #endregion

    #region 安裝命令

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            IsProcessing = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ProgressPercentage = 0;
            ProgressMessage = "準備安裝...";
            StatusMessage = "安裝中...";

            var progress = new Progress<InstallProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = p.PercentComplete;
                    ProgressMessage = p.Message;
                });
            });

            var result = await _healthMonitoringService.InstallAsync(progress, _cancellationTokenSource.Token);

            if (result.Success)
            {
                StatusMessage = "安裝成功";
                await LoadInstallStatusAsync();
            }
            else
            {
                StatusMessage = $"安裝失敗: {result.ErrorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "安裝已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"安裝失敗: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanInstall() => !IsProcessing;

    #endregion

    #region 移除命令

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            IsProcessing = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ProgressPercentage = 0;
            ProgressMessage = "準備移除...";
            StatusMessage = "移除中...";

            var options = new UninstallOptions(KeepHistoryData, RemoveJobsOnly);

            var progress = new Progress<InstallProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = p.PercentComplete;
                    ProgressMessage = p.Message;
                });
            });

            var result = await _healthMonitoringService.UninstallAsync(options, progress, _cancellationTokenSource.Token);

            if (result.Success)
            {
                StatusMessage = "移除成功";
                await LoadInstallStatusAsync();
            }
            else
            {
                StatusMessage = $"移除失敗: {result.ErrorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "移除已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"移除失敗: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanUninstall() => !IsProcessing && (InstallStatus?.IsPartiallyInstalled == true || InstallStatus?.AgentJobsExist == true);

    #endregion

    #region 刷新命令

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "重新整理中...";

            await LoadInstallStatusAsync();

            if (IsInstalled)
            {
                await LoadDashboardDataAsync();
            }

            StatusMessage = "重新整理完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"重新整理失敗: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanRefresh() => !IsProcessing;

    #endregion

    #region 執行健康檢查命令

    [RelayCommand(CanExecute = nameof(CanExecuteHealthCheck))]
    private async Task ExecuteHealthCheckAsync()
    {
        if (_healthMonitoringService == null) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "執行健康檢查中...";

            await _healthMonitoringService.ExecuteHealthCheckAsync();

            StatusMessage = "健康檢查完成，正在更新資料...";
            await LoadDashboardDataAsync();

            StatusMessage = "健康檢查完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"健康檢查失敗: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanExecuteHealthCheck() => !IsProcessing && IsInstalled;

    #endregion

    #region 更新監控類別

    [RelayCommand]
    private async Task UpdateCategoryAsync(MonitoringCategory? category)
    {
        if (_healthMonitoringService == null || category == null) return;

        try
        {
            await _healthMonitoringService.UpdateCategoryAsync(
                category.CategoryId,
                category.IsEnabled,
                category.CheckIntervalMinutes);

            StatusMessage = $"已更新 {category.CategoryName} 設定";
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失敗: {ex.Message}";
        }
    }

    #endregion

    #region 匯出 SQL 腳本

    [RelayCommand]
    private async Task ExportSqlAsync()
    {
        if (_healthMonitoringService == null || SaveFileCallback == null) return;

        try
        {
            StatusMessage = "正在產生 SQL 腳本...";
            var sql = await _healthMonitoringService.GenerateExportSqlAsync();
            var filePath = await SaveFileCallback(sql);
            if (filePath != null)
                StatusMessage = $"已匯出 SQL 腳本至 {filePath}";
            else
                StatusMessage = "已取消匯出";
        }
        catch (Exception ex)
        {
            StatusMessage = $"匯出失敗：{ex.Message}";
        }
    }

    #endregion

    #region 取消命令

    [RelayCommand]
    private void CancelOperation()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "正在取消...";
    }

    #endregion
}
