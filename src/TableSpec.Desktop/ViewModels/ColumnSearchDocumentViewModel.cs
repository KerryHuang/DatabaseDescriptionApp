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
    private readonly ITableQueryService? _tableQueryService;
    private readonly IColumnSearchService? _columnSearchService;
    private static int _instanceCount;

    [ObservableProperty]
    private string _columnSearchText = string.Empty;

    [ObservableProperty]
    private string _tableSearchText = string.Empty;

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

    [ObservableProperty]
    private int _batchNewLength;

    [ObservableProperty]
    private string _batchUpdateProgress = string.Empty;

    [ObservableProperty]
    private bool _isExactMatch;

    [ObservableProperty]
    private ColumnSearchResult? _selectedSearchResult;

    [ObservableProperty]
    private bool _showApplyDescriptionConfirm;

    [ObservableProperty]
    private string _applyDescriptionPreview = string.Empty;

    [ObservableProperty]
    private int _emptyDescriptionCount;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<ColumnSearchResult> ColumnSearchResults { get; } = [];
    public ObservableCollection<ColumnTypeGroupViewModel> ColumnGroups { get; } = [];
    public ObservableCollection<ColumnTypeInfo> FilteredColumns { get; } = [];

    /// <summary>
    /// 可勾選的連線設定清單（用於多資料庫搜尋）
    /// </summary>
    public ObservableCollection<SelectableProfile> SelectableProfiles { get; } = [];

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
        IConnectionManager connectionManager,
        ITableQueryService tableQueryService,
        IColumnSearchService columnSearchService)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _columnTypeRepository = columnTypeRepository;
        _connectionManager = connectionManager;
        _tableQueryService = tableQueryService;
        _columnSearchService = columnSearchService;
        _instanceId = ++_instanceCount;
        Title = $"欄位搜尋 {_instanceId}";
        Icon = "🔍";
        CanClose = true;

        LoadConnectionProfiles();
    }

    private void LoadConnectionProfiles()
    {
        ConnectionProfiles.Clear();
        SelectableProfiles.Clear();
        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
            SelectableProfiles.Add(new SelectableProfile { Profile = profile });
        }

        // 選擇目前的連線（用於操作連線），並預設勾選
        var currentProfile = _connectionManager?.GetCurrentProfile();
        if (currentProfile != null)
        {
            SelectedProfile = ConnectionProfiles.FirstOrDefault(p => p.Id == currentProfile.Id);
            var selectable = SelectableProfiles.FirstOrDefault(sp => sp.Profile.Id == currentProfile.Id);
            if (selectable != null) selectable.IsSelected = true;
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        // SelectedProfile 用於型態分析和套用說明等寫入操作
        if (value != null && _connectionManager != null)
        {
            _connectionManager.SetCurrentProfile(value.Id);
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
        if (string.IsNullOrWhiteSpace(ColumnSearchText) && string.IsNullOrWhiteSpace(TableSearchText))
            return;

        var selectedProfileIds = SelectableProfiles
            .Where(sp => sp.IsSelected)
            .Select(sp => sp.Profile.Id)
            .ToList();

        if (selectedProfileIds.Count == 0)
        {
            StatusMessage = "請至少勾選一個資料庫連線";
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = "搜尋中...";
            ColumnSearchResults.Clear();
            ColumnGroups.Clear();
            ShowTypeAnalysis = false;

            // 自動設定操作連線為第一個勾選的連線
            var firstSelected = SelectableProfiles.FirstOrDefault(sp => sp.IsSelected);
            if (firstSelected != null)
            {
                SelectedProfile = ConnectionProfiles.FirstOrDefault(p => p.Id == firstSelected.Profile.Id);
            }

            List<ColumnSearchResult> results;
            var searchText = ColumnSearchText.Trim();
            var tableText = string.IsNullOrWhiteSpace(TableSearchText) ? null : TableSearchText.Trim();

            if (selectedProfileIds.Count == 1 && _sqlQueryRepository != null)
            {
                // 單一資料庫：使用原有邏輯
                var profile = SelectableProfiles.First(sp => sp.IsSelected).Profile;
                _connectionManager?.SetCurrentProfile(profile.Id);
                results = await _sqlQueryRepository.SearchColumnsAsync(searchText, IsExactMatch, tableText);
                foreach (var r in results)
                    r.DatabaseName = profile.Database;
            }
            else if (_columnSearchService != null)
            {
                // 多資料庫：使用欄位搜尋服務
                var progress = new Progress<string>(msg => StatusMessage = msg);
                results = await _columnSearchService.SearchColumnsMultiAsync(
                    searchText, selectedProfileIds, IsExactMatch, tableText, progress);
            }
            else
            {
                StatusMessage = "多資料庫搜尋服務未初始化";
                return;
            }

            // 計算同名欄位出現次數最多的資料型別
            ComputePrimaryDataTypes(results);

            foreach (var result in results)
            {
                ColumnSearchResults.Add(result);
            }

            var dbCount = results.Select(r => r.DatabaseName).Distinct().Count();
            var matchMode = IsExactMatch ? "完整比對" : "模糊搜尋";
            StatusMessage = dbCount > 1
                ? $"在 {dbCount} 個資料庫中找到 {ColumnSearchResults.Count} 個符合的欄位/參數（{matchMode}）"
                : $"找到 {ColumnSearchResults.Count} 個符合的欄位/參數（{matchMode}）";
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
        TableSearchText = string.Empty;
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

    [RelayCommand]
    private async Task BatchUpdateLengthAsync()
    {
        if (_columnTypeRepository == null || SelectedGroup == null)
            return;

        if (BatchNewLength <= 0 && BatchNewLength != -1)
        {
            StatusMessage = "請輸入有效的長度（正整數或 -1 表示 MAX）";
            return;
        }

        // 取得所有不一致且可變更長度的欄位
        var columnsToUpdate = SelectedGroup.Columns
            .Where(c => !c.IsConsistent && c.IsLengthChangeable)
            .ToList();

        if (columnsToUpdate.Count == 0)
        {
            StatusMessage = "沒有需要更新的欄位（無不一致且可變更長度的欄位）";
            return;
        }

        try
        {
            IsUpdating = true;
            var successCount = 0;
            var failCount = 0;
            var totalCount = columnsToUpdate.Count;

            for (var i = 0; i < columnsToUpdate.Count; i++)
            {
                var columnInfo = columnsToUpdate[i];
                BatchUpdateProgress = $"({i + 1}/{totalCount})";
                StatusMessage = $"正在更新 [{columnInfo.SchemaName}].[{columnInfo.TableName}].[{columnInfo.ColumnName}]...";

                try
                {
                    // 檢查是否為縮短長度
                    if (BatchNewLength != -1 && BatchNewLength < columnInfo.MaxLength && columnInfo.MaxLength != -1)
                    {
                        var maxDataLength = await _columnTypeRepository.GetMaxDataLengthAsync(
                            columnInfo.SchemaName, columnInfo.TableName, columnInfo.ColumnName);

                        if (maxDataLength > BatchNewLength)
                        {
                            StatusMessage = $"跳過 [{columnInfo.FullTableName}]：資料最大長度 {maxDataLength} 超過目標長度 {BatchNewLength}";
                            failCount++;
                            continue;
                        }
                    }

                    var success = await _columnTypeRepository.UpdateColumnLengthAsync(
                        columnInfo.SchemaName,
                        columnInfo.TableName,
                        columnInfo.ColumnName,
                        BatchNewLength);

                    if (success)
                    {
                        // 更新 UI 中的資料
                        var lengthSpec = BatchNewLength == -1 ? "MAX" : BatchNewLength.ToString();
                        columnInfo.MaxLength = BatchNewLength;
                        columnInfo.DataType = $"{columnInfo.BaseType}({lengthSpec})";
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"更新 [{columnInfo.FullTableName}] 失敗：{ex.Message}";
                    failCount++;
                }
            }

            // 重新整理群組的計算屬性
            SelectedGroup.RefreshCalculatedProperties();
            UpdateFilteredColumns();

            var lengthDisplay = BatchNewLength == -1 ? "MAX" : BatchNewLength.ToString();
            StatusMessage = $"批次更新完成：成功 {successCount} 個，失敗 {failCount} 個（目標長度：{lengthDisplay}）";
            BatchUpdateProgress = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"批次更新錯誤：{ex.Message}";
            BatchUpdateProgress = string.Empty;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>
    /// 準備套用說明到空白欄位（顯示確認對話框）
    /// </summary>
    [RelayCommand]
    private void PrepareApplyDescription()
    {
        if (SelectedSearchResult == null)
        {
            StatusMessage = "請先選擇一筆有說明的資料";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSearchResult.Description))
        {
            StatusMessage = "選中的資料沒有說明，無法套用";
            return;
        }

        // 找出同名欄位中說明為空的項目（TABLE 和 VIEW）
        var emptyItems = ColumnSearchResults
            .Where(r => (r.ObjectType == "TABLE" || r.ObjectType == "VIEW") &&
                       string.Equals(r.ColumnName, SelectedSearchResult.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                       string.IsNullOrWhiteSpace(r.Description) &&
                       r != SelectedSearchResult)
            .ToList();

        if (emptyItems.Count == 0)
        {
            StatusMessage = "沒有需要更新的欄位（所有同名欄位都已有說明）";
            return;
        }

        var tableCount = emptyItems.Count(r => r.ObjectType == "TABLE");
        var viewCount = emptyItems.Count(r => r.ObjectType == "VIEW");

        EmptyDescriptionCount = emptyItems.Count;
        ApplyDescriptionPreview = $"將「{SelectedSearchResult.Description}」套用至 {emptyItems.Count} 個空白說明的欄位" +
                                  $"（資料表 {tableCount} 個、檢視 {viewCount} 個）：\n" +
                                  string.Join("\n", emptyItems.Take(5).Select(r => $"  • [{r.ObjectType}] {r.FullObjectName}.{r.ColumnName}")) +
                                  (emptyItems.Count > 5 ? $"\n  ... 等共 {emptyItems.Count} 個" : "");

        ShowApplyDescriptionConfirm = true;
    }

    /// <summary>
    /// 確認套用說明
    /// </summary>
    [RelayCommand]
    private async Task ConfirmApplyDescriptionAsync()
    {
        if (_tableQueryService == null || SelectedSearchResult == null)
            return;

        ShowApplyDescriptionConfirm = false;

        var description = SelectedSearchResult.Description;
        if (string.IsNullOrWhiteSpace(description))
            return;

        // 找出同名欄位中說明為空的項目（TABLE 和 VIEW）
        var emptyItems = ColumnSearchResults
            .Where(r => (r.ObjectType == "TABLE" || r.ObjectType == "VIEW") &&
                       string.Equals(r.ColumnName, SelectedSearchResult.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                       string.IsNullOrWhiteSpace(r.Description) &&
                       r != SelectedSearchResult)
            .ToList();

        try
        {
            IsUpdating = true;
            var successCount = 0;
            var failCount = 0;

            for (var i = 0; i < emptyItems.Count; i++)
            {
                var item = emptyItems[i];
                StatusMessage = $"更新說明中 ({i + 1}/{emptyItems.Count})：{item.FullObjectName}.{item.ColumnName}";

                try
                {
                    await _tableQueryService.UpdateColumnDescriptionAsync(
                        item.SchemaName,
                        item.ObjectName,
                        item.ColumnName,
                        description,
                        item.ObjectType);

                    // 更新 UI 中的資料
                    item.Description = description;
                    successCount++;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"更新 [{item.ObjectType}] [{item.FullObjectName}].[{item.ColumnName}] 失敗：{ex.Message}";
                    failCount++;
                }
            }

            StatusMessage = $"說明套用完成：成功 {successCount} 個，失敗 {failCount} 個";
        }
        catch (Exception ex)
        {
            StatusMessage = $"套用說明錯誤：{ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>
    /// 取消套用說明
    /// </summary>
    [RelayCommand]
    private void CancelApplyDescription()
    {
        ShowApplyDescriptionConfirm = false;
    }

    /// <summary>
    /// 全選搜尋連線
    /// </summary>
    [RelayCommand]
    private void SelectAllProfiles()
    {
        foreach (var sp in SelectableProfiles)
            sp.IsSelected = true;
    }

    /// <summary>
    /// 取消全選搜尋連線
    /// </summary>
    [RelayCommand]
    private void DeselectAllProfiles()
    {
        foreach (var sp in SelectableProfiles)
            sp.IsSelected = false;
    }

    /// <summary>
    /// 計算同名欄位中出現次數最多的資料型別，並填入 PrimaryDataType
    /// </summary>
    private static void ComputePrimaryDataTypes(List<ColumnSearchResult> results)
    {
        // 依欄位名稱分組（不區分大小寫），計算每個名稱中出現最多的資料型別
        var primaryTypes = results
            .GroupBy(r => r.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.DataType, StringComparer.OrdinalIgnoreCase)
                      .OrderByDescending(tg => tg.Count())
                      .First().Key,
                StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (primaryTypes.TryGetValue(result.ColumnName, out var primaryType))
            {
                result.PrimaryDataType = primaryType;
            }
        }
    }
}
