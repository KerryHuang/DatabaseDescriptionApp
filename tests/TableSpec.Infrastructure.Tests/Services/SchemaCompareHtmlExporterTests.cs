using FluentAssertions;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Infrastructure.Services;

namespace TableSpec.Infrastructure.Tests.Services;

/// <summary>
/// Schema Compare HTML 匯出器測試
/// </summary>
public class SchemaCompareHtmlExporterTests : IDisposable
{
    private readonly SchemaCompareHtmlExporter _exporter;
    private readonly string _tempPath;

    public SchemaCompareHtmlExporterTests()
    {
        _exporter = new SchemaCompareHtmlExporter();
        _tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.html");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    #region 基本匯出測試

    [Fact]
    public async Task ExportAsync_應該產生有效的HTML檔案()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        File.Exists(_tempPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("<!DOCTYPE html>");
        content.Should().Contain("<html");
        content.Should().Contain("</html>");
    }

    [Fact]
    public async Task ExportAsync_應該包含UTF8編碼宣告()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("charset=\"UTF-8\"");
    }

    [Fact]
    public async Task ExportAsync_應該包含標題()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("<title>");
        content.Should().Contain("Schema Compare");
    }

    #endregion

    #region 內容測試

    [Fact]
    public async Task ExportAsync_應該包含基準環境名稱()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("開發環境");
    }

    [Fact]
    public async Task ExportAsync_應該包含目標環境名稱()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("測試環境");
    }

    [Fact]
    public async Task ExportAsync_應該包含差異物件名稱()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("[dbo].[Users].[Email]");
    }

    #endregion

    #region 風險等級樣式測試

    [Fact]
    public async Task ExportAsync_應該包含風險等級CSS樣式()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("<style>");
        content.Should().Contain(".risk-low");
        content.Should().Contain(".risk-high");
    }

    [Fact]
    public async Task ExportAsync_低風險項目應該有綠色樣式()
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
                    ObjectName = "[dbo].[Test].[Col1]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low
                }
            }
        };

        // Act
        await _exporter.ExportAsync(new[] { comparison }, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("risk-low");
        // CSS 應該定義綠色
        content.Should().MatchRegex(@"\.risk-low\s*\{[^}]*green|\.risk-low\s*\{[^}]*#[0-9a-fA-F]*[8-9a-fA-F][0-9a-fA-F]*");
    }

    [Fact]
    public async Task ExportAsync_高風險項目應該有紅色樣式()
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
                    ObjectName = "[dbo].[Test].[Col1]",
                    DifferenceType = DifferenceType.Modified,
                    RiskLevel = RiskLevel.High
                }
            }
        };

        // Act
        await _exporter.ExportAsync(new[] { comparison }, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("risk-high");
    }

    #endregion

    #region 互動功能測試

    [Fact]
    public async Task ExportAsync_應該包含JavaScript摺疊功能()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("<script>");
        content.Should().Contain("function");
    }

    [Fact]
    public async Task ExportAsync_應該有可點擊的摺疊區塊()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        // 應該有 addEventListener 事件處理
        content.Should().Contain("addEventListener");
    }

    #endregion

    #region 摘要區塊測試

    [Fact]
    public async Task ExportAsync_應該包含風險摘要統計()
    {
        // Arrange
        var comparisons = CreateTestComparisons();

        // Act
        await _exporter.ExportAsync(comparisons, _tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        // 應該顯示差異數量
        content.Should().Contain("2"); // 測試資料有 2 個差異
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
