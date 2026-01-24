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
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// SQL 查詢文件 ViewModel
/// </summary>
public partial class SqlQueryDocumentViewModel : DocumentViewModel
{
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly IConnectionManager? _connectionManager;
    private Dictionary<string, string> _columnDescriptions = new(StringComparer.OrdinalIgnoreCase);
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

    public SqlQueryDocumentViewModel(ISqlQueryRepository sqlQueryRepository, IConnectionManager connectionManager)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
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
            _connectionManager.SetCurrentProfile(value.Id);
            StatusMessage = $"已切換至：{value.Name}";
            _ = LoadColumnDescriptionsAsync();
        }
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        if (_sqlQueryRepository == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        try
        {
            IsExecuting = true;
            StatusMessage = "執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();

            var stopwatch = Stopwatch.StartNew();
            var dataTable = await _sqlQueryRepository.ExecuteQueryAsync(SqlText.Trim());
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
            StatusMessage = $"查詢完成：{RowCount} 筆資料，耗時 {ExecutionTimeMs} ms";

            // 加入歷史記錄
            AddToHistory(SqlText.Trim());
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

    [RelayCommand]
    private void ClearQuery()
    {
        SqlText = string.Empty;
        QueryResults.Clear();
        ResultColumns.Clear();
        StatusMessage = string.Empty;
        RowCount = 0;
        ExecutionTimeMs = 0;
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
