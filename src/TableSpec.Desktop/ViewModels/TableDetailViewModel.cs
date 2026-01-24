using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

public partial class TableDetailViewModel : ViewModelBase
{
    private readonly ITableQueryService? _tableQueryService;

    [ObservableProperty]
    private TableInfo? _currentTable;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string? _definition;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<ColumnInfo> Columns { get; } = [];
    public ObservableCollection<IndexInfo> Indexes { get; } = [];
    public ObservableCollection<RelationInfo> Relations { get; } = [];
    public ObservableCollection<ParameterInfo> Parameters { get; } = [];

    /// <summary>
    /// 確認儲存的回調函數（由 View 設定）
    /// </summary>
    public Func<string, Task<bool>>? ConfirmSaveCallback { get; set; }

    public TableDetailViewModel()
    {
        // Design-time constructor
    }

    public TableDetailViewModel(ITableQueryService tableQueryService)
    {
        _tableQueryService = tableQueryService;
    }

    public async Task LoadTableAsync(TableInfo? table)
    {
        if (table == null || _tableQueryService == null)
        {
            CurrentTable = null;
            ClearAll();
            return;
        }

        try
        {
            IsLoading = true;
            CurrentTable = table;
            ClearAll();

            if (table.Type is "PROCEDURE" or "FUNCTION")
            {
                // 載入參數和定義
                var parameters = await _tableQueryService.GetParametersAsync(table.Schema, table.Name);
                foreach (var param in parameters)
                {
                    Parameters.Add(param);
                }

                Definition = await _tableQueryService.GetDefinitionAsync(table.Schema, table.Name);
            }
            else
            {
                // 載入欄位
                var columns = await _tableQueryService.GetColumnsAsync(table.Type, table.Schema, table.Name);
                foreach (var col in columns)
                {
                    col.OriginalDescription = col.Description;
                    Columns.Add(col);
                }

                // 載入索引 (僅 Table)
                if (table.Type == "BASE TABLE")
                {
                    var indexes = await _tableQueryService.GetIndexesAsync(table.Schema, table.Name);
                    foreach (var idx in indexes)
                    {
                        Indexes.Add(idx);
                    }

                    // 載入關聯
                    var relations = await _tableQueryService.GetRelationsAsync(table.Schema, table.Name);
                    foreach (var rel in relations)
                    {
                        Relations.Add(rel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading table details: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearAll()
    {
        Columns.Clear();
        Indexes.Clear();
        Relations.Clear();
        Parameters.Clear();
        Definition = null;
        HasUnsavedChanges = false;
        StatusMessage = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        // TODO: 實作欄位搜尋過濾
    }

    /// <summary>
    /// 檢查是否有未儲存的變更
    /// </summary>
    public void CheckForChanges()
    {
        HasUnsavedChanges = Columns.Any(c => c.Description != c.OriginalDescription);
    }

    /// <summary>
    /// 取得變更的欄位清單
    /// </summary>
    public IEnumerable<ColumnInfo> GetChangedColumns()
    {
        return Columns.Where(c => c.Description != c.OriginalDescription);
    }

    [RelayCommand]
    private async Task SaveColumnDescriptionsAsync()
    {
        if (_tableQueryService == null || CurrentTable == null)
            return;

        var changedColumns = GetChangedColumns().ToList();
        if (!changedColumns.Any())
        {
            StatusMessage = "沒有需要儲存的變更";
            return;
        }

        // 確認是否儲存
        var message = $"確定要更新以下 {changedColumns.Count} 個欄位的說明嗎？\n\n" +
                      string.Join("\n", changedColumns.Select(c => $"• {c.ColumnName}"));

        if (ConfirmSaveCallback != null)
        {
            var confirmed = await ConfirmSaveCallback(message);
            if (!confirmed)
            {
                StatusMessage = "已取消儲存";
                return;
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在儲存...";

            foreach (var col in changedColumns)
            {
                await _tableQueryService.UpdateColumnDescriptionAsync(
                    col.Schema,
                    col.TableName,
                    col.ColumnName,
                    col.Description);

                // 更新原始值
                col.OriginalDescription = col.Description;
            }

            HasUnsavedChanges = false;
            StatusMessage = $"已成功更新 {changedColumns.Count} 個欄位的說明";
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelChanges()
    {
        foreach (var col in Columns)
        {
            col.Description = col.OriginalDescription;
        }
        HasUnsavedChanges = false;
        StatusMessage = "已取消變更";
    }
}
