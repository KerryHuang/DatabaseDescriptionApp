using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// ObjectTreeViewModel 測試
/// </summary>
public class ObjectTreeViewModelTests
{
    private readonly ITableQueryService _tableQueryService;
    private readonly IConnectionManager _connectionManager;

    public ObjectTreeViewModelTests()
    {
        _tableQueryService = Substitute.For<ITableQueryService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "TestDb" });
        _connectionManager.GetCurrentDatabase().Returns("TestDb");
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應建立四個群組()
    {
        // Act
        var vm = new ObjectTreeViewModel();

        // Assert
        vm.Groups.Should().HaveCount(4);
        vm.Groups.Select(g => g.Name).Should().Contain(new[] { "Tables", "Views", "Stored Procedures", "Functions" });
    }

    [Fact]
    public void Constructor_有服務參數_應建立四個群組()
    {
        // Act
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Assert
        vm.Groups.Should().HaveCount(4);
    }

    [Fact]
    public void Constructor_群組ObjectType應正確設定()
    {
        // Act
        var vm = new ObjectTreeViewModel();

        // Assert
        vm.Groups.First(g => g.Name == "Tables").ObjectType.Should().Be("BASE TABLE");
        vm.Groups.First(g => g.Name == "Views").ObjectType.Should().Be("VIEW");
        vm.Groups.First(g => g.Name == "Stored Procedures").ObjectType.Should().Be("PROCEDURE");
        vm.Groups.First(g => g.Name == "Functions").ObjectType.Should().Be("FUNCTION");
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_SearchText應為空()
    {
        // Act
        var vm = new ObjectTreeViewModel();

        // Assert
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_IsLoading應為False()
    {
        // Act
        var vm = new ObjectTreeViewModel();

        // Assert
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_SelectedTable應為Null()
    {
        // Act
        var vm = new ObjectTreeViewModel();

        // Assert
        vm.SelectedTable.Should().BeNull();
    }

    #endregion

    #region RefreshCommand 測試

    [Fact]
    public async Task RefreshCommand_應載入物件到對應群組()
    {
        // Arrange
        var tables = new List<TableInfo>
        {
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" },
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Orders" },
            new() { Type = "VIEW", Schema = "dbo", Name = "vw_ActiveUsers" },
            new() { Type = "PROCEDURE", Schema = "dbo", Name = "sp_GetUsers" },
            new() { Type = "FUNCTION", Schema = "dbo", Name = "fn_Calculate" }
        };
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.Groups.First(g => g.Name == "Tables").Items.Should().HaveCount(2);
        vm.Groups.First(g => g.Name == "Views").Items.Should().HaveCount(1);
        vm.Groups.First(g => g.Name == "Stored Procedures").Items.Should().HaveCount(1);
        vm.Groups.First(g => g.Name == "Functions").Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshCommand_載入時_IsLoading應為True()
    {
        // Arrange
        var loadingStates = new List<bool>();
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(10);
                return (IReadOnlyList<TableInfo>)new List<TableInfo>();
            });

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.IsLoading))
                loadingStates.Add(vm.IsLoading);
        };

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        loadingStates.Should().Contain(true);
        vm.IsLoading.Should().BeFalse(); // 完成後應為 false
    }

    [Fact]
    public async Task RefreshCommand_發生錯誤_LastError應有值()
    {
        // Arrange
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("測試錯誤"));

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.LastError.Should().Be("測試錯誤");
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshCommand_無TableQueryService_LastError應有提示()
    {
        // Arrange
        var vm = new ObjectTreeViewModel();

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.LastError.Should().Contain("未初始化");
    }

    [Fact]
    public async Task RefreshCommand_應載入資料庫節點清單()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2", "Db3" });
        _connectionManager.GetCurrentDatabase().Returns("Db2");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.Databases.Should().HaveCount(3);
        vm.Databases.Select(d => d.Name).Should().ContainInOrder("Db1", "Db2", "Db3");
    }

    [Fact]
    public async Task RefreshCommand_當前資料庫節點_應標示為Current且展開並掛載群組()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2" });
        _connectionManager.GetCurrentDatabase().Returns("Db2");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>
            {
                new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" }
            });

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        var current = vm.Databases.First(d => d.Name == "Db2");
        current.IsCurrent.Should().BeTrue();
        current.IsExpanded.Should().BeTrue();
        current.Groups.Should().HaveCount(4);
        current.Groups.First(g => g.Name == "Tables").Items.Should().HaveCount(1);

        var other = vm.Databases.First(d => d.Name == "Db1");
        other.IsCurrent.Should().BeFalse();
        other.IsExpanded.Should().BeFalse();
        other.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshCommand_當前資料庫不在清單中_應插入清單開頭()
    {
        // Arrange：連線設定檔預設資料庫可能不是使用者資料庫（例如 master）
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1" });
        _connectionManager.GetCurrentDatabase().Returns("master");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.Databases.Select(d => d.Name).Should().ContainInOrder("master", "Db1");
        vm.Databases.First(d => d.Name == "master").IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshCommand_資料庫列舉失敗_應degrade為僅顯示當前資料庫且物件照常載入()
    {
        // Arrange
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("權限不足"));
        _connectionManager.GetCurrentDatabase().Returns("DefaultDb");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>
            {
                new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" }
            });

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        vm.LastError.Should().BeNull();
        vm.Databases.Should().ContainSingle(d => d.Name == "DefaultDb" && d.IsCurrent);
        vm.Groups.First(g => g.Name == "Tables").Items.Should().HaveCount(1);
    }

    #endregion

    #region SearchText 過濾測試

    [Fact]
    public async Task SearchText_變更時_應過濾物件()
    {
        // Arrange
        var tables = new List<TableInfo>
        {
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" },
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Orders" },
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Products" }
        };
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);

        // Act
        vm.SearchText = "user";

        // Assert
        var tablesGroup = vm.Groups.First(g => g.Name == "Tables");
        tablesGroup.Items.Where(i => i.IsVisible).Should().HaveCount(1);
        tablesGroup.Items.First(i => i.IsVisible).Table.Name.Should().Be("Users");
    }

    [Fact]
    public async Task SearchText_清空時_應顯示全部物件()
    {
        // Arrange
        var tables = new List<TableInfo>
        {
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" },
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Orders" }
        };
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SearchText = "user";

        // Act
        vm.SearchText = "";

        // Assert
        var tablesGroup = vm.Groups.First(g => g.Name == "Tables");
        tablesGroup.Items.All(i => i.IsVisible).Should().BeTrue();
    }

    [Fact]
    public async Task SearchText_應搜尋Description()
    {
        // Arrange
        var tables = new List<TableInfo>
        {
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "TBL001", Description = "使用者資料表" },
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "TBL002", Description = "訂單資料表" }
        };
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);

        // Act
        vm.SearchText = "使用者";

        // Assert
        var tablesGroup = vm.Groups.First(g => g.Name == "Tables");
        tablesGroup.Items.Where(i => i.IsVisible).Should().HaveCount(1);
        tablesGroup.Items.First(i => i.IsVisible).Table.Name.Should().Be("TBL001");
    }

    [Fact]
    public async Task SearchText_應不區分大小寫()
    {
        // Arrange
        var tables = new List<TableInfo>
        {
            new() { Type = "BASE TABLE", Schema = "dbo", Name = "Users" }
        };
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);

        // Act
        vm.SearchText = "USERS";

        // Assert
        var tablesGroup = vm.Groups.First(g => g.Name == "Tables");
        tablesGroup.Items.First().IsVisible.Should().BeTrue();
    }

    #endregion

    #region SelectObjectCommand 測試

    [Fact]
    public void SelectObjectCommand_選擇物件_SelectedTable應更新()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        var item = new ObjectItemViewModel(table);
        var vm = new ObjectTreeViewModel();

        // Act
        vm.SelectObjectCommand.Execute(item);

        // Assert
        vm.SelectedTable.Should().Be(table);
    }

    [Fact]
    public void SelectObjectCommand_傳入Null_SelectedTable應不變()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        var vm = new ObjectTreeViewModel();
        vm.SelectedTable = table;

        // Act
        vm.SelectObjectCommand.Execute(null);

        // Assert
        vm.SelectedTable.Should().Be(table);
    }

    #endregion
}

