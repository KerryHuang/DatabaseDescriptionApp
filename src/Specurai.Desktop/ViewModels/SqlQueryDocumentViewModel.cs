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
using Specurai.Application.Models;
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
    private readonly IUpdateSqlGenerator? _updateSqlGenerator;
    private readonly IDmlExecutionService? _dmlExecutionService;
    private readonly IDdlExecutionService? _ddlExecutionService;
    private QueryResultWithSchema? _lastQueryResult;

    /// <summary>原值快照：以列物件參照為鍵，排序/重排不影響配對</summary>
    private readonly Dictionary<Dictionary<string, object?>, Dictionary<string, object?>> _originalByRow
        = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, string> _columnDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private string? _localConnectionString;
    /// <summary>選到的連線設定檔已停用（GetConnectionString 回 null）：查詢不可靜默改用目前連線</summary>
    private bool _selectedConnectionDisabled;
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

    /// <summary>查詢結果是否可編輯（單一資料表來源才開放）</summary>
    [ObservableProperty]
    private bool _isResultEditable;

    /// <summary>無主鍵時的定位欄挑選回呼（View 掛真對話框，測試掛假回呼）；回傳 null 表示略過</summary>
    public Func<IReadOnlyList<string>, Task<IReadOnlyList<string>?>>? PickKeyColumnsAsync { get; set; }

    /// <summary>顯示產生 SQL 的回呼（View 掛 SqlPreviewWindow）</summary>
    public Func<string, Task>? ShowGeneratedSqlAsync { get; set; }

    /// <summary>執行 DML 前的確認回呼（View 掛真對話框，測試掛假回呼）；null 或回傳 false 時不執行</summary>
    public Func<string, Task<bool>>? ConfirmExecuteCallback { get; set; }

    /// <summary>是否可執行 DML：非正式環境連線且服務可用（Production 一律停用）</summary>
    public bool CanExecuteDml =>
        _dmlExecutionService != null
        && SelectedProfile != null
        && SelectedProfile.Environment != DatabaseEnvironment.Production
        && !_selectedConnectionDisabled;

    /// <summary>是否可執行 DDL：非正式環境連線且服務可用（Production 一律停用）</summary>
    public bool CanExecuteDdl =>
        _ddlExecutionService != null
        && SelectedProfile != null
        && SelectedProfile.Environment != DatabaseEnvironment.Production
        && !_selectedConnectionDisabled;

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
        ISqlDryRunRepository? sqlDryRunRepository = null,
        IUpdateSqlGenerator? updateSqlGenerator = null,
        IDmlExecutionService? dmlExecutionService = null,
        IDdlExecutionService? ddlExecutionService = null)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
        _sqlDryRunRepository = sqlDryRunRepository;
        _updateSqlGenerator = updateSqlGenerator;
        _dmlExecutionService = dmlExecutionService;
        _ddlExecutionService = ddlExecutionService;
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
        var profiles = _connectionManager?.GetEnabledProfiles() ?? [];
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
            var descriptions = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlQueryRepository.GetColumnDescriptionsAsync(_localConnectionString)
                : await _sqlQueryRepository.GetColumnDescriptionsAsync();
            _columnDescriptions = descriptions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                _selectedConnectionDisabled = false;
            }
            else
            {
                // 使用者手動選擇了「不同」的連線設定檔：屬於明確指定，
                // 才釘住該設定檔的預設資料庫連線字串。
                _localConnectionString = _connectionManager.GetConnectionString(value.Id);

                if (_localConnectionString == null)
                {
                    // 該連線已被停用（清單可能是舊的）：不可落入「跟隨目前連線」的路徑，
                    // 否則查詢會靜默跑到另一個資料庫。明確告知使用者改選其他連線，並擋下查詢執行。
                    _selectedConnectionDisabled = true;
                    StatusMessage = "此連線已停用，請改選其他連線。";
                    OnPropertyChanged(nameof(CanExecuteDml));
                    ExecuteDmlCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(CanExecuteDdl));
                    ExecuteDdlCommand.NotifyCanExecuteChanged();
                    return;
                }

                _selectedConnectionDisabled = false;
            }

            StatusMessage = $"已切換至：{value.Name}";
            _ = LoadColumnDescriptionsAsync();
        }

        OnPropertyChanged(nameof(CanExecuteDml));
        ExecuteDmlCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecuteDdl));
        ExecuteDdlCommand.NotifyCanExecuteChanged();
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

        if (_selectedConnectionDisabled)
        {
            StatusMessage = "此連線已停用，請改選其他連線。";
            return;
        }

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";

        try
        {
            IsExecuting = true;
            StatusMessage = "執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

            var stopwatch = Stopwatch.StartNew();
            var result = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlQueryRepository.ExecuteQueryWithSchemaAsync(sql, _localConnectionString)
                : await _sqlQueryRepository.ExecuteQueryWithSchemaAsync(sql);
            stopwatch.Stop();

            var dataTable = result.Table;
            _lastQueryResult = result;
            IsResultEditable = result.IsSingleTable;

            var metaByName = result.Columns
                .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 建立欄位（包含描述；可編輯結果依中繼資料設定唯讀欄與雙向綁定）
            foreach (DataColumn col in dataTable.Columns)
            {
                var headerText = col.ColumnName;
                if (_columnDescriptions.TryGetValue(col.ColumnName, out var description)
                    && !string.IsNullOrWhiteSpace(description))
                {
                    headerText = $"{col.ColumnName}\n({description})";
                }

                var meta = metaByName.GetValueOrDefault(col.ColumnName);
                var editable = IsResultEditable && meta is { IsReadOnly: false };

                ResultColumns.Add(new DataGridTextColumn
                {
                    Header = headerText,
                    Binding = new Avalonia.Data.Binding($"[{col.ColumnName}]")
                    {
                        Mode = editable ? Avalonia.Data.BindingMode.TwoWay : Avalonia.Data.BindingMode.OneWay
                    },
                    IsReadOnly = !editable,
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

            // 快照原值：產生異動 SQL 時以此比對
            _originalByRow.Clear();
            foreach (var row in QueryResults)
            {
                _originalByRow[row] = new Dictionary<string, object?>(row);
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

        if (_selectedConnectionDisabled)
        {
            StatusMessage = "此連線已停用，請改選其他連線。";
            return;
        }

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
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

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

    /// <summary>
    /// 執行 DML：先預演取得影響筆數，經使用者確認後才 COMMIT 寫入。
    /// 環境閘門在 IDmlExecutionService（Production 拒絕），此處僅控制 UI 可用性與確認流程。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDml))]
    private async Task ExecuteDmlAsync()
    {
        if (_dmlExecutionService == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        if (_selectedConnectionDisabled)
        {
            StatusMessage = "此連線已停用，請改選其他連線。";
            return;
        }

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";
        // 目前連線（_localConnectionString == null）傳 null 跟隨資料庫覆寫；
        // 明確選擇的其他連線傳其 Id
        var profileId = _localConnectionString == null ? (Guid?)null : SelectedProfile?.Id;

        try
        {
            IsExecuting = true;
            StatusMessage = "預演中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

            var preview = await _dmlExecutionService.ExecuteAsync(sql, confirm: false, profileId);

            if (!preview.IsValid)
            {
                StatusMessage = preview.SyntaxErrors.Count > 0
                    ? $"語法錯誤（第 {preview.SyntaxErrors[0].Line} 行第 {preview.SyntaxErrors[0].Column} 列）：{preview.SyntaxErrors[0].Message}"
                    : preview.RejectReason ?? "驗證未通過";
                return;
            }

            if (preview.ExecutionError != null)
            {
                StatusMessage = preview.ExecutionError;
                DryRunWarnings = string.Join("\n", preview.Warnings);
                return;
            }

            // 跟隨目前連線時，SelectedProfile 可能是開分頁當下的快照，與執行當下的實際連線／資料庫不同步
            // （使用者事後於側邊欄切換連線或資料庫）；確認訊息一律以執行當下的真實目標為準。
            var targetName = _localConnectionString == null
                ? _connectionManager?.GetCurrentProfile()?.Name ?? SelectedProfile?.Name
                : SelectedProfile?.Name;
            var targetDatabase = _localConnectionString == null
                ? _connectionManager?.GetCurrentDatabase()
                : SelectedProfile?.Database;
            var targetNote = string.IsNullOrEmpty(targetDatabase) ? "" : $"（資料庫：{targetDatabase}）";

            var confirmed = ConfirmExecuteCallback != null
                && await ConfirmExecuteCallback(
                    $"將對「{targetName}」{targetNote}執行 {preview.StatementType}，影響 {preview.AffectedRowCount} 筆。\n" +
                    "此操作會 COMMIT 寫入資料庫，確定執行？");

            if (!confirmed)
            {
                StatusMessage = "已取消，資料庫未變更。";
                return;
            }

            StatusMessage = "執行中...";
            var result = await _dmlExecutionService.ExecuteAsync(sql, confirm: true, profileId);

            if (!result.IsValid)
            {
                StatusMessage = result.RejectReason ?? "驗證未通過";
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
            var committedNote = result.Committed ? "已寫入資料庫" : "未確認已寫入，請檢查";
            StatusMessage = $"執行完成{selectionNote}：影響 {result.AffectedRowCount} 筆（{result.StatementType}）｜{committedNote}";
            AddToHistory(sql);
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// 執行 DDL：先預演取得逐句摘要，經使用者確認後才 COMMIT 變更 schema。
    /// 環境閘門在 IDdlExecutionService（Production 拒絕），此處僅控制 UI 可用性與確認流程。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDdl))]
    private async Task ExecuteDdlAsync()
    {
        if (_ddlExecutionService == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        if (_selectedConnectionDisabled)
        {
            StatusMessage = "此連線已停用，請改選其他連線。";
            return;
        }

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";
        // 目前連線（_localConnectionString == null）傳 null 跟隨資料庫覆寫；
        // 明確選擇的其他連線傳其 Id
        var profileId = _localConnectionString == null ? (Guid?)null : SelectedProfile?.Id;

        try
        {
            IsExecuting = true;
            StatusMessage = "DDL 預演中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

            var preview = await _ddlExecutionService.ExecuteAsync(sql, confirm: false, profileId);

            if (!preview.IsValid)
            {
                StatusMessage = preview.SyntaxErrors.Count > 0
                    ? $"語法錯誤（第 {preview.SyntaxErrors[0].Line} 行第 {preview.SyntaxErrors[0].Column} 列）：{preview.SyntaxErrors[0].Message}"
                    : preview.RejectReason ?? "驗證未通過";
                return;
            }

            if (preview.ExecutionError != null)
            {
                StatusMessage = preview.ExecutionError;
                return;
            }

            var summary = string.Join("\n", preview.Statements
                .Select(s => $"{s.Index}. {s.Type} {s.ObjectName}".TrimEnd()));

            // 跟隨目前連線時，SelectedProfile 可能是開分頁當下的快照，確認訊息一律以執行當下的真實目標為準
            var targetName = _localConnectionString == null
                ? _connectionManager?.GetCurrentProfile()?.Name ?? SelectedProfile?.Name
                : SelectedProfile?.Name;
            var targetDatabase = _localConnectionString == null
                ? _connectionManager?.GetCurrentDatabase()
                : SelectedProfile?.Database;
            var targetNote = string.IsNullOrEmpty(targetDatabase) ? "" : $"（資料庫：{targetDatabase}）";

            var confirmed = ConfirmExecuteCallback != null
                && await ConfirmExecuteCallback(
                    $"將對「{targetName}」{targetNote}執行 {preview.Statements.Count} 句 DDL：\n{summary}\n" +
                    "此操作會 COMMIT 變更 schema，確定執行？");

            if (!confirmed)
            {
                StatusMessage = "已取消，資料庫未變更。";
                return;
            }

            StatusMessage = "DDL 執行中...";
            var result = await _ddlExecutionService.ExecuteAsync(sql, confirm: true, profileId);

            if (!result.IsValid)
            {
                StatusMessage = result.RejectReason ?? "驗證未通過";
                return;
            }

            if (result.ExecutionError != null)
            {
                StatusMessage = result.ExecutionError;
                return;
            }

            DryRunWarnings = summary;
            var committedNote = result.Committed ? "已寫入資料庫" : "未確認已寫入，請檢查";
            StatusMessage = $"DDL 執行完成{selectionNote}：{result.Statements.Count} 句｜{committedNote}";
            AddToHistory(sql);
        }
        catch (Exception ex)
        {
            StatusMessage = $"DDL 執行失敗：{ex.Message}";
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
        IsResultEditable = false;
        _lastQueryResult = null;
        _originalByRow.Clear();
    }

    /// <summary>
    /// 比對結果格的編輯差異，產生 UPDATE 語句（僅產生文字，不執行任何寫入）
    /// </summary>
    [RelayCommand]
    private async Task GenerateUpdateSqlAsync()
    {
        if (_updateSqlGenerator == null)
            return;

        if (_lastQueryResult is not { IsSingleTable: true } schema || !IsResultEditable)
        {
            StatusMessage = "僅支援單一資料表的查詢結果。";
            return;
        }

        if (QueryResults.Any(r => !_originalByRow.ContainsKey(r)))
        {
            StatusMessage = "結果列與快照不一致，請重新執行查詢。";
            return;
        }

        // 主鍵優先；無主鍵讓使用者挑選定位欄；略過則全欄位原值 fallback
        var keyColumns = schema.Columns.Where(c => c.IsKey).Select(c => c.ColumnName).ToList();
        var isFallback = false;
        if (keyColumns.Count == 0)
        {
            var candidates = schema.Columns
                .Where(c => !string.IsNullOrEmpty(c.BaseColumn) && c.ClrType != typeof(byte[]))
                .Select(c => c.ColumnName)
                .ToList();

            var picked = PickKeyColumnsAsync != null ? await PickKeyColumnsAsync(candidates) : null;
            if (picked is { Count: > 0 })
            {
                keyColumns = picked.ToList();
            }
            else
            {
                keyColumns = candidates;
                isFallback = true;
            }
        }

        var rows = QueryResults
            .Select(current => new UpdateSqlRow { Original = _originalByRow[current], Current = current })
            .ToList();

        var result = _updateSqlGenerator.Generate(new UpdateSqlRequest
        {
            TargetSchema = schema.TargetSchema,
            TargetTable = schema.TargetTable!,
            Columns = schema.Columns,
            KeyColumns = keyColumns,
            IsFallbackKeys = isFallback,
            Rows = rows
        });

        if (result.StatementCount == 0)
        {
            StatusMessage = result.Warnings.Count > 0 ? string.Join("；", result.Warnings) : "無異動。";
            return;
        }

        var warningNote = result.Warnings.Count > 0 ? $"（{string.Join("；", result.Warnings)}）" : "";
        StatusMessage = $"已產生 {result.StatementCount} 句 UPDATE{warningNote}";

        if (ShowGeneratedSqlAsync != null)
            await ShowGeneratedSqlAsync(result.Sql);
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
