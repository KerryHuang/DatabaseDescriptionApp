using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 欄位搜尋文件 ViewModel
/// </summary>
public partial class ColumnSearchDocumentViewModel : DocumentViewModel
{
    private readonly ISqlQueryRepository? _sqlQueryRepository;
    private readonly IColumnTypeRepository? _columnTypeRepository;
    private readonly IConnectionManager? _connectionManager;
    private static int _instanceCount;

    [ObservableProperty]
    private string _columnSearchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private ColumnTypeGroupViewModel? _selectedGroup;

    [ObservableProperty]
    private ColumnTypeInfo? _selectedColumnType;

    [ObservableProperty]
    private bool _showTypeAnalysis;

    [ObservableProperty]
    private int _newLength;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private string _detailFilter = "全部";

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<ColumnSearchResult> ColumnSearchResults { get; } = [];
    public ObservableCollection<ColumnTypeGroupViewModel> ColumnGroups { get; } = [];
    public ObservableCollection<ColumnTypeInfo> FilteredColumns { get; } = [];

    /// <summary>
    /// 篩選選項
    /// </summary>
    public string[] FilterOptions { get; } = ["全部", "僅不一致", "僅一致"];

    public override string DocumentType => "ColumnSearch";

    public override string DocumentKey => $"{DocumentType}:{_instanceId}";

    private readonly int _instanceId;

    public ColumnSearchDocumentViewModel()
    {
        // Design-time constructor
        _instanceId = ++_instanceCount;
        Title = $"欄位搜尋 {_instanceId}";
        Icon = "🔍";
    }

