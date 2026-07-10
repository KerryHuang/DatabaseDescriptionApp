using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// SQL 查詢文件 ViewModel
/// </summary>
public partial class SqlQueryDocumentViewModel : DocumentViewModel
{
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly IConnectionManager? _connectionManager;
    private readonly ISqlDryRunRepository? _sqlDryRunRepository;
    private Dictionary<string, string> _columnDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private string? _localConnectionString;
    private static int _instanceCount;

    [ObservableProperty]
    private string _sqlText = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private long _executionTimeMs;

    [ObservableProperty]
    private string _dryRunWarnings = string.Empty;

    /// <summary>編輯器選取起點（與 TextBox.SelectionStart 雙向綁定）</summary>
    [ObservableProperty]
    private int _selectionStart;

    /// <summary>編輯器選取終點（與 TextBox.SelectionEnd 雙向綁定）</summary>
    [ObservableProperty]
    private int _selectionEnd;

    /// <summary>是否有 Dry Run 警告需要顯示（供警告列 IsVisible 綁定）</summary>
    public bool HasDryRunWarnings => !string.IsNullOrEmpty(DryRunWarnings);

    partial void OnDryRunWarningsChanged(string value) => OnPropertyChanged(nameof(HasDryRunWarnings));

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> QueryResults { get; } = [];
    public ObservableCollection<DataGridColumn> ResultColumns { get; } = [];
    public ObservableCollection<string> QueryHistory { get; } = [];

    public override string DocumentType => "SqlQuery";

    public override string DocumentKey => $"{DocumentType}:{_instanceId}";

    private readonly int _instanceId;

    public SqlQueryDocumentViewModel()
    {
        // Design-time constructor
        _instanceId = ++_instanceCount;
        Title = $"SQL 查詢 {_instanceId}";
        Icon = "📝";
    }

