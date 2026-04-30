using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.Tests.ViewModels;

/// <summary>
/// SchemaMigrationDocumentViewModel 單元測試
/// </summary>
public class SchemaMigrationDocumentViewModelTests
{
    private readonly ISchemaMigrationService _migrationService;
    private readonly ISqlScriptGenerator _scriptGenerator;
    private readonly ISchemaMigrationExecutor _executor;
    private readonly IConnectionManager _connectionManager;

    public SchemaMigrationDocumentViewModelTests()
    {
        _migrationService = Substitute.For<ISchemaMigrationService>();
        _scriptGenerator = Substitute.For<ISqlScriptGenerator>();
        _executor = Substitute.For<ISchemaMigrationExecutor>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new SchemaMigrationDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("Schema Migration");
        vm.DocumentType.Should().Be("SchemaMigration");
    }

    [Fact]
    public void Constructor_有服務注入_初始狀態應正確()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "正式環境", Server = "prod-server", Database = "ProdDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);

        // Act
        var vm = new SchemaMigrationDocumentViewModel(
            _migrationService, _scriptGenerator, _executor, _connectionManager);

        // Assert
        vm.ConnectionProfiles.Should().HaveCount(2);
        vm.DifferenceRows.Should().BeEmpty();
        vm.IsAnalyzing.Should().BeFalse();
        vm.IsExecuting.Should().BeFalse();
    }

    [Fact]
    public void ExecuteMigrationCommand_無選取差異_應無法執行()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();

        // Act & Assert
        vm.ExecuteMigrationCommand.CanExecute(null).Should().BeFalse();
    }

    #region 資料表名稱與欄位名稱篩選測試

    [Fact]
    public void FilterTableName_設定關鍵字_FilteredRows只顯示ObjectName包含該關鍵字的列()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Orders",    RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Customers", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        // Act
        vm.FilterTableName = "Orders";

        // Assert
        vm.FilteredRows.Should().ContainSingle()
            .Which.Difference.ObjectName.Should().Be("dbo.Orders");
    }

    [Fact]
    public void FilterColumnName_設定關鍵字_欄位列篩選且非欄位列維持顯示()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Table,  ObjectName = "dbo.Orders",                RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[CustomerName]", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[OrderDate]",    RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        // Act
        vm.FilterColumnName = "Customer";

        // Assert
        vm.FilteredRows.Should().HaveCount(2);
        vm.FilteredRows.Should().Contain(r => r.Difference.ObjectType == SchemaObjectType.Table);
        vm.FilteredRows.Should().Contain(r => r.Difference.ObjectName == "dbo.Orders.[CustomerName]");
        vm.FilteredRows.Should().NotContain(r => r.Difference.ObjectName == "dbo.Orders.[OrderDate]");
    }

    [Fact]
    public void FilterTableName與FilterColumnName_同時設定_取交集()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Orders.[CustomerName]",    RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectType = SchemaObjectType.Column, ObjectName = "dbo.Customers.[CustomerName]", RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        // Act
        vm.FilterTableName = "Orders";
        vm.FilterColumnName = "Customer";

        // Assert
        vm.FilteredRows.Should().ContainSingle()
            .Which.Difference.ObjectName.Should().Be("dbo.Orders.[CustomerName]");
    }

    #endregion
}