    public ColumnSearchDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IColumnTypeRepository columnTypeRepository,
        IConnectionManager connectionManager)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _columnTypeRepository = columnTypeRepository;
        _connectionManager = connectionManager;
        _instanceId = ++_instanceCount;
        Title = $"欄位搜尋 {_instanceId}";
        Icon = "🔍";
        CanClose = true;

        LoadConnectionProfiles();
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

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null && _connectionManager != null)
        {
            _connectionManager.SetCurrentProfile(value.Id);
            StatusMessage = $"已切換至：{value.Name}";
        }
    }

    partial void OnSelectedGroupChanged(ColumnTypeGroupViewModel? value)
    {
        SelectedColumnType = null;
        NewLength = 0;
        UpdateFilteredColumns();
    }

    partial void OnDetailFilterChanged(string value)
    {
        UpdateFilteredColumns();
    }

    /// <summary>
    /// 更新篩選後的欄位清單（不一致的排在最上面）
    /// </summary>
    private void UpdateFilteredColumns()
    {
        FilteredColumns.Clear();

        if (SelectedGroup == null)
            return;

        var columns = SelectedGroup.Columns.AsEnumerable();

        // 套用篩選
        columns = DetailFilter switch
        {
            "僅不一致" => columns.Where(c => !c.IsConsistent),
            "僅一致" => columns.Where(c => c.IsConsistent),
            _ => columns
        };

        // 排序：不一致的排在最上面，然後按 Schema 和 TableName 排序
        columns = columns
            .OrderBy(c => c.IsConsistent)  // false (不一致) 排在前面
            .ThenBy(c => c.SchemaName)
            .ThenBy(c => c.TableName);

        foreach (var column in columns)
        {
            FilteredColumns.Add(column);
        }
    }

    partial void OnSelectedColumnTypeChanged(ColumnTypeInfo? value)
    {
        if (value != null)
        {
            // 預設新長度為目前長度
            NewLength = value.MaxLength;
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
            ColumnGroups.Clear();
            ShowTypeAnalysis = false;

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
        ColumnGroups.Clear();
        SelectedGroup = null;
        SelectedColumnType = null;
        ShowTypeAnalysis = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task AnalyzeConsistencyAsync()
    {
        if (_columnTypeRepository == null || ColumnSearchResults.Count == 0)
            return;

        try
        {
            IsSearching = true;
            StatusMessage = "分析型態一致性中...";
            ColumnGroups.Clear();

            // 取得所有不重複的欄位名稱（僅限 TABLE 的欄位，不區分大小寫）
            var tableColumns = ColumnSearchResults
                .Where(r => r.ObjectType == "TABLE")
                .Select(r => r.ColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var processedCount = 0;
            foreach (var columnName in tableColumns)
            {
                processedCount++;
                StatusMessage = $"分析中 ({processedCount}/{tableColumns.Count})：{columnName}";

                // 直接查詢（SQL Server 通常不區分大小寫）
                var types = await _columnTypeRepository.GetColumnTypesAsync(columnName);

                // 移除重複的結果（以 Schema + TableName 為 key）
                var uniqueTypes = types
                    .GroupBy(t => $"{t.SchemaName}.{t.TableName}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                if (uniqueTypes.Count > 0)
                {
                    var group = new ColumnTypeGroupViewModel
                    {
                        ColumnName = columnName
                    };

                    foreach (var typeInfo in uniqueTypes)
                    {
                        group.Columns.Add(typeInfo);
                    }

                    group.RefreshCalculatedProperties();
                    ColumnGroups.Add(group);
                }
            }

            // 依一致性等級排序（嚴重 > 警告 > 一致）
            var sortedGroups = ColumnGroups
                .OrderByDescending(g => (int)g.Level)
                .ThenBy(g => g.ColumnName)
                .ToList();

            ColumnGroups.Clear();
            foreach (var group in sortedGroups)
            {
                ColumnGroups.Add(group);
            }

            ShowTypeAnalysis = true;

            var severeCount = ColumnGroups.Count(g => g.Level == ConsistencyLevel.Severe);
            var warningCount = ColumnGroups.Count(g => g.Level == ConsistencyLevel.Warning);
            var consistentCount = ColumnGroups.Count(g => g.Level == ConsistencyLevel.Consistent);

            StatusMessage = $"分析完成：{ColumnGroups.Count} 個欄位（嚴重 {severeCount}、警告 {warningCount}、一致 {consistentCount}）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分析錯誤：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task UpdateColumnLengthAsync(ColumnTypeInfo? columnInfo)
    {
        if (_columnTypeRepository == null || columnInfo == null)
            return;

        if (NewLength <= 0 && NewLength != -1)
        {
            StatusMessage = "請輸入有效的長度（正整數或 -1 表示 MAX）";
            return;
        }

        // 檢查是否為縮短長度
        if (NewLength != -1 && NewLength < columnInfo.MaxLength && columnInfo.MaxLength != -1)
        {
            try
            {
                var maxDataLength = await _columnTypeRepository.GetMaxDataLengthAsync(
                    columnInfo.SchemaName, columnInfo.TableName, columnInfo.ColumnName);

                if (maxDataLength > NewLength)
                {
                    StatusMessage = $"警告：目前資料最大長度為 {maxDataLength}，無法縮短至 {NewLength}";
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"檢查資料長度錯誤：{ex.Message}";
                return;
            }
        }

        try
        {
            IsUpdating = true;
            StatusMessage = $"正在更新 [{columnInfo.SchemaName}].[{columnInfo.TableName}].[{columnInfo.ColumnName}] 的長度...";

            var success = await _columnTypeRepository.UpdateColumnLengthAsync(
                columnInfo.SchemaName,
                columnInfo.TableName,
                columnInfo.ColumnName,
                NewLength);

            if (success)
            {
                // 更新 UI 中的資料
                var lengthSpec = NewLength == -1 ? "MAX" : NewLength.ToString();
                columnInfo.MaxLength = NewLength;
                columnInfo.DataType = $"{columnInfo.BaseType}({lengthSpec})";

                // 重新整理群組的計算屬性
                SelectedGroup?.RefreshCalculatedProperties();

                StatusMessage = $"成功更新 [{columnInfo.SchemaName}].[{columnInfo.TableName}].[{columnInfo.ColumnName}] 的長度為 {lengthSpec}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失敗：{ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task RefreshConstraintsAsync(ColumnTypeInfo? columnInfo)
    {
        if (_columnTypeRepository == null || columnInfo == null)
            return;

        try
        {
            StatusMessage = $"載入約束資訊...";

            var constraints = await _columnTypeRepository.GetColumnConstraintsAsync(
                columnInfo.SchemaName,
                columnInfo.TableName,
                columnInfo.ColumnName);

            columnInfo.Constraints = constraints.ToList();

            StatusMessage = $"[{columnInfo.FullTableName}].[{columnInfo.ColumnName}] 有 {constraints.Count} 個約束";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入約束錯誤：{ex.Message}";
        }
    }
}
