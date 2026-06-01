using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// 未使用索引報表文件 ViewModel
/// </summary>
public partial class UnusedIndexReportDocumentViewModel : DocumentViewModel
{
    private readonly IPerformanceDiagnosticsService? _service;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "UnusedIndexReport";
    public override string DocumentKey => DocumentType;

    #region 原始資料緩存

    private IReadOnlyList<UnusedIndex> _allUnusedIndexes = [];

    /// <summary>伺服器上的所有使用者資料庫（開啟報表時載入，供篩選選項使用）</summary>
    private IReadOnlyList<string> _serverDatabases = [];

    #endregion

    #region 狀態屬性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadUnusedIndexesCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "請點擊「載入報表」開始分析";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    #endregion

    #region 篩選屬性

    [ObservableProperty]
    private string? _databaseFilter = "全部";

    [ObservableProperty]
    private string? _tableFilter;

    [ObservableProperty]
    private long? _minUpdates;

    #endregion

    #region 篩選選項

    /// <summary>資料庫篩選選項</summary>
    public ObservableCollection<string> DatabaseOptions { get; } = ["全部"];

    /// <summary>最小更新次數門檻選項</summary>
    public ObservableCollection<long> MinUpdatesOptions { get; } =
        [0, 100, 1000, 10000, 100000];

    #endregion

    #region 摘要屬性

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _highCount;

    [ObservableProperty]
    private decimal _totalWastedSizeMB;

    #endregion

    #region 資料集合

    /// <summary>篩選後的未使用索引集合</summary>
    public ObservableCollection<UnusedIndex> UnusedIndexes { get; } = [];

    [ObservableProperty]
    private UnusedIndex? _selectedUnusedIndex;

    #endregion

    #region 確認回調

    /// <summary>
    /// 確認執行的回調函數（由 View 設定）
    /// </summary>
    public Func<string, Task<bool>>? ConfirmExecuteCallback { get; set; }

    #endregion

