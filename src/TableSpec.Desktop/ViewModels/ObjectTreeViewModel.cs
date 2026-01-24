using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.ViewModels;

public partial class ObjectTreeViewModel : ViewModelBase
{
    private readonly ITableQueryService? _tableQueryService;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TableInfo? _selectedTable;

    [ObservableProperty]
    private ObjectGroupViewModel? _selectedGroup;

    public ObservableCollection<ObjectGroupViewModel> Groups { get; } = [];

    public ObjectTreeViewModel()
    {
        // Design-time constructor
        Groups.Add(new ObjectGroupViewModel("Tables", "BASE TABLE"));
        Groups.Add(new ObjectGroupViewModel("Views", "VIEW"));
        Groups.Add(new ObjectGroupViewModel("Stored Procedures", "PROCEDURE"));
        Groups.Add(new ObjectGroupViewModel("Functions", "FUNCTION"));
    }

    public ObjectTreeViewModel(ITableQueryService tableQueryService)
    {
        _tableQueryService = tableQueryService;

        Groups.Add(new ObjectGroupViewModel("Tables", "BASE TABLE"));
        Groups.Add(new ObjectGroupViewModel("Views", "VIEW"));
        Groups.Add(new ObjectGroupViewModel("Stored Procedures", "PROCEDURE"));
        Groups.Add(new ObjectGroupViewModel("Functions", "FUNCTION"));
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterObjects();
    }

    public string? LastError { get; private set; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_tableQueryService == null)
        {
            LastError = "TableQueryService 未初始化";
            return;
        }

        try
        {
            IsLoading = true;
            LastError = null;

            var allObjects = await _tableQueryService.GetAllTablesAsync();

            System.Diagnostics.Debug.WriteLine($"Loaded {allObjects.Count} objects");

            foreach (var group in Groups)
            {
                group.Items.Clear();
                var items = allObjects.Where(t => t.Type == group.ObjectType).ToList();
                System.Diagnostics.Debug.WriteLine($"Group {group.Name} ({group.ObjectType}): {items.Count} items");
                foreach (var item in items)
                {
                    group.Items.Add(new ObjectItemViewModel(item));
                }
                group.UpdateCount();
            }

            FilterObjects();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Error loading objects: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterObjects()
    {
        var searchLower = SearchText.ToLowerInvariant();

        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.IsVisible = string.IsNullOrEmpty(searchLower) ||
                    item.Table.Name.ToLowerInvariant().Contains(searchLower) ||
                    (item.Table.Description?.ToLowerInvariant().Contains(searchLower) ?? false);
            }
            group.UpdateVisibleCount();
        }
    }

    [RelayCommand]
    private void SelectObject(ObjectItemViewModel? item)
    {
        if (item != null)
        {
            SelectedTable = item.Table;
        }
    }
}

public partial class ObjectGroupViewModel : ViewModelBase
{
    public string Name { get; }
    public string ObjectType { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private int _visibleCount;

    public ObservableCollection<ObjectItemViewModel> Items { get; } = [];

    public ObjectGroupViewModel(string name, string objectType)
    {
        Name = name;
        ObjectType = objectType;
    }

    public void UpdateCount()
    {
        Count = Items.Count;
        VisibleCount = Items.Count;
    }

    public void UpdateVisibleCount()
    {
        VisibleCount = Items.Count(i => i.IsVisible);
    }
}

public partial class ObjectItemViewModel : ViewModelBase
{
    public TableInfo Table { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayName => !string.IsNullOrEmpty(Table.Description)
        ? $"{Table.Name} ({Table.Description})"
        : Table.Name;

    public ObjectItemViewModel(TableInfo table)
    {
        Table = table;
    }
}
