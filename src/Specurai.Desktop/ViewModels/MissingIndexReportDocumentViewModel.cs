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
/// 缺少索引報表文件 ViewModel
/// </summary>
public partial class MissingIndexReportDocumentViewModel : DocumentViewModel
{
    private readonly IPerformanceDiagnosticsService? _service;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "MissingIndexReport";
    public override string DocumentKey => DocumentType;

    #region 原始資料緩存

    private IReadOnlyList<MissingIndex> _allMissingIndexes = [];

    /// <summary>伺服器上的所有使用者資料庫（開啟報表時載入，供篩選選項使用）</summary>
    private IReadOnlyList<string> _serverDatabases = [];

    #endregion

    #region 狀態屬性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMissingIndexesCommand))]
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
    private decimal? _minScore;

    #endregion

    #region 篩選選項

    /// <summary>資料庫篩選選項</summary>
    public ObservableCollection<string> DatabaseOptions { get; } = ["全部"];

    /// <summary>改善指標門檻選項</summary>
    public ObservableCollection<decimal> MinScoreOptions { get; } =
        [0, 1000, 10000, 100000, 1000000, 10000000];

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

    #endregion

    #region 資料集合

    /// <summary>篩選後的缺少索引集合</summary>
    public ObservableCollection<MissingIndex> MissingIndexes { get; } = [];

    [ObservableProperty]
    private MissingIndex? _selectedMissingIndex;

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
    public MissingIndexReportDocumentViewModel()
    {
        Title = "缺少索引報表";
        Icon = "📋";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public MissingIndexReportDocumentViewModel(IPerformanceDiagnosticsService service) : this()
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
            SetDatabaseOptions(_serverDatabases.Concat(_allMissingIndexes.Select(m => m.DatabaseName)));

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
    private async Task LoadMissingIndexesAsync()
    {
        if (_service == null) return;

        IsLoading = true;
        StatusMessage = "正在分析缺少索引...";
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var results = await _service.GetMissingIndexesAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allMissingIndexes = results;

                // 合併伺服器資料庫清單與結果中的資料庫，確保篩選選項完整
                SetDatabaseOptions(_serverDatabases.Concat(results.Select(m => m.DatabaseName)));

                // 重置篩選
                DatabaseFilter = "全部";
                TableFilter = string.Empty;
                MinScore = 0;

                ApplyFilter();

                HasData = results.Count > 0;
                TotalCount = results.Count;
                StatusMessage = $"分析完成，共 {results.Count} 個缺少索引建議";
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
    private async Task ExecuteCreateIndexAsync(MissingIndex? index)
    {
        if (_service == null || index == null) return;

        // 確認對話框
        var message = $"確定要在資料庫上執行以下建立索引語法嗎？\n\n{index.CreateIndexStatement}\n\n資料表：{index.TableName}\n改善指標：{index.ImprovementMeasure:N2}";

        if (ConfirmExecuteCallback != null)
        {
            var confirmed = await ConfirmExecuteCallback(message);
            if (!confirmed)
            {
                StatusMessage = "已取消執行";
                return;
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"正在建立索引：{index.ShortTableName}...";

            await _service.ExecuteCreateIndexAsync(
                index.CreateIndexStatement,
                _cancellationTokenSource?.Token ?? CancellationToken.None);

            StatusMessage = $"索引建立成功：{index.ShortTableName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"索引建立失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CopyCreateIndexStatementAsync(MissingIndex? index)
    {
        if (index == null) return;

        try
        {
            await CopyToClipboardAsync(index.CreateIndexStatement);
            StatusMessage = "已複製建立索引語法到剪貼簿";
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
        MinScore = 0;
    }

    #endregion

    #region 篩選變更通知

    partial void OnDatabaseFilterChanged(string? value) => ApplyFilter();
    partial void OnTableFilterChanged(string? value) => ApplyFilter();
    partial void OnMinScoreChanged(decimal? value) => ApplyFilter();

    #endregion

    #region 篩選邏輯

    private void ApplyFilter()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var filtered = _allMissingIndexes.AsEnumerable();

            // 資料庫篩選
            if (!string.IsNullOrEmpty(DatabaseFilter) && DatabaseFilter != "全部")
            {
                filtered = filtered.Where(m =>
                    m.DatabaseName.Equals(DatabaseFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 資料表名稱搜尋
            if (!string.IsNullOrEmpty(TableFilter))
            {
                filtered = filtered.Where(m =>
                    m.TableName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase) ||
                    m.ShortTableName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 改善指標門檻
            if (MinScore.HasValue && MinScore.Value > 0)
            {
                filtered = filtered.Where(m => m.ImprovementMeasure >= MinScore.Value);
            }

            var result = filtered.ToList();

            MissingIndexes.Clear();
            foreach (var item in result)
            {
                MissingIndexes.Add(item);
            }

            // 更新摘要
            FilteredCount = result.Count;
            CriticalCount = result.Count(m => m.SeverityLevel == "Critical");
            HighCount = result.Count(m => m.SeverityLevel == "High");
            HasData = MissingIndexes.Count > 0;

            if (_allMissingIndexes.Count > 0 && result.Count != _allMissingIndexes.Count)
            {
                StatusMessage = $"篩選結果：{result.Count} / {_allMissingIndexes.Count} 個建議";
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
