using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 使用狀態分析文件 ViewModel
/// </summary>
public partial class UsageAnalysisDocumentViewModel : DocumentViewModel
{
    private readonly IUsageAnalysisService? _service;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cts;
    private UsageScanResult? _scanResult;

    public override string DocumentType => "UsageAnalysis";
    public override string DocumentKey => DocumentType;

    // === 模式切換 ===
    [ObservableProperty]
    private bool _isCompareMode;

    // === 操作參數 ===
    [ObservableProperty]
    private int _yearsThreshold = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "請點擊「開始掃描」分析使用狀態";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    // === 維度切換 ===
    [ObservableProperty]
    private bool _isColumnDimension;

    // === 篩選 ===
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _statusFilter; // 0=全部, 1=僅未使用, 2=僅使用中

    // === 摘要 ===
    [ObservableProperty]
    private int _totalTableCount;

    [ObservableProperty]
    private int _unusedTableCount;

    [ObservableProperty]
    private int _totalColumnCount;

    [ObservableProperty]
    private int _unusedColumnCount;

    // === 單環境結果 ===
    public ObservableCollection<TableUsageInfo> TableResults { get; } = [];
    public ObservableCollection<ColumnUsageStatus> ColumnResults { get; } = [];

    // === 多環境結果 ===
    public ObservableCollection<TableUsageComparisonRow> ComparisonTableRows { get; } = [];
    public ObservableCollection<ColumnUsageComparisonRow> ComparisonColumnRows { get; } = [];

    // === 多環境選擇 ===
    [ObservableProperty]
    private ConnectionProfile? _selectedBaseProfile;

    public ObservableCollection<ConnectionProfile> AvailableProfiles { get; } = [];
    public ObservableCollection<SelectableProfile> TargetProfileItems { get; } = [];

    // === 確認回調 ===
    public Func<string, Task<bool>>? ConfirmExecuteCallback { get; set; }

    // === 建構函式 ===
    public UsageAnalysisDocumentViewModel()
    {
        Title = "使用狀態分析";
    }

    public UsageAnalysisDocumentViewModel(
        IUsageAnalysisService service,
        IConnectionManager connectionManager) : this()
    {
        _service = service;
        _connectionManager = connectionManager;
        LoadProfiles();
    }

    // === 命令 ===
    private bool CanScan => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (_service == null) return;

