using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.ViewModels;

public partial class SqlQueryViewModel : ViewModelBase
{
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly IConnectionManager? _connectionManager;
    private Dictionary<string, string> _columnDescriptions = new(StringComparer.OrdinalIgnoreCase);

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

    [ObservableProperty]
    private string _columnSearchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<Dictionary<string, object?>> QueryResults { get; } = [];
    public ObservableCollection<DataGridColumn> ResultColumns { get; } = [];
    public ObservableCollection<string> QueryHistory { get; } = [];
    public ObservableCollection<ColumnSearchResult> ColumnSearchResults { get; } = [];

    public SqlQueryViewModel()
    {
        // Design-time constructor
    }

    public SqlQueryViewModel(ISqlQueryRepository sqlQueryRepository, IConnectionManager connectionManager)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
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

    [RelayCommand]
    private async Task SearchColumnsAsync()
    {
        if (_sqlQueryRepository == null || string.IsNullOrWhiteSpace(ColumnSearchText))
            return;

        try
        {
            IsSearching = true;
            StatusMessage = "搜尋中...";
            ColumnSearchResults.Clear();

            var results = await _sqlQueryRepository.SearchColumnsAsync(ColumnSearchText.Trim());

            foreach (var result in results)
            {
                ColumnSearchResults.Add(result);
            }

            StatusMessage = $"找到 {ColumnSearchResults.Count} 個符合的欄位/參數";
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜尋錯誤：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void ClearColumnSearch()
    {
        ColumnSearchText = string.Empty;
        ColumnSearchResults.Clear();
        StatusMessage = string.Empty;
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