    #region 建構函式

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public UnusedIndexReportDocumentViewModel()
    {
        Title = "未使用索引報表";
        Icon = "🗑️";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public UnusedIndexReportDocumentViewModel(IPerformanceDiagnosticsService service) : this()
    {
        _service = service;
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 開啟報表時載入伺服器上的所有使用者資料庫，使資料庫篩選選項在尚未載入報表前即可使用。
    /// 必須由 UI 執行緒呼叫（透過捕捉的同步內容回到 UI 執行緒修改集合）。
    /// </summary>
    public async Task LoadDatabaseOptionsAsync()
    {
        if (_service == null) return;

        try
        {
            var databases = await _service.GetUserDatabasesAsync();
            _serverDatabases = databases;

            // 與已載入報表的結果合併，避免覆蓋掉已載入的選項（開啟後立即載入報表的競態）
            var previousSelection = DatabaseFilter;
            SetDatabaseOptions(_serverDatabases.Concat(_allUnusedIndexes.Select(m => m.DatabaseName)));

            // 保留使用者目前的選取；若已不存在則回到「全部」
            DatabaseFilter = previousSelection is not null && DatabaseOptions.Contains(previousSelection)
                ? previousSelection
                : "全部";
        }
        catch
        {
            // 預載失敗時靜默略過；使用者仍可手動「載入報表」，屆時選項會由結果補上
        }
    }

    /// <summary>
    /// 以指定的資料庫名稱集合重建篩選選項（保留開頭的「全部」）。
    /// </summary>
    private void SetDatabaseOptions(IEnumerable<string?> databases)
    {
        DatabaseOptions.Clear();
        DatabaseOptions.Add("全部");
        foreach (var db in databases
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            DatabaseOptions.Add(db!);
        }
    }

    #endregion

    #region 命令

    private bool CanRunCommand => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task LoadUnusedIndexesAsync()
    {
        if (_service == null) return;

        IsLoading = true;
        StatusMessage = "正在分析未使用索引...";
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var results = await _service.GetUnusedIndexesAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allUnusedIndexes = results;

                // 合併伺服器資料庫清單與結果中的資料庫，確保篩選選項完整
                SetDatabaseOptions(_serverDatabases.Concat(results.Select(m => m.DatabaseName)));

                // 重置篩選
                DatabaseFilter = "全部";
                TableFilter = string.Empty;
                MinUpdates = 0;

                ApplyFilter();

                HasData = results.Count > 0;
                TotalCount = results.Count;
                StatusMessage = $"分析完成，共 {results.Count} 個未使用索引";
            });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消分析";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分析失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteDropIndexAsync(UnusedIndex? index)
    {
        if (_service == null || index == null) return;

        var message = $"確定要刪除以下索引嗎？此操作無法復原！\n\n" +
                      $"{index.DropIndexStatement}\n\n" +
                      $"資料庫：{index.DatabaseName}\n" +
                      $"資料表：{index.SchemaName}.{index.TableName}\n" +
                      $"索引欄位：{index.IndexColumns}\n" +
                      $"索引大小：{index.IndexSizeMB:N2} MB\n" +
                      $"更新次數：{index.UserUpdates:N0}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed)
            {
                StatusMessage = "已取消刪除";
                return;
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"正在刪除索引：{index.IndexName}...";

            await _service.ExecuteDropIndexAsync(
                index.DropIndexStatement,
                index.DatabaseName,
                _cancellationTokenSource?.Token ?? CancellationToken.None);

            StatusMessage = $"索引刪除成功：{index.IndexName}";

            // 從集合中移除已刪除的索引
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allUnusedIndexes = _allUnusedIndexes
                    .Where(x => x != index).ToList();
                UnusedIndexes.Remove(index);
                TotalCount = _allUnusedIndexes.Count;
                FilteredCount = UnusedIndexes.Count;
                CriticalCount = UnusedIndexes.Count(m => m.SeverityLevel == "Critical");
                HighCount = UnusedIndexes.Count(m => m.SeverityLevel == "High");
                TotalWastedSizeMB = UnusedIndexes.Sum(m => m.IndexSizeMB);
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"索引刪除失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CopyDropStatementAsync(UnusedIndex? index)
    {
        if (index == null) return;

        try
        {
            await CopyToClipboardAsync(index.DropIndexStatement);
            StatusMessage = "已複製 DROP INDEX 語法到剪貼簿";
        }
        catch (Exception ex)
        {
            StatusMessage = $"複製失敗: {ex.Message}";
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
        DatabaseFilter = "全部";
        TableFilter = string.Empty;
        MinUpdates = 0;
    }

    #endregion

    #region 篩選變更通知

    partial void OnDatabaseFilterChanged(string? value) => ApplyFilter();
    partial void OnTableFilterChanged(string? value) => ApplyFilter();
    partial void OnMinUpdatesChanged(long? value) => ApplyFilter();

    #endregion

    #region 篩選邏輯

    private void ApplyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var filtered = _allUnusedIndexes.AsEnumerable();

            // 資料庫篩選
            if (!string.IsNullOrEmpty(DatabaseFilter) && DatabaseFilter != "全部")
            {
                filtered = filtered.Where(m =>
                    m.DatabaseName.Equals(DatabaseFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 資料表或索引名稱搜尋
            if (!string.IsNullOrEmpty(TableFilter))
            {
                filtered = filtered.Where(m =>
                    m.TableName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase) ||
                    m.IndexName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 最小更新次數門檻
            if (MinUpdates.HasValue && MinUpdates.Value > 0)
            {
                filtered = filtered.Where(m => m.UserUpdates >= MinUpdates.Value);
            }

            var result = filtered.ToList();

            UnusedIndexes.Clear();
            foreach (var item in result)
            {
                UnusedIndexes.Add(item);
            }

            // 更新摘要
            FilteredCount = result.Count;
            CriticalCount = result.Count(m => m.SeverityLevel == "Critical");
            HighCount = result.Count(m => m.SeverityLevel == "High");
            TotalWastedSizeMB = result.Sum(m => m.IndexSizeMB);
            HasData = UnusedIndexes.Count > 0;

            if (_allUnusedIndexes.Count > 0 && result.Count != _allUnusedIndexes.Count)
            {
                StatusMessage = $"篩選結果：{result.Count} / {_allUnusedIndexes.Count} 個索引";
            }
        });
    }

    #endregion

    #region 輔助方法

    private static async Task CopyToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    #endregion
}
