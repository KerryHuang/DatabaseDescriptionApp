using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// ObjectTreeViewModel 測試
/// </summary>
public class ObjectTreeViewModelTests
{
    private readonly ITableQueryService _tableQueryService;

    public ObjectTreeViewModelTests()
    {
        _tableQueryService = Substitute.For<ITableQueryService>();
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
    public void Constructor_有TableQueryService_應建立四個群組()
    {
        // Act
        var vm = new ObjectTreeViewModel(_tableQueryService);

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

        var vm = new ObjectTreeViewModel(_tableQueryService);

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

        var vm = new ObjectTreeViewModel(_tableQueryService);
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

        var vm = new ObjectTreeViewModel(_tableQueryService);

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

        var vm = new ObjectTreeViewModel(_tableQueryService);
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

        var vm = new ObjectTreeViewModel(_tableQueryService);
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

        var vm = new ObjectTreeViewModel(_tableQueryService);
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

        var vm = new ObjectTreeViewModel(_tableQueryService);
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
        item.DisplayName.Should().Be("Users");
    }

    [Fact]
    public void DisplayName_有Description_應顯示Name和Description()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users", Description = "使用者資料表" };

        // Act
        var item = new ObjectItemViewModel(table);

        // Assert
        item.DisplayName.Should().Be("Users (使用者資料表)");
    }
}
