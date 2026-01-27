using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// ColumnUsageService 測試
/// </summary>
public class ColumnUsageServiceTests
{
    private readonly IColumnUsageRepository _repository;
    private readonly ColumnUsageService _service;

    public ColumnUsageServiceTests()
    {
        _repository = Substitute.For<IColumnUsageRepository>();
        _service = new ColumnUsageService(_repository);
    }

    #region GetStatisticsAsync 測試

    [Fact]
    public async Task GetStatisticsAsync_無資料_應回傳空清單()
    {
        // Arrange
        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ColumnUsageDetail>());

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatisticsAsync_應按欄位名稱分組()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Id", "dbo", "Users", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Id", "dbo", "Orders", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Name", "dbo", "Users", "TABLE", "nvarchar(50)", "nvarchar", 50, true)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.ColumnName == "Id" && s.UsageCount == 2);
        result.Should().Contain(s => s.ColumnName == "Name" && s.UsageCount == 1);
    }

    [Fact]
    public async Task GetStatisticsAsync_型別一致_IsTypeConsistent應為True()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("CreateDate", "dbo", "Users", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("CreateDate", "dbo", "Orders", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("CreateDate", "dbo", "Products", "TABLE", "datetime", "datetime", 8, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.IsTypeConsistent.Should().BeTrue();
        stat.IsLengthConsistent.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatisticsAsync_型別不一致_IsTypeConsistent應為False()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("CustomerID", "dbo", "Orders", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("CustomerID", "dbo", "Invoices", "TABLE", "varchar(20)", "varchar", 20, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.IsTypeConsistent.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatisticsAsync_長度不一致_IsLengthConsistent應為False()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Name", "dbo", "Users", "TABLE", "nvarchar(50)", "nvarchar", 50, false),
            CreateUsageDetail("Name", "dbo", "Products", "TABLE", "nvarchar(100)", "nvarchar", 100, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.IsTypeConsistent.Should().BeTrue(); // 基礎型別一致
        stat.IsLengthConsistent.Should().BeFalse(); // 長度不一致
    }

    [Fact]
    public async Task GetStatisticsAsync_可空性不一致_IsNullabilityConsistent應為False()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Email", "dbo", "Users", "TABLE", "nvarchar(100)", "nvarchar", 100, false),
            CreateUsageDetail("Email", "dbo", "Contacts", "TABLE", "nvarchar(100)", "nvarchar", 100, true)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.IsTypeConsistent.Should().BeTrue();
        stat.IsLengthConsistent.Should().BeTrue();
        stat.IsNullabilityConsistent.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatisticsAsync_應設定PrimaryDataType為最常見的型別()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Status", "dbo", "Orders", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Status", "dbo", "Products", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Status", "dbo", "Users", "TABLE", "varchar(10)", "varchar", 10, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.PrimaryDataType.Should().Be("int");
        stat.PrimaryBaseType.Should().Be("int");
    }

    [Fact]
    public async Task GetStatisticsAsync_Usages應包含HasDifference標記()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Price", "dbo", "Products", "TABLE", "decimal(18,2)", "decimal", 0, false, 18, 2),
            CreateUsageDetail("Price", "dbo", "Orders", "TABLE", "decimal(18,2)", "decimal", 0, false, 18, 2),
            CreateUsageDetail("Price", "dbo", "Invoices", "TABLE", "money", "money", 8, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        var stat = result.Single();
        stat.Usages.Should().HaveCount(3);

        // decimal(18,2) 是主要型別，不應標記為有差異
        stat.Usages.Where(u => u.DataType == "decimal(18,2)")
            .Should().AllSatisfy(u => u.HasDifference.Should().BeFalse());

        // money 與主要型別不同，應標記為有差異
        stat.Usages.Single(u => u.DataType == "money")
            .HasDifference.Should().BeTrue();
    }

    #endregion

    #region GetFilteredStatisticsAsync 測試

    [Fact]
    public async Task GetFilteredStatisticsAsync_搜尋欄位名稱_應過濾結果()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("CreateDate", "dbo", "Users", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("ModifyDate", "dbo", "Users", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("Name", "dbo", "Users", "TABLE", "nvarchar(50)", "nvarchar", 50, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetFilteredStatisticsAsync("Date");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.ColumnName == "CreateDate");
        result.Should().Contain(s => s.ColumnName == "ModifyDate");
    }

    [Fact]
    public async Task GetFilteredStatisticsAsync_只顯示不一致_應過濾結果()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Id", "dbo", "Users", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Id", "dbo", "Orders", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Name", "dbo", "Users", "TABLE", "nvarchar(50)", "nvarchar", 50, false),
            CreateUsageDetail("Name", "dbo", "Products", "TABLE", "nvarchar(100)", "nvarchar", 100, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetFilteredStatisticsAsync(showOnlyInconsistent: true);

        // Assert
        result.Should().HaveCount(1);
        result.Single().ColumnName.Should().Be("Name");
    }

    [Fact]
    public async Task GetFilteredStatisticsAsync_最小出現次數_應過濾結果()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("Id", "dbo", "Users", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Id", "dbo", "Orders", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Id", "dbo", "Products", "TABLE", "int", "int", 4, false),
            CreateUsageDetail("Name", "dbo", "Users", "TABLE", "nvarchar(50)", "nvarchar", 50, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act
        var result = await _service.GetFilteredStatisticsAsync(minUsageCount: 2);

        // Assert
        result.Should().HaveCount(1);
        result.Single().ColumnName.Should().Be("Id");
        result.Single().UsageCount.Should().Be(3);
    }

    [Fact]
    public async Task GetFilteredStatisticsAsync_組合篩選_應正確過濾()
    {
        // Arrange
        var testUsages = new List<ColumnUsageDetail>
        {
            CreateUsageDetail("CreateDate", "dbo", "Users", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("CreateDate", "dbo", "Orders", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("ModifyDate", "dbo", "Users", "TABLE", "datetime", "datetime", 8, false),
            CreateUsageDetail("ModifyDate", "dbo", "Orders", "TABLE", "datetime2", "datetime2", 8, false)
        };

        _repository.GetAllColumnUsagesAsync(Arg.Any<CancellationToken>())
            .Returns(testUsages);

        // Act - 搜尋 Date，只顯示不一致，最小次數 2
        var result = await _service.GetFilteredStatisticsAsync(
            searchText: "Date",
            showOnlyInconsistent: true,
            minUsageCount: 2);

        // Assert
        result.Should().HaveCount(1);
        result.Single().ColumnName.Should().Be("ModifyDate");
    }

    #endregion

    #region 輔助方法

    private static ColumnUsageDetail CreateUsageDetail(
        string columnName,
        string schemaName,
        string objectName,
        string objectType,
        string dataType,
        string baseType,
        int maxLength,
        bool isNullable,
        int precision = 0,
        int scale = 0)
    {
        return new ColumnUsageDetail
        {
            ColumnName = columnName,
            SchemaName = schemaName,
            ObjectName = objectName,
            ObjectType = objectType,
            DataType = dataType,
            BaseType = baseType,
            MaxLength = maxLength,
            IsNullable = isNullable,
            Precision = precision,
            Scale = scale
        };
    }

    #endregion
}
