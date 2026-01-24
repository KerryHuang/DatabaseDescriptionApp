using System;
using System.Collections.ObjectModel;
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

    public ObservableCollection<ColumnInfo> Columns { get; } = [];
    public ObservableCollection<IndexInfo> Indexes { get; } = [];
    public ObservableCollection<RelationInfo> Relations { get; } = [];
    public ObservableCollection<ParameterInfo> Parameters { get; } = [];

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
    }

    partial void OnSearchTextChanged(string value)
    {
        // TODO: 實作欄位搜尋過濾
    }
}