        IsScanning = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => ProgressMessage = msg));

            if (IsCompareMode)
            {
                await RunCompareAsync(progress, _cts.Token);
            }
            else
            {
                await RunSingleScanAsync(progress, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消掃描";
        }
        catch (Exception ex)
        {
            StatusMessage = $"掃描失敗: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ProgressMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteTableAsync(TableUsageInfo? table)
    {
        if (_service == null || table == null) return;

        var sql = _service.GenerateDropTableSql(table.SchemaName, table.TableName);
        var message = $"確定要刪除以下資料表嗎？此操作無法復原！\n\n{sql}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed) return;
        }

        try
        {
            StatusMessage = $"正在刪除資料表：{table.FullTableName}...";
            await _service.DeleteTableAsync(table.SchemaName, table.TableName, CancellationToken.None);
            StatusMessage = $"資料表刪除成功：{table.FullTableName}";

            await Dispatcher.UIThread.InvokeAsync(() => TableResults.Remove(table));
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteColumnAsync(ColumnUsageStatus? column)
    {
        if (_service == null || column == null) return;

        var sql = _service.GenerateDropColumnSql(column.SchemaName, column.TableName, column.ColumnName);
        var message = $"確定要刪除以下欄位嗎？此操作無法復原！\n\n{sql}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed) return;
        }

        try
        {
            StatusMessage = $"正在刪除欄位：{column.DropColumnStatement}...";
            await _service.DeleteColumnAsync(
                column.SchemaName, column.TableName, column.ColumnName, CancellationToken.None);
            StatusMessage = $"欄位刪除成功：[{column.TableName}].[{column.ColumnName}]";

            await Dispatcher.UIThread.InvokeAsync(() => ColumnResults.Remove(column));
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除失敗: {ex.Message}";
        }
    }

    // === 篩選通知 ===
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(int value) => ApplyFilter();
    partial void OnIsColumnDimensionChanged(bool value) => ApplyFilter();

    // === 私有方法 ===

    private async Task RunSingleScanAsync(IProgress<string> progress, CancellationToken ct)
    {
        StatusMessage = "正在掃描...";
        _scanResult = await _service!.ScanAsync(YearsThreshold, progress, ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TotalTableCount = _scanResult.Tables.Count;
            UnusedTableCount = _scanResult.UnusedTableCount(YearsThreshold);
            TotalColumnCount = _scanResult.Columns.Count;
            UnusedColumnCount = _scanResult.UnusedColumnCount;
            HasData = _scanResult.Tables.Count > 0 || _scanResult.Columns.Count > 0;
            ApplyFilter();
            StatusMessage = $"掃描完成，共 {TotalTableCount} 張表、{TotalColumnCount} 個欄位";
        });
    }

    private async Task RunCompareAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (_connectionManager == null || SelectedBaseProfile == null) return;

        var targetIds = TargetProfileItems
            .Where(p => p.IsSelected)
            .Select(p => p.Profile.Id)
            .ToList();

        if (targetIds.Count == 0)
        {
            StatusMessage = "請至少選擇一個目標環境";
            return;
        }

        StatusMessage = "正在進行多環境比對...";
        var comparison = await _service!.CompareAsync(
            SelectedBaseProfile.Id, targetIds, YearsThreshold, progress, ct);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ComparisonTableRows.Clear();
            foreach (var row in comparison.TableRows)
                ComparisonTableRows.Add(row);

            ComparisonColumnRows.Clear();
            foreach (var row in comparison.ColumnRows)
                ComparisonColumnRows.Add(row);

            HasData = comparison.TableRows.Count > 0 || comparison.ColumnRows.Count > 0;
            StatusMessage = $"比對完成，共 {comparison.TableRows.Count} 張表、{comparison.ColumnRows.Count} 個欄位";
        });
    }

    private void ApplyFilter()
    {
        if (_scanResult == null && !IsCompareMode) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCompareMode && _scanResult != null)
            {
                ApplySingleModeFilter();
            }
        });
    }

    private void ApplySingleModeFilter()
    {
        if (_scanResult == null) return;

        if (!IsColumnDimension)
        {
            var filtered = _scanResult.Tables.AsEnumerable();

            if (!string.IsNullOrEmpty(SearchText))
                filtered = filtered.Where(t =>
                    t.TableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    t.SchemaName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (StatusFilter == 1)
                filtered = filtered.Where(t => !t.HasQueryActivity && !t.HasRecentUpdate(YearsThreshold));
            else if (StatusFilter == 2)
                filtered = filtered.Where(t => t.HasQueryActivity || t.HasRecentUpdate(YearsThreshold));

            TableResults.Clear();
            foreach (var item in filtered)
                TableResults.Add(item);
        }
        else
        {
            var filtered = _scanResult.Columns.AsEnumerable();

            if (!string.IsNullOrEmpty(SearchText))
                filtered = filtered.Where(c =>
                    c.ColumnName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    c.TableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (StatusFilter == 1)
                filtered = filtered.Where(c => !c.IsUsed);
            else if (StatusFilter == 2)
                filtered = filtered.Where(c => c.IsUsed);

            ColumnResults.Clear();
            foreach (var item in filtered)
                ColumnResults.Add(item);
        }
    }

    private void LoadProfiles()
    {
        if (_connectionManager == null) return;
        var profiles = _connectionManager.GetAllProfiles();
        AvailableProfiles.Clear();
        foreach (var p in profiles)
            AvailableProfiles.Add(p);
    }

    partial void OnSelectedBaseProfileChanged(ConnectionProfile? value)
    {
        TargetProfileItems.Clear();
        if (value == null || _connectionManager == null) return;

        foreach (var p in _connectionManager.GetAllProfiles().Where(p => p.Id != value.Id))
            TargetProfileItems.Add(new SelectableProfile { Profile = p });
    }
}

/// <summary>
/// 可勾選的連線設定（用於多環境比對目標選擇）
/// </summary>
public partial class SelectableProfile : ObservableObject
{
    public required ConnectionProfile Profile { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
