using ClosedXML.Excel;
using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Infrastructure.Services;

namespace TableSpec.Infrastructure.Tests.Services;

/// <summary>
/// Schema Compare Excel 匯出器測試
/// </summary>
public class SchemaCompareExcelExporterTests : IDisposable
{
    private readonly SchemaCompareExcelExporter _exporter;
    private readonly string _tempPath;

    public SchemaCompareExcelExporterTests()
    {
        _exporter = new SchemaCompareExcelExporter();
        _tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    #region 基本匯出測試

    [Fact]
    public async Task ExportAsync_應該產生有效的Excel檔案()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        File.Exists(_tempPath).Should().BeTrue();
        using var workbook = new XLWorkbook(_tempPath);
        workbook.Worksheets.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportAsync_應該包含摘要工作表()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        workbook.Worksheets.Contains("摘要").Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_摘要工作表應該包含比對資訊()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var summarySheet = workbook.Worksheet("摘要");

        // 應該包含基準環境名稱
        summarySheet.CellsUsed().Any(c => c.GetString().Contains("開發環境")).Should().BeTrue();
    }

    #endregion

    #region 差異清單工作表測試

    [Fact]
    public async Task ExportAsync_應該包含差異清單工作表()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        workbook.Worksheets.Contains("差異清單").Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_差異清單應該包含所有差異項目()
    {
        // Arrange
        var comparisons = CreateTestComparisons();
        var totalDiffs = comparisons.Sum(c => c.Differences.Count);

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var diffSheet = workbook.Worksheet("差異清單");

        // 減去標題列，資料列數應該等於差異數
        var dataRowCount = diffSheet.RowsUsed().Count() - 1;
        dataRowCount.Should().Be(totalDiffs);
    }

    [Fact]
    public async Task ExportAsync_差異清單應該包含風險等級欄位()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var diffSheet = workbook.Worksheet("差異清單");
        var headerRow = diffSheet.Row(1);

        headerRow.CellsUsed().Any(c => c.GetString().Contains("風險")).Should().BeTrue();
    }

    #endregion

    #region 風險等級顏色測試

    [Fact]
    public async Task ExportAsync_低風險項目應該使用綠色()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發環境",
            TargetEnvironment = "測試環境",
            ComparedAt = DateTime.Now,
            Differences = new List<SchemaDifference>
            {
                new()
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Email]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                }
            }
        };

        // Act
        await _exporter.ExportAsync(new[] { comparison }, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var diffSheet = workbook.Worksheet("差異清單");

        // 找到風險等級欄位並檢查顏色
        var riskCell = diffSheet.Row(2).CellsUsed()
            .FirstOrDefault(c => c.GetString() == "Low" || c.GetString() == "低");

        riskCell.Should().NotBeNull();
        // 綠色系（檢查背景色的 RGB 值）
        var bgColor = riskCell!.Style.Fill.BackgroundColor;
        bgColor.Color.G.Should().BeGreaterThan(bgColor.Color.R);
    }

    [Fact]
    public async Task ExportAsync_高風險項目應該使用紅色()
    {
        // Arrange
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "開發環境",
            TargetEnvironment = "測試環境",
            ComparedAt = DateTime.Now,
            Differences = new List<SchemaDifference>
            {
                new()
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = "[dbo].[Users].[Email]",
                    DifferenceType = DifferenceType.Modified,
                    RiskLevel = RiskLevel.High
                }
            }
        };

        // Act
        await _exporter.ExportAsync(new[] { comparison }, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var diffSheet = workbook.Worksheet("差異清單");

        var riskCell = diffSheet.Row(2).CellsUsed()
            .FirstOrDefault(c => c.GetString() == "High" || c.GetString() == "高");

        riskCell.Should().NotBeNull();
        var bgColor = riskCell!.Style.Fill.BackgroundColor;
        bgColor.Color.R.Should().BeGreaterThan(bgColor.Color.G);
    }

    #endregion

    #region 多環境比對測試

    [Fact]
    public async Task ExportAsync_多環境比對應該在摘要顯示所有環境()
    {
        // Arrange
        var comparisons = new List<SchemaComparison>
        {
            new()
            {
                BaseEnvironment = "開發環境",
                TargetEnvironment = "測試環境",
                ComparedAt = DateTime.Now,
                Differences = new List<SchemaDifference>()
            },
            new()
            {
                BaseEnvironment = "開發環境",
                TargetEnvironment = "正式環境",
                ComparedAt = DateTime.Now,
                Differences = new List<SchemaDifference>()
            }
        };

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        using var workbook = new XLWorkbook(_tempPath);
        var summarySheet = workbook.Worksheet("摘要");
        var allText = string.Join(" ", summarySheet.CellsUsed().Select(c => c.GetString()));

        allText.Should().Contain("測試環境");
        allText.Should().Contain("正式環境");
    }

    #endregion

    #region 輔助方法

    private static IList<SchemaComparison> CreateTestComparisons()
    {
        return new List<SchemaComparison>
        {
            new()
            {
                BaseEnvironment = "開發環境",
                TargetEnvironment = "測試環境",
                ComparedAt = DateTime.Now,
                Differences = new List<SchemaDifference>
                {
                    new()
                    {
                        ObjectType = SchemaObjectType.Column,
                        ObjectName = "[dbo].[Users].[Email]",
                        DifferenceType = DifferenceType.Added,
                        RiskLevel = RiskLevel.Low,
                        Description = "欄位不存在於目標環境"
                    },
                    new()
                    {
                        ObjectType = SchemaObjectType.Index,
                        ObjectName = "[dbo].[Users].[IX_Email]",
                        DifferenceType = DifferenceType.Added,
                        RiskLevel = RiskLevel.Low,
                        Description = "索引不存在於目標環境"
                    }
                }
            }
        };
    }

    #endregion
}
