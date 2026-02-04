using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels.Messages;
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
    private string? _tableDescription;

    private string? _originalTableDescription;

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

    [ObservableProperty]
    private string _columnSearchText = string.Empty;

    public ObservableCollection<ColumnInfo> Columns { get; } = [];
    public ObservableCollection<ColumnInfo> FilteredColumns { get; } = [];
    public ObservableCollection<IndexInfo> Indexes { get; } = [];

    [ObservableProperty]
    private IndexInfo? _selectedIndex;

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

        // 初始化表級說明
        TableDescription = table.Description;
        _originalTableDescription = table.Description;

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

                // 套用篩選（顯示所有欄位或保持現有搜尋條件）
                ApplyColumnFilter();

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
        FilteredColumns.Clear();
        Indexes.Clear();
        Relations.Clear();
        Parameters.Clear();
        Definition = null;
        HasUnsavedChanges = false;
        StatusMessage = string.Empty;
        ColumnSearchText = string.Empty;
        TableDescription = CurrentTable?.Description;
        _originalTableDescription = CurrentTable?.Description;
    }

    /// <summary>
    /// 檢查是否有未儲存的變更
    /// </summary>
    public void CheckForChanges()
    {
        var hasColumnChanges = Columns.Any(c => c.Description != c.OriginalDescription);
        var hasTableDescriptionChange = TableDescription != _originalTableDescription;
        HasUnsavedChanges = hasColumnChanges || hasTableDescriptionChange;

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
    /// 當表級說明變更時觸發
    /// </summary>
    partial void OnTableDescriptionChanged(string? value)
    {
        CheckForChanges();
    }

    /// <summary>
    /// 當搜尋文字變更時觸發
    /// </summary>
    partial void OnColumnSearchTextChanged(string value)
    {
        ApplyColumnFilter();
    }

    /// <summary>
    /// 套用欄位篩選
    /// </summary>
    private void ApplyColumnFilter()
    {
        FilteredColumns.Clear();

        var searchText = ColumnSearchText?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(searchText))
        {
            // 無搜尋文字，顯示全部欄位
            foreach (var col in Columns)
            {
                FilteredColumns.Add(col);
            }
        }
        else
        {
            // 篩選欄位名稱或說明包含搜尋文字的項目（不分大小寫）
            var filtered = Columns.Where(c =>
                (c.ColumnName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));

            foreach (var col in filtered)
            {
                FilteredColumns.Add(col);
            }
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
        var hasTableDescriptionChange = TableDescription != _originalTableDescription;

        if (!changedColumns.Any() && !hasTableDescriptionChange)
        {
            StatusMessage = "沒有需要儲存的變更";
            return;
        }

        // 建立確認訊息
        var messageLines = new List<string>();
        if (hasTableDescriptionChange)
        {
            messageLines.Add($"• 物件說明");
        }
        messageLines.AddRange(changedColumns.Select(c => $"• {c.ColumnName}"));

        var message = $"確定要更新以下 {messageLines.Count} 項說明嗎？\n\n" +
                      string.Join("\n", messageLines);

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

            var savedCount = 0;

            // 儲存表級說明
            if (hasTableDescriptionChange)
            {
                await _tableQueryService.UpdateTableDescriptionAsync(
                    CurrentTable.Type,
                    CurrentTable.Schema,
                    CurrentTable.Name,
                    TableDescription);

                _originalTableDescription = TableDescription;
                savedCount++;

                // 通知物件樹更新說明
                WeakReferenceMessenger.Default.Send(new TableDescriptionUpdatedMessage
                {
                    Type = CurrentTable.Type,
                    Schema = CurrentTable.Schema,
                    Name = CurrentTable.Name,
                    NewDescription = TableDescription
                });
            }

            // 儲存欄位說明
            foreach (var col in changedColumns)
            {
                await _tableQueryService.UpdateColumnDescriptionAsync(
                    col.Schema,
                    col.TableName,
                    col.ColumnName,
                    col.Description);

                // 更新原始值
                col.OriginalDescription = col.Description;
                savedCount++;
            }

            HasUnsavedChanges = false;
            Title = CurrentTable?.Name ?? "資料表";
            StatusMessage = $"已成功更新 {savedCount} 項說明";
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
        // 還原表級說明
        TableDescription = _originalTableDescription;

        // 還原欄位說明
        foreach (var col in Columns)
        {
            col.Description = col.OriginalDescription;
        }

        HasUnsavedChanges = false;
        Title = CurrentTable?.Name ?? "資料表";
        StatusMessage = "已取消變更";
    }

    [RelayCommand]
    private void ClearColumnSearch()
    {
        ColumnSearchText = string.Empty;
    }

    [RelayCommand]
    private async Task DropIndexAsync(IndexInfo? index)
    {
        if (_tableQueryService == null || CurrentTable == null || index == null) return;

        // 主鍵和叢集索引不允許直接刪除
        if (index.IsPrimaryKey)
        {
            StatusMessage = "無法刪除主鍵索引，請先移除主鍵約束";
            return;
        }

        var message = $"確定要刪除索引嗎？\n\n索引名稱：{index.Name}\n類型：{index.Type}\n欄位：{string.Join(", ", index.Columns)}\n\n此操作無法復原！";

        if (ConfirmSaveCallback != null)
        {
            var confirmed = await ConfirmSaveCallback(message);
            if (!confirmed)
            {
                StatusMessage = "已取消刪除";
                return;
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"正在刪除索引 {index.Name}...";

            await _tableQueryService.DropIndexAsync(
                CurrentTable.Schema,
                CurrentTable.Name,
                index.Name);

            StatusMessage = $"索引 {index.Name} 已刪除";

            // 重新載入索引清單
            Indexes.Clear();
            var indexes = await _tableQueryService.GetIndexesAsync(CurrentTable.Schema, CurrentTable.Name);
            foreach (var idx in indexes)
            {
                Indexes.Add(idx);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除索引失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