/// <summary>
/// ObjectGroupViewModel 測試
/// </summary>
public class ObjectGroupViewModelTests
{
    [Fact]
    public void Constructor_應設定Name和ObjectType()
    {
        // Act
        var group = new ObjectGroupViewModel("Tables", "BASE TABLE");

        // Assert
        group.Name.Should().Be("Tables");
        group.ObjectType.Should().Be("BASE TABLE");
    }

    [Fact]
    public void 初始狀態_IsExpanded應為True()
    {
        // Act
        var group = new ObjectGroupViewModel("Test", "TEST");

        // Assert
        group.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void UpdateCount_應更新Count和VisibleCount()
    {
        // Arrange
        var group = new ObjectGroupViewModel("Tables", "BASE TABLE");
        group.Items.Add(new ObjectItemViewModel(new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "T1" }));
        group.Items.Add(new ObjectItemViewModel(new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "T2" }));

        // Act
        group.UpdateCount();

        // Assert
        group.Count.Should().Be(2);
        group.VisibleCount.Should().Be(2);
    }

    [Fact]
    public void UpdateVisibleCount_應只計算可見項目()
    {
        // Arrange
        var group = new ObjectGroupViewModel("Tables", "BASE TABLE");
        var item1 = new ObjectItemViewModel(new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "T1" }) { IsVisible = true };
        var item2 = new ObjectItemViewModel(new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "T2" }) { IsVisible = false };
        group.Items.Add(item1);
        group.Items.Add(item2);

        // Act
        group.UpdateVisibleCount();

        // Assert
        group.VisibleCount.Should().Be(1);
    }
}

