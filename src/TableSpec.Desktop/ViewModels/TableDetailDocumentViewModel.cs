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

/// <summary>
/// 資料表/視圖/預存程序詳細資訊文件 ViewModel
/// </summary>
public partial class TableDetailDocumentViewModel : DocumentViewModel
{
    private readonly ITableQueryService? _tableQueryService;

    [ObservableProperty]
    private TableInfo? _currentTable;

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

    public override string DocumentType => "TableDetail";

    public override string DocumentKey => CurrentTable != null
        ? $"{DocumentType}:{CurrentTable.Schema}.{CurrentTable.Name}"
        : base.DocumentKey;

    public TableDetailDocumentViewModel()
    {
        // Design-time constructor
        Title = "資料表";
        Icon = "";
    }

    public TableDetailDocumentViewModel(ITableQueryService tableQueryService, TableInfo table)
    {
        _tableQueryService = tableQueryService;
        CurrentTable = table;
        Title = table.Name;
        Icon = GetIconForType(table.Type);
        CanClose = true;

        // 立即載入資料
        _ = LoadTableAsync();
    }

    private static string GetIconForType(string type) => type switch
    {
        "BASE TABLE" => "",
        "VIEW" => "",
        "PROCEDURE" => "",
        "FUNCTION" => "",
        _ => ""
    };

    public async Task LoadTableAsync()
    {
        if (CurrentTable == null || _tableQueryService == null)
        {
            ClearAll();
            return;
        }

        try
        {
            IsLoading = true;
            ClearAll();

            if (CurrentTable.Type is "PROCEDURE" or "FUNCTION")
            {
                // 載入參數和定義
                var parameters = await _tableQueryService.GetParametersAsync(CurrentTable.Schema, CurrentTable.Name);
                foreach (var param in parameters)
                {
                    Parameters.Add(param);
                }

                Definition = await _tableQueryService.GetDefinitionAsync(CurrentTable.Schema, CurrentTable.Name);
            }
            else
            {
                // 載入欄位
                var columns = await _tableQueryService.GetColumnsAsync(CurrentTable.Type, CurrentTable.Schema, CurrentTable.Name);
                foreach (var col in columns)
                {
                    col.OriginalDescription = col.Description;
                    Columns.Add(col);
                }

                // 載入索引 (僅 Table)
                if (CurrentTable.Type == "BASE TABLE")
                {
                    var indexes = await _tableQueryService.GetIndexesAsync(CurrentTable.Schema, CurrentTable.Name);
                    foreach (var idx in indexes)
                    {
                        Indexes.Add(idx);
                    }

                    // 載入關聯
                    var relations = await _tableQueryService.GetRelationsAsync(CurrentTable.Schema, CurrentTable.Name);
                    foreach (var rel in relations)
                    {
                        Relations.Add(rel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"載入資料表詳細資訊時發生錯誤: {ex.Message}");
            StatusMessage = $"載入失敗: {ex.Message}";
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

    /// <summary>
    /// 檢查是否有未儲存的變更
    /// </summary>
    public void CheckForChanges()
    {
        HasUnsavedChanges = Columns.Any(c => c.Description != c.OriginalDescription);
        if (HasUnsavedChanges)
        {
            Title = $"{CurrentTable?.Name} *";
        }
        else
        {
            Title = CurrentTable?.Name ?? "資料表";
        }
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
            Title = CurrentTable?.Name ?? "資料表";
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
        Title = CurrentTable?.Name ?? "資料表";
        StatusMessage = "已取消變更";
    }
}
