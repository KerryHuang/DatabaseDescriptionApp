using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// TableDetailDocumentViewModel 測試
/// </summary>
public class TableDetailDocumentViewModelTests
{
    private readonly ITableQueryService _tableQueryService;

    public TableDetailDocumentViewModelTests()
    {
        _tableQueryService = Substitute.For<ITableQueryService>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new TableDetailDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("資料表");
    }

    [Fact]
    public void Constructor_有TableInfo_應設定Title和Icon()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();

        // Act
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        // Assert
        vm.Title.Should().Be("Users");
        vm.CurrentTable.Should().Be(table);
    }

    #endregion

    #region DocumentType 和 DocumentKey 測試

    [Fact]
    public void DocumentType_應為TableDetail()
    {
        // Act
        var vm = new TableDetailDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("TableDetail");
    }

    [Fact]
    public void DocumentKey_有Table_應包含Schema和Name()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();

        // Act
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        // Assert
        vm.DocumentKey.Should().Be("TableDetail:dbo.Users");
    }

    #endregion

    #region 屬性初始值測試

    [Fact]
    public void 初始狀態_IsLoading應為False()
    {
        // Act
        var vm = new TableDetailDocumentViewModel();

        // Assert
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_HasUnsavedChanges應為False()
    {
        // Act
        var vm = new TableDetailDocumentViewModel();

        // Assert
        vm.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_集合應為空()
    {
        // Act
        var vm = new TableDetailDocumentViewModel();

        // Assert
        vm.Columns.Should().BeEmpty();
        vm.Indexes.Should().BeEmpty();
        vm.Relations.Should().BeEmpty();
        vm.Parameters.Should().BeEmpty();
    }

    #endregion

    #region LoadTableAsync 測試

    [Fact]
    public async Task LoadTableAsync_Table類型_應載入欄位索引關聯()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        var columns = new List<ColumnInfo>
        {
            new() { Schema = "dbo", TableName = "Users", ColumnName = "Id", DataType = "int" }
        };
        var indexes = new List<IndexInfo>
        {
            new() { Name = "PK_Users", Type = "Clustered", Columns = new[] { "Id" } }
        };
        var relations = new List<RelationInfo>
        {
            new() { ConstraintName = "FK_Test", FromTable = "Users", FromColumn = "Id", ToTable = "Orders", ToColumn = "UserId" }
        };

        _tableQueryService.GetColumnsAsync("BASE TABLE", "dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(columns);
        _tableQueryService.GetIndexesAsync("dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(indexes);
        _tableQueryService.GetRelationsAsync("dbo", "Users", Arg.Any<CancellationToken>())
            .Returns(relations);

        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        // Act - 等待建構函式中的非同步載入完成
        await Task.Delay(100);

        // Assert
        vm.Columns.Should().HaveCount(1);
        vm.Indexes.Should().HaveCount(1);
        vm.Relations.Should().HaveCount(1);
        vm.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadTableAsync_Procedure類型_應載入參數和定義()
    {
        // Arrange
        var table = new TableInfo { Type = "PROCEDURE", Schema = "dbo", Name = "sp_GetUsers" };
        var parameters = new List<ParameterInfo>
        {
            new() { Name = "@Id", DataType = "int", Ordinal = 1 }
        };
        var definition = "CREATE PROCEDURE sp_GetUsers AS SELECT * FROM Users";

        _tableQueryService.GetParametersAsync("dbo", "sp_GetUsers", Arg.Any<CancellationToken>())
            .Returns(parameters);
        _tableQueryService.GetDefinitionAsync("dbo", "sp_GetUsers", Arg.Any<CancellationToken>())
            .Returns(definition);

        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        // Act - 等待非同步載入完成
        await Task.Delay(100);

        // Assert
        vm.Parameters.Should().HaveCount(1);
        vm.Definition.Should().Be(definition);
        vm.Columns.Should().BeEmpty();
    }

    #endregion

    #region CheckForChanges 測試

    [Fact]
    public void CheckForChanges_無變更_HasUnsavedChanges應為False()
    {
        // Arrange
        var vm = new TableDetailDocumentViewModel();
        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "T",
            ColumnName = "C",
            DataType = "int",
            Description = "原始說明",
            OriginalDescription = "原始說明"
        });

        // Act
        vm.CheckForChanges();

        // Assert
        vm.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void CheckForChanges_有變更_HasUnsavedChanges應為True()
    {
        // Arrange
        var vm = new TableDetailDocumentViewModel();
        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "T",
            ColumnName = "C",
            DataType = "int",
            Description = "新說明",
            OriginalDescription = "原始說明"
        });

        // Act
        vm.CheckForChanges();

        // Assert
        vm.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public void CheckForChanges_有變更_Title應加星號()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Name",
            DataType = "nvarchar",
            Description = "新說明",
            OriginalDescription = "原始說明"
        });

        // Act
        vm.CheckForChanges();

        // Assert
        vm.Title.Should().EndWith("*");
    }

    #endregion

    #region GetChangedColumns 測試

    [Fact]
    public void GetChangedColumns_應只回傳變更的欄位()
    {
        // Arrange
        var vm = new TableDetailDocumentViewModel();
        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "T",
            ColumnName = "Changed",
            DataType = "int",
            Description = "新",
            OriginalDescription = "舊"
        });
        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "T",
            ColumnName = "Unchanged",
            DataType = "int",
            Description = "相同",
            OriginalDescription = "相同"
        });

        // Act
        var changed = vm.GetChangedColumns().ToList();

        // Assert
        changed.Should().HaveCount(1);
        changed.First().ColumnName.Should().Be("Changed");
    }

    #endregion

    #region CancelChangesCommand 測試

    [Fact]
    public void CancelChangesCommand_應還原所有變更()
    {
        // Arrange
        var vm = new TableDetailDocumentViewModel();
        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "T",
            ColumnName = "C",
            DataType = "int",
            Description = "已修改",
            OriginalDescription = "原始"
        };
        vm.Columns.Add(column);

        // Act
        vm.CancelChangesCommand.Execute(null);

        // Assert
        column.Description.Should().Be("原始");
        vm.HasUnsavedChanges.Should().BeFalse();
    }

    #endregion

    #region SaveColumnDescriptionsCommand 測試

    [Fact]
    public async Task SaveColumnDescriptionsCommand_無變更_應顯示訊息()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);

        // Act
        await vm.SaveColumnDescriptionsCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Contain("沒有");
    }

    [Fact]
    public async Task SaveColumnDescriptionsCommand_有變更且確認_應儲存()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);
        vm.ConfirmSaveCallback = _ => Task.FromResult(true);

        var column = new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Name",
            DataType = "nvarchar",
            Description = "新說明",
            OriginalDescription = "舊說明"
        };
        vm.Columns.Add(column);

        // Act
        await vm.SaveColumnDescriptionsCommand.ExecuteAsync(null);

        // Assert
        await _tableQueryService.Received(1).UpdateColumnDescriptionAsync(
            "dbo", "Users", "Name", "新說明", "TABLE", Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("成功");
    }

    [Fact]
    public async Task SaveColumnDescriptionsCommand_取消確認_應不儲存()
    {
        // Arrange
        var table = new TableInfo { Type = "BASE TABLE", Schema = "dbo", Name = "Users" };
        SetupEmptyMocks();
        var vm = new TableDetailDocumentViewModel(_tableQueryService, table);
        vm.ConfirmSaveCallback = _ => Task.FromResult(false);

        vm.Columns.Add(new ColumnInfo
        {
            Schema = "dbo",
            TableName = "Users",
            ColumnName = "Name",
            DataType = "nvarchar",
            Description = "新",
            OriginalDescription = "舊"
        });

        // Act
        await vm.SaveColumnDescriptionsCommand.ExecuteAsync(null);

        // Assert
        await _tableQueryService.DidNotReceive().UpdateColumnDescriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("取消");
    }

    #endregion

    #region Helper Methods

    private void SetupEmptyMocks()
    {
        _tableQueryService.GetColumnsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ColumnInfo>());
        _tableQueryService.GetIndexesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<IndexInfo>());
        _tableQueryService.GetRelationsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<RelationInfo>());
        _tableQueryService.GetParametersAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ParameterInfo>());
        _tableQueryService.GetDefinitionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
    }

    #endregion
}