    public SqlQueryDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IConnectionManager connectionManager,
        ISqlDryRunRepository? sqlDryRunRepository = null)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
        _sqlDryRunRepository = sqlDryRunRepository;
        _instanceId = ++_instanceCount;
        Title = $"SQL 查詢 {_instanceId}";
        Icon = "📝";
        CanClose = true;

        LoadConnectionProfiles();
        _ = LoadColumnDescriptionsAsync();
    }

    private void LoadConnectionProfiles()
    {
        ConnectionProfiles.Clear();
        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
        }

        // 選擇目前的連線
        var currentProfile = _connectionManager?.GetCurrentProfile();
        if (currentProfile != null)
        {
            SelectedProfile = ConnectionProfiles.FirstOrDefault(p => p.Id == currentProfile.Id);
        }
    }

    private async Task LoadColumnDescriptionsAsync()
    {
        if (_sqlQueryRepository == null) return;

        try
        {
            if (!string.IsNullOrEmpty(_localConnectionString))
                _columnDescriptions = await _sqlQueryRepository.GetColumnDescriptionsAsync(_localConnectionString);
            else
                _columnDescriptions = await _sqlQueryRepository.GetColumnDescriptionsAsync();
        }
        catch
        {
            _columnDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null && _connectionManager != null)
        {
            var currentProfile = _connectionManager.GetCurrentProfile();
            if (currentProfile != null && currentProfile.Id == value.Id)
            {
                // 選到的仍是目前使用中的連線設定檔：不可釘住其預設資料庫的連線字串，
                // 否則會蓋過側邊欄「目前資料庫覆寫」（GetCurrentConnectionString）。
                // 保持 null，讓查詢在執行當下透過 Repository 的 Func<string?>
                // 重新解析 GetCurrentConnectionString()，跟隨最新切換的資料庫。
                _localConnectionString = null;
            }
            else
            {
                // 使用者手動選擇了「不同」的連線設定檔：屬於明確指定，
                // 才釘住該設定檔的預設資料庫連線字串。
                _localConnectionString = _connectionManager.GetConnectionString(value.Id);
            }

            StatusMessage = $"已切換至：{value.Name}";
            _ = LoadColumnDescriptionsAsync();
        }
    }

    /// <summary>
    /// 取得要執行的 SQL：編輯器有非空白的選取範圍時只取選取文字（SSMS 行為），否則取全文。
    /// 反向選取以 min/max 正規化；索引超出目前文字長度時鉗制在合法範圍。
    /// </summary>
    private (string Sql, bool IsSelection) GetEffectiveSql()
    {
        var text = SqlText;
        var start = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, text.Length);

        if (end > start)
        {
            var selected = text[start..end];
            if (!string.IsNullOrWhiteSpace(selected))
                return (selected.Trim(), true);
        }

        return (text.Trim(), false);
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        if (_sqlQueryRepository == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";

        try
        {
            IsExecuting = true;
            StatusMessage = "執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;

            var stopwatch = Stopwatch.StartNew();
            var dataTable = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlQueryRepository.ExecuteQueryAsync(sql, _localConnectionString)
                : await _sqlQueryRepository.ExecuteQueryAsync(sql);
            stopwatch.Stop();

            // 建立欄位（包含描述）
            foreach (DataColumn col in dataTable.Columns)
            {
                var headerText = col.ColumnName;
                if (_columnDescriptions.TryGetValue(col.ColumnName, out var description)
                    && !string.IsNullOrWhiteSpace(description))
                {
                    headerText = $"{col.ColumnName}\n({description})";
                }

                ResultColumns.Add(new DataGridTextColumn
                {
                    Header = headerText,
                    Binding = new Avalonia.Data.Binding($"[{col.ColumnName}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                });
            }

            // 轉換資料
            foreach (DataRow row in dataTable.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (DataColumn col in dataTable.Columns)
                {
                    dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                }
                QueryResults.Add(dict);
            }

            RowCount = dataTable.Rows.Count;
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            StatusMessage = $"查詢完成{selectionNote}：{RowCount} 筆資料，耗時 {ExecutionTimeMs} ms";

            // 加入歷史記錄（記實際執行的那段）
            AddToHistory(sql);
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤：{ex.Message}";
            QueryResults.Clear();
            ResultColumns.Clear();
            RowCount = 0;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// Dry Run 預演 DML：交易中執行取得影響筆數與前後對照，一律回滾
    /// </summary>
    [RelayCommand]
    private async Task DryRunAsync()
    {
        if (_sqlDryRunRepository == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";

        try
        {
            IsExecuting = true;
            StatusMessage = "Dry Run 執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;

            var stopwatch = Stopwatch.StartNew();
            var result = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlDryRunRepository.DryRunAsync(sql, _localConnectionString)
                : await _sqlDryRunRepository.DryRunAsync(sql);
            stopwatch.Stop();
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (!result.IsValid)
            {
                StatusMessage = result.SyntaxErrors.Count > 0
                    ? $"語法錯誤（第 {result.SyntaxErrors[0].Line} 行第 {result.SyntaxErrors[0].Column} 列）：{result.SyntaxErrors[0].Message}"
                    : result.RejectReason ?? "Dry run 驗證未通過";
                return;
            }

            if (result.ExecutionError != null)
            {
                StatusMessage = result.ExecutionError;
                DryRunWarnings = string.Join("\n", result.Warnings);
                return;
            }

            if (result.PreviewTable != null)
            {
                foreach (DataColumn col in result.PreviewTable.Columns)
                {
                    ResultColumns.Add(new DataGridTextColumn
                    {
                        Header = col.ColumnName,
                        Binding = new Avalonia.Data.Binding($"[{col.ColumnName}]"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                    });
                }

                foreach (DataRow row in result.PreviewTable.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in result.PreviewTable.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                    }
                    QueryResults.Add(dict);
                }
            }

            RowCount = result.AffectedRowCount;
            DryRunWarnings = string.Join("\n", result.Warnings);
            var truncatedNote = result.PreviewTruncated ? $"（預覽僅顯示前 {QueryResults.Count} 筆）" : "";
            StatusMessage = $"Dry Run 完成{selectionNote}：影響 {result.AffectedRowCount} 筆（{result.StatementType}）{truncatedNote}｜已回滾，資料庫未變更，耗時 {ExecutionTimeMs} ms";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dry run 失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void ClearQuery()
    {
        SqlText = string.Empty;
        QueryResults.Clear();
        ResultColumns.Clear();
        StatusMessage = string.Empty;
        RowCount = 0;
        ExecutionTimeMs = 0;
        DryRunWarnings = string.Empty;
    }

    [RelayCommand]
    private void LoadFromHistory(string? sql)
    {
        if (!string.IsNullOrEmpty(sql))
        {
            SqlText = sql;
        }
    }

    private void AddToHistory(string sql)
    {
        // 移除重複項目
        if (QueryHistory.Contains(sql))
        {
            QueryHistory.Remove(sql);
        }

        // 加入最前面
        QueryHistory.Insert(0, sql);

        // 保留最近 20 筆
        while (QueryHistory.Count > 20)
        {
            QueryHistory.RemoveAt(QueryHistory.Count - 1);
        }
    }
}
