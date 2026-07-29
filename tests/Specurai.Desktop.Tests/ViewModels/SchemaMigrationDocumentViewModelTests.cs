using System;
using System.Collections.Generic;
using System.Threading;
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
        _connectionManager.GetEnabledProfiles().Returns(profiles);

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
    public void Constructor_有IsDefault連線_ConnectionProfiles應包含IsDefault連線()
    {
        // Arrange
        var defaultProfile = new ConnectionProfile { Name = "正式環境", Server = "prod", Database = "ProdDb", IsDefault = true };
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "Anchiao-Staging", Server = "other", Database = "OtherDb", IsDefault = false },
            defaultProfile
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetEnabledProfiles().Returns(profiles);

        // Act
        var vm = new SchemaMigrationDocumentViewModel(
            _migrationService, _scriptGenerator, _executor, _connectionManager);

        // Assert — SelectedBaseProfile 由 Dispatcher.UIThread.Post 設定，此處驗證連線清單正確載入
        vm.ConnectionProfiles.Should().HaveCount(2);
        vm.ConnectionProfiles.Should().Contain(p => p.IsDefault);
    }

    [Fact]
    public void Constructor_無IsDefault連線_ConnectionProfiles應正確載入()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb", IsDefault = false },
            new() { Name = "其他環境", Server = "other", Database = "OtherDb", IsDefault = false }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetEnabledProfiles().Returns(profiles);

        // Act
        var vm = new SchemaMigrationDocumentViewModel(
            _migrationService, _scriptGenerator, _executor, _connectionManager);

        // Assert
        vm.ConnectionProfiles.Should().HaveCount(2);
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

    #region Schema 篩選測試

    [Fact]
    public void RebuildSchemaFilters_分析後_應產生依名稱排序的不重複結構描述清單()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[Sales].[Orders]", Schema = "Sales", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[dbo].[Products]", Schema = "dbo", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[Sales].[Invoices]", Schema = "Sales", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        // Act — 模擬 AnalyzeAsync 後的重建行為
        var method = vm.GetType().GetMethod("RebuildSchemaFilters",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(vm, null);

        // Assert
        vm.SchemaFilters.Should().HaveCount(2);
        vm.SchemaFilters.Should().Contain(f => f.Label == "Sales");
        vm.SchemaFilters.Should().Contain(f => f.Label == "dbo");
    }

    [Fact]
    public void SchemaFilter_選取特定Schema_FilteredRows只顯示該Schema的列()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[Sales].[Orders]", Schema = "Sales", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[dbo].[Products]", Schema = "dbo", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        var method = vm.GetType().GetMethod("RebuildSchemaFilters",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(vm, null);

        // Act — 勾選 "Sales"
        vm.SchemaFilters.First(f => f.Label == "Sales").IsSelected = true;

        // Assert
        vm.FilteredRows.Should().ContainSingle()
            .Which.Difference.Schema.Should().Be("Sales");
    }

    [Fact]
    public void SchemaFilterLabel_選取一個Schema_標籤應顯示數量()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();
        vm.DifferenceRows.Add(new MigrationDifferenceRowViewModel(
            new SchemaDifference { ObjectName = "[Sales].[Orders]", Schema = "Sales", ObjectType = SchemaObjectType.Table, RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }));

        var method = vm.GetType().GetMethod("RebuildSchemaFilters",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(vm, null);

        // Act
        vm.SchemaFilters.First().IsSelected = true;

        // Assert
        vm.SchemaFilterLabel.Should().Be("結構描述（1）▾");
    }

    #endregion

    #region 執行前確認閘門測試

    /// <summary>
    /// 建立可執行狀態的 VM：
    /// 1. 透過反射設定 _currentAnalysis（ExecuteMigrationAsync 的第一個 null 守衛）
    /// 2. 直接填入 FilteredRows 含一筆已選取且可執行（RiskLevel.Low）的列
    /// 3. 設定 SelectedTargetProfile（另一個 null 守衛）
    /// 4. _connectionManager.GetConnectionString 回傳合法連線字串
    /// </summary>
    private SchemaMigrationDocumentViewModel BuildExecutableVm()
    {
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile>());
        _connectionManager.GetConnectionString(Arg.Any<Guid>()).Returns("Server=localhost;Database=Target;");

        var vm = new SchemaMigrationDocumentViewModel(
            _migrationService, _scriptGenerator, _executor, _connectionManager);

        // 設定 SelectedTargetProfile（守衛之一）
        var targetProfile = new ConnectionProfile { Name = "目標環境", Server = "target", Database = "TargetDb" };
        vm.SelectedTargetProfile = targetProfile;

        // 透過反射設定 _currentAnalysis（守衛之一）
        var analysis = new MigrationAnalysis
        {
            BaseSchema = new DatabaseSchema { ConnectionName = "基準" },
            TargetSchema = new DatabaseSchema { ConnectionName = "目標" },
            Comparison = new SchemaComparison
            {
                Differences = new List<SchemaDifference>
                {
                    new() { ObjectType = SchemaObjectType.Table, ObjectName = "dbo.Orders",
                            RiskLevel = RiskLevel.Low, DifferenceType = DifferenceType.Added }
                }
            }
        };
        var field = typeof(SchemaMigrationDocumentViewModel)
            .GetField("_currentAnalysis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("若 _currentAnalysis 欄位改名，請同步更新此測試");
        field!.SetValue(vm, analysis);

        // 直接填入 FilteredRows，使 selected.Count > 0（守衛之一）
        var diff = analysis.Comparison.Differences[0];
        var row = new MigrationDifferenceRowViewModel(diff); // 預設 IsSelected = true，IsExecutable = true（Low）
        vm.FilteredRows.Add(row);

        // _scriptGenerator 回傳合法腳本讓 ExecuteAsync 能被呼叫
        _scriptGenerator.Generate(
            Arg.Any<IList<SchemaDifference>>(),
            Arg.Any<DatabaseSchema>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DatabaseSchema?>())
            .Returns(new SyncScript { ApplyScript = "-- migration script" });

        // _executor 回傳成功報告（true 測試才能走到底）
        _executor.ExecuteAsync(
            Arg.Any<SyncScript>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MigrationReport { IsSuccess = true }));

        return vm;
    }

    [Fact]
    public async Task ExecuteMigration_確認回傳False_不應執行()
    {
        // Arrange
        var vm = BuildExecutableVm();
        vm.ConfirmExecuteCallback = _ => Task.FromResult(false);

        // Act
        await vm.ExecuteMigrationCommand.ExecuteAsync(null);

        // Assert
        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<SyncScript>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteMigration_確認回傳True_應執行()
    {
        // Arrange
        var vm = BuildExecutableVm();
        vm.ConfirmExecuteCallback = _ => Task.FromResult(true);

        // Act
        await vm.ExecuteMigrationCommand.ExecuteAsync(null);

        // Assert
        await _executor.Received().ExecuteAsync(
            Arg.Any<SyncScript>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
