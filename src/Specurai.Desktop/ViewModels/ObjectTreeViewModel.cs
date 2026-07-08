using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels.Messages;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.ViewModels;

public partial class ObjectTreeViewModel : ViewModelBase
{
    private readonly ITableQueryService? _tableQueryService;
    private readonly IConnectionManager? _connectionManager;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TableInfo? _selectedTable;

    [ObservableProperty]
    private ObjectGroupViewModel? _selectedGroup;

    /// <summary>
    /// 伺服器上的資料庫節點（TreeView 根層）
    /// </summary>
    public ObservableCollection<DatabaseNodeViewModel> Databases { get; } = [];

    /// <summary>
    /// 當前資料庫的四個物件群組（掛載於當前資料庫節點下）
    /// </summary>
    public ObservableCollection<ObjectGroupViewModel> Groups { get; } = [];

    public ObjectTreeViewModel()
    {
        // Design-time constructor
        AddDefaultGroups();
        Databases.Add(new DatabaseNodeViewModel("DesignDb", isCurrent: true, groups: Groups));
        Databases.Add(new DatabaseNodeViewModel("OtherDb", isCurrent: false, groups: []));
    }

    public ObjectTreeViewModel(ITableQueryService tableQueryService, IConnectionManager connectionManager)
    {
        _tableQueryService = tableQueryService;
        _connectionManager = connectionManager;
        AddDefaultGroups();
    }

    private void AddDefaultGroups()
    {
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

            // 1. 載入伺服器資料庫清單；列舉失敗（權限不足/離線）時 degrade 為僅顯示當前資料庫
            IReadOnlyList<string> databaseNames;
            try
            {
                databaseNames = _connectionManager != null
                    ? await _connectionManager.GetDatabasesAsync()
                    : Array.Empty<string>();
            }
            catch
            {
                databaseNames = Array.Empty<string>();
            }

            var currentDatabase = _connectionManager?.GetCurrentDatabase();

            // 當前資料庫必須在清單中（列舉失敗或預設庫非使用者資料庫時插入開頭）
            var names = databaseNames.ToList();
            if (currentDatabase != null &&
                !names.Contains(currentDatabase, StringComparer.OrdinalIgnoreCase))
            {
                names.Insert(0, currentDatabase);
            }

            // 2. 重建資料庫節點；僅當前資料庫掛載共用群組並展開（單一展開原則）
            Databases.Clear();
            foreach (var name in names)
            {
                var isCurrent = string.Equals(name, currentDatabase, StringComparison.OrdinalIgnoreCase);
                var node = new DatabaseNodeViewModel(name, isCurrent, isCurrent ? Groups : []);
                node.PropertyChanged += (s, e) =>
                {
                    // 使用者以展開箭頭展開非當前資料庫時，等同點選切換
                    if (e.PropertyName == nameof(DatabaseNodeViewModel.IsExpanded) &&
                        s is DatabaseNodeViewModel n && n.IsExpanded && !n.IsCurrent)
                    {
                        SelectDatabase(n);
                    }
                };
                Databases.Add(node);
            }

            // 3. 載入當前資料庫的物件
            var allObjects = await _tableQueryService.GetAllTablesAsync();

            foreach (var group in Groups)
            {
                group.Items.Clear();
                var items = allObjects.Where(t => t.Type == group.ObjectType)
                    .OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
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
                    item.Table.Schema.ToLowerInvariant().Contains(searchLower) ||
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

    [RelayCommand]
    private void SelectDatabase(DatabaseNodeViewModel? node)
    {
        if (node == null || node.IsCurrent || _connectionManager == null)
            return;

        // 切換全域當前資料庫；後續載入由 CurrentDatabaseChanged 訂閱端（MainWindowViewModel）驅動
        _connectionManager.SetCurrentDatabase(node.Name);
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

public partial class ObjectItemViewModel : ViewModelBase, IRecipient<TableDescriptionUpdatedMessage>
{
    public TableInfo Table { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName = string.Empty;

    public ObjectItemViewModel(TableInfo table)
    {
        Table = table;
        UpdateDisplayName();

        // 註冊接收訊息
        WeakReferenceMessenger.Default.Register(this);
    }

    private void UpdateDisplayName()
    {
        var schemaPrefix = $"{Table.Schema}.";
        DisplayName = !string.IsNullOrEmpty(Table.Description)
            ? $"{schemaPrefix}{Table.Name} ({Table.Description})"
            : $"{schemaPrefix}{Table.Name}";
    }

    public void Receive(TableDescriptionUpdatedMessage message)
    {
        // 檢查是否為同一個物件
        if (Table.Type == message.Type &&
            Table.Schema == message.Schema &&
            Table.Name == message.Name)
        {
            Table.Description = message.NewDescription;
            UpdateDisplayName();
        }
    }
}

/// <summary>
/// 資料庫節點 ViewModel（TreeView 根層，SSMS 式資料庫瀏覽）
/// </summary>
public partial class DatabaseNodeViewModel : ViewModelBase
{
    public string Name { get; }

    /// <summary>
    /// 是否為目前使用中的資料庫（節點於每次重建時決定，不需通知變更）
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>
    /// 節點名稱字重（當前資料庫以粗體標示）
    /// </summary>
    public string NameFontWeight => IsCurrent ? "Bold" : "Normal";

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<ObjectGroupViewModel> Groups { get; }

    public DatabaseNodeViewModel(string name, bool isCurrent, ObservableCollection<ObjectGroupViewModel> groups)
    {
        Name = name;
        IsCurrent = isCurrent;
        _isExpanded = isCurrent;
        Groups = groups;
    }
}