/// <summary>
/// ObjectItemViewModel 測試
/// </summary>
public class ObjectItemViewModelTests
{
    [Fact]
    public void Constructor_應設定Table()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };

        // Act
        var item = new ObjectItemViewModel(table);

        // Assert
        item.Table.Should().Be(table);
    }

    [Fact]
    public void 初始狀態_IsVisible應為True()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };

        // Act
        var item = new ObjectItemViewModel(table);

        // Assert
        item.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void DisplayName_無Description_應只顯示Name()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };

        // Act
        var item = new ObjectItemViewModel(table);

        // Assert
        item.DisplayName.Should().Be("dbo.Users");
    }

    [Fact]
    public void DisplayName_有Description_應顯示Name和Description()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users", Description = "使用者資料表" };

        // Act
        var item = new ObjectItemViewModel(table);

        // Assert
        item.DisplayName.Should().Be("dbo.Users (使用者資料表)");
    }
}

/// <summary>
/// SelectDatabaseCommand 與 DatabaseNodeViewModel 測試
/// </summary>
public class ObjectTreeDatabaseSelectionTests
{
    private readonly ITableQueryService _tableQueryService = Substitute.For<ITableQueryService>();
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();

    public ObjectTreeDatabaseSelectionTests()
    {
        _connectionManager.GetDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Db1", "Db2" });
        _connectionManager.GetCurrentDatabase().Returns("Db1");
        _tableQueryService.GetAllTablesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableInfo>());
    }

    [Fact]
    public async Task SelectDatabaseCommand_點選非當前資料庫_應呼叫SetCurrentDatabase()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var other = vm.Databases.First(d => d.Name == "Db2");

        // Act
        vm.SelectDatabaseCommand.Execute(other);

        // Assert
        _connectionManager.Received(1).SetCurrentDatabase("Db2");
    }

    [Fact]
    public async Task SelectDatabaseCommand_點選當前資料庫_不應呼叫SetCurrentDatabase()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var current = vm.Databases.First(d => d.Name == "Db1");

        // Act
        vm.SelectDatabaseCommand.Execute(current);

        // Assert
        _connectionManager.DidNotReceive().SetCurrentDatabase(Arg.Any<string?>());
    }

    [Fact]
    public async Task 展開非當前資料庫節點_應觸發切換()
    {
        // Arrange
        var vm = new ObjectTreeViewModel(_tableQueryService, _connectionManager);
        await vm.RefreshCommand.ExecuteAsync(null);
        var other = vm.Databases.First(d => d.Name == "Db2");

        // Act：模擬使用者點選 TreeView 節點的展開箭頭
        other.IsExpanded = true;

        // Assert
        _connectionManager.Received(1).SetCurrentDatabase("Db2");
    }

    [Fact]
    public void DatabaseNodeViewModel_當前節點_字重應為Bold()
    {
        var node = new DatabaseNodeViewModel("Db1", isCurrent: true, groups: []);

        node.NameFontWeight.Should().Be("Bold");
        node.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void DatabaseNodeViewModel_非當前節點_字重應為Normal()
    {
        var node = new DatabaseNodeViewModel("Db1", isCurrent: false, groups: []);

        node.NameFontWeight.Should().Be("Normal");
        node.IsExpanded.Should().BeFalse();
    }
}
