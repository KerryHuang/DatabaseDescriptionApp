using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// SchemaCompareDocumentViewModel 測試
/// </summary>
public class SchemaCompareDocumentViewModelTests
{
    private readonly ISchemaCompareService _schemaCompareService;
    private readonly ISchemaCollector _schemaCollector;
    private readonly IConnectionManager _connectionManager;

    public SchemaCompareDocumentViewModelTests()
    {
        _schemaCompareService = Substitute.For<ISchemaCompareService>();
        _schemaCollector = Substitute.For<ISchemaCollector>();
        _connectionManager = Substitute.For<IConnectionManager>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Title.Should().Be("Schema Compare");
        vm.DocumentType.Should().Be("SchemaCompare");
    }

    [Fact]
    public void Constructor_有服務注入_應載入連線設定檔()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Name = "測試環境", Server = "test-server", Database = "TestDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);

        // Act
        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);

        // Assert
        vm.AvailableProfiles.Should().HaveCount(2);
    }

    #endregion

    #region 初始狀態測試

    [Fact]
    public void 初始狀態_SelectedProfiles應為空()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.SelectedProfiles.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_BaseProfile應為Null()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.BaseProfile.Should().BeNull();
    }

    [Fact]
    public void 初始狀態_IsComparing應為False()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.IsComparing.Should().BeFalse();
    }

    [Fact]
    public void 初始狀態_ComparisonResults應為空()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.ComparisonResults.Should().BeEmpty();
    }

    [Fact]
    public void 初始狀態_StatusMessage應為就緒()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.StatusMessage.Should().Be("就緒");
    }

    #endregion

    #region DocumentViewModel 屬性測試

    [Fact]
    public void DocumentType_應返回SchemaCompare()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.DocumentType.Should().Be("SchemaCompare");
    }

    [Fact]
    public void DocumentKey_應返回SchemaCompare()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.DocumentKey.Should().Be("SchemaCompare");
    }

    [Fact]
    public void Title_應為SchemaCompare()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.Title.Should().Be("Schema Compare");
    }

    [Fact]
    public void Icon_應為比對圖示()
    {
        // Act
        var vm = new SchemaCompareDocumentViewModel();

        // Assert
        vm.Icon.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region BaseProfile 選擇測試

    [Fact]
    public void BaseProfile_選擇設定檔_應更新CurrentBaseName()
    {
        // Arrange
        var profile = new ConnectionProfile
        {
            Name = "開發環境",
            Server = "localhost",
            Database = "DevDb"
        };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);

        // Act
        vm.BaseProfile = profile;

        // Assert
        vm.CurrentBaseName.Should().Be("開發環境");
    }

    [Fact]
    public void BaseProfile_設為Null_應清空CurrentBaseName()
    {
        // Arrange
        var profile = new ConnectionProfile { Name = "開發環境", Server = "localhost", Database = "DevDb" };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);
        vm.BaseProfile = profile;

        // Act
        vm.BaseProfile = null;

        // Assert
        vm.CurrentBaseName.Should().BeEmpty();
    }

    #endregion

    #region CompareCommand 測試

    [Fact]
    public void CompareCommand_無BaseProfile_不應執行()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);

        // Act & Assert
        vm.CompareCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CompareCommand_無SelectedProfiles_不應執行()
    {
        // Arrange
        var profile = new ConnectionProfile { Name = "基準", Server = "localhost", Database = "DevDb" };
        _connectionManager.GetAllProfiles().Returns(new List<ConnectionProfile> { profile });

        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);
        vm.BaseProfile = profile;

        // Act & Assert
        vm.CompareCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CompareCommand_執行成功_應更新ComparisonResults()
    {
        // Arrange
        var profiles = new List<ConnectionProfile>
        {
            new() { Id = Guid.NewGuid(), Name = "開發環境", Server = "localhost", Database = "DevDb" },
            new() { Id = Guid.NewGuid(), Name = "測試環境", Server = "test-server", Database = "TestDb" }
        };
        _connectionManager.GetAllProfiles().Returns(profiles);
        _connectionManager.GetConnectionString(profiles[0].Id).Returns("Server=localhost;Database=DevDb");
        _connectionManager.GetConnectionString(profiles[1].Id).Returns("Server=test-server;Database=TestDb");

        var baseSchema = new DatabaseSchema { ConnectionName = "開發環境" };
        var targetSchema = new DatabaseSchema { ConnectionName = "測試環境" };

        _schemaCollector.CollectAsync(Arg.Any<string>(), "開發環境", Arg.Any<CancellationToken>())
            .Returns(baseSchema);
        _schemaCollector.CollectAsync(Arg.Any<string>(), "測試環境", Arg.Any<CancellationToken>())
            .Returns(targetSchema);

        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發環境",
            TargetEnvironment = "測試環境",
            ComparedAt = DateTime.Now,
            Differences = new List<SchemaDifference>()
        };

        _schemaCompareService.CompareMultipleAsync(baseSchema, Arg.Any<IList<DatabaseSchema>>())
            .Returns(new List<SchemaComparison> { comparison });

        var vm = new SchemaCompareDocumentViewModel(
            _schemaCompareService,
            _schemaCollector,
            _connectionManager);
        vm.BaseProfile = profiles[0];
        // 選擇目標環境（透過 TargetProfileItems）
        var targetItem = vm.TargetProfileItems.FirstOrDefault(p => p.Profile.Id == profiles[1].Id);
        if (targetItem != null) targetItem.IsSelected = true;

        // Act
        await vm.CompareCommand.ExecuteAsync(null);

        // Assert
        vm.ComparisonResults.Should().HaveCount(1);
        vm.StatusMessage.Should().Contain("完成");
    }

    #endregion

    #region 匯出命令測試

    [Fact]
    public void ExportExcelCommand_無比對結果_不應執行()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();

        // Act & Assert
        vm.ExportExcelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExportHtmlCommand_無比對結果_不應執行()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();

        // Act & Assert
        vm.ExportHtmlCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ExportSqlCommand_無比對結果_不應執行()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();

        // Act & Assert
        vm.ExportSqlCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region 風險篩選測試

    [Fact]
    public void FilteredDifferences_無篩選_應返回所有差異()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();
        vm.ShowLowRisk = true;
        vm.ShowMediumRisk = true;
        vm.ShowHighRisk = true;
        vm.ShowForbidden = true;

        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test2", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Medium },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test3", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.High },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test4", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.Forbidden }
            }
        };
        vm.SelectedComparison = comparison;

        // Assert
        vm.FilteredDifferences.Should().HaveCount(4);
    }

    [Fact]
    public void FilteredDifferences_只顯示高風險_應只返回高風險項目()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();
        vm.ShowLowRisk = false;
        vm.ShowMediumRisk = false;
        vm.ShowHighRisk = true;
        vm.ShowForbidden = false;

        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test2", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Medium },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test3", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.High },
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test4", DifferenceType = DifferenceType.Modified, RiskLevel = RiskLevel.Forbidden }
            }
        };
        vm.SelectedComparison = comparison;

        // Assert
        vm.FilteredDifferences.Should().HaveCount(1);
        vm.FilteredDifferences.First().RiskLevel.Should().Be(RiskLevel.High);
    }

    #endregion

    #region 取消命令測試

    [Fact]
    public void CancelCommand_執行後_StatusMessage應包含取消()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();

        // Act
        vm.CancelCommand.Execute(null);

        // Assert
        vm.StatusMessage.Should().Contain("取消");
    }

    #endregion

    #region SelectedComparison 變更測試

    [Fact]
    public void SelectedComparison_選擇比對結果_應更新FilteredDifferences()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();
        vm.ShowLowRisk = true;
        vm.ShowMediumRisk = true;
        vm.ShowHighRisk = true;
        vm.ShowForbidden = true;

        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發",
            TargetEnvironment = "測試",
            Differences = new List<SchemaDifference>
            {
                new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                new() { ObjectType = SchemaObjectType.Index, ObjectName = "Test2", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.High }
            }
        };

        // Act
        vm.SelectedComparison = comparison;

        // Assert
        vm.FilteredDifferences.Should().HaveCount(2);
    }

    #endregion

    #region 統計資訊測試

    [Fact]
    public void TotalDifferenceCount_應返回所有差異總數()
    {
        // Arrange
        var vm = new SchemaCompareDocumentViewModel();
        var comparisons = new List<SchemaComparison>
        {
            new()
            {
                BaseEnvironment = "開發",
                TargetEnvironment = "測試1",
                Differences = new List<SchemaDifference>
                {
                    new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test1", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Low },
                    new() { ObjectType = SchemaObjectType.Index, ObjectName = "Test2", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.High }
                }
            },
            new()
            {
                BaseEnvironment = "開發",
                TargetEnvironment = "測試2",
                Differences = new List<SchemaDifference>
                {
                    new() { ObjectType = SchemaObjectType.Column, ObjectName = "Test3", DifferenceType = DifferenceType.Added, RiskLevel = RiskLevel.Medium }
                }
            }
        };

        foreach (var c in comparisons)
        {
            vm.ComparisonResults.Add(c);
        }

        // Assert
        vm.TotalDifferenceCount.Should().Be(3);
    }

    #endregion
}
