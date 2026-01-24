using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Tests.Services;

/// <summary>
/// Schema 比對服務測試
/// </summary>
public class SchemaCompareServiceTests
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _service;

    public SchemaCompareServiceTests()
    {
        _schemaCollector = Substitute.For<ISchemaCollector>();
        _service = new SchemaCompareService(_schemaCollector);
    }

    #region 基本比對測試

    [Fact]
    public async Task CompareAsync_兩個相同的Schema_應該沒有差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        var targetSchema = CreateTestSchema("目標環境");

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeFalse();
        result.BaseEnvironment.Should().Be("基準環境");
        result.TargetEnvironment.Should().Be("目標環境");
    }

    [Fact]
    public async Task CompareAsync_基準有表格目標沒有_應該產生Added差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable { Schema = "dbo", Name = "Users" });

        var targetSchema = CreateTestSchema("目標環境");

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Differences.Should().ContainSingle();
        var diff = result.Differences.First();
        diff.ObjectType.Should().Be(SchemaObjectType.Table);
        diff.DifferenceType.Should().Be(DifferenceType.Added);
        diff.ObjectName.Should().Contain("Users");
    }

    [Fact]
    public async Task CompareAsync_目標有表格基準沒有_應該產生Added差異_因為最大化原則()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable { Schema = "dbo", Name = "ExtraTable" });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Differences.Should().ContainSingle();
        var diff = result.Differences.First();
        diff.DifferenceType.Should().Be(DifferenceType.Added);
        diff.Description.Should().Contain("基準");
    }

    #endregion

    #region 欄位比對測試

    [Fact]
    public async Task CompareAsync_基準有欄位目標沒有_應該產生Column的Added差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var columnDiff = result.Differences.FirstOrDefault(d => d.ObjectType == SchemaObjectType.Column);
        columnDiff.Should().NotBeNull();
        columnDiff!.ObjectName.Should().Contain("Email");
        columnDiff.DifferenceType.Should().Be(DifferenceType.Added);
    }

    [Fact]
    public async Task CompareAsync_欄位長度不同_應該產生Modified差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 100 }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var diff = result.Differences.First();
        diff.DifferenceType.Should().Be(DifferenceType.Modified);
        diff.PropertyName.Should().Be("MaxLength");
        diff.SourceValue.Should().Be("200");
        diff.TargetValue.Should().Be("100");
    }

    [Fact]
    public async Task CompareAsync_欄位型別不同_應該產生Modified差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Status", DataType = "INT" }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Status", DataType = "VARCHAR", MaxLength = 50 }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var diff = result.Differences.First();
        diff.DifferenceType.Should().Be(DifferenceType.Modified);
        diff.PropertyName.Should().Be("DataType");
    }

    [Fact]
    public async Task CompareAsync_欄位Nullable不同_應該產生Modified差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200, IsNullable = false }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200, IsNullable = true }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var diff = result.Differences.First();
        diff.PropertyName.Should().Be("IsNullable");
    }

    #endregion

    #region 風險評估測試

    [Fact]
    public async Task CompareAsync_新增Nullable欄位_應該是低風險()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "NewColumn", DataType = "NVARCHAR", MaxLength = 100, IsNullable = true }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        var diff = result.Differences.First(d => d.ObjectType == SchemaObjectType.Column);
        diff.RiskLevel.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public async Task CompareAsync_新增NotNull欄位有Default_應該是中風險()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "Status", DataType = "INT", IsNullable = false, DefaultValue = "0" }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        var diff = result.Differences.First(d => d.ObjectType == SchemaObjectType.Column);
        diff.RiskLevel.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public async Task CompareAsync_縮短欄位長度_應該是高風險()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 100 }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        var diff = result.Differences.First();
        diff.RiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public async Task CompareAsync_延長欄位長度_應該是低風險()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 100 }
            }
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        var diff = result.Differences.First();
        diff.RiskLevel.Should().Be(RiskLevel.Low);
    }

    #endregion

    #region 索引比對測試

    [Fact]
    public async Task CompareAsync_基準有索引目標沒有_應該產生Index的Added差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn> { new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 } },
            Indexes = new List<SchemaIndex>
            {
                new() { Name = "IX_Users_Email", Columns = new List<string> { "Email" } }
            }
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn> { new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 } },
            Indexes = new List<SchemaIndex>()
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var indexDiff = result.Differences.FirstOrDefault(d => d.ObjectType == SchemaObjectType.Index);
        indexDiff.Should().NotBeNull();
        indexDiff!.DifferenceType.Should().Be(DifferenceType.Added);
        indexDiff.RiskLevel.Should().Be(RiskLevel.Low); // 新增索引是低風險
    }

    #endregion

    #region 程式物件比對測試

    [Fact]
    public async Task CompareAsync_View定義不同_應該產生Modified差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.Views.Add(new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Test",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_Test] AS SELECT Id, Name FROM Users"
        });

        var targetSchema = CreateTestSchema("目標環境");
        targetSchema.Views.Add(new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "vw_Test",
            ObjectType = ProgramObjectType.View,
            Definition = "CREATE VIEW [dbo].[vw_Test] AS SELECT Id FROM Users"
        });

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var viewDiff = result.Differences.FirstOrDefault(d => d.ObjectType == SchemaObjectType.View);
        viewDiff.Should().NotBeNull();
        viewDiff!.DifferenceType.Should().Be(DifferenceType.Modified);
    }

    [Fact]
    public async Task CompareAsync_SP不存在於目標_應該產生Added差異()
    {
        // Arrange
        var baseSchema = CreateTestSchema("基準環境");
        baseSchema.StoredProcedures.Add(new SchemaProgramObject
        {
            Schema = "dbo",
            Name = "sp_GetUsers",
            ObjectType = ProgramObjectType.StoredProcedure,
            Definition = "CREATE PROCEDURE [dbo].[sp_GetUsers] AS SELECT * FROM Users"
        });

        var targetSchema = CreateTestSchema("目標環境");

        // Act
        var result = await _service.CompareAsync(baseSchema, targetSchema);

        // Assert
        result.HasDifferences.Should().BeTrue();
        var spDiff = result.Differences.FirstOrDefault(d => d.ObjectType == SchemaObjectType.StoredProcedure);
        spDiff.Should().NotBeNull();
        spDiff!.DifferenceType.Should().Be(DifferenceType.Added);
    }

    #endregion

    #region 多環境比對測試

    [Fact]
    public async Task CompareMultipleAsync_應該比對基準與所有目標環境()
    {
        // Arrange
        var baseSchema = CreateTestSchema("開發環境");
        baseSchema.Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        var targetSchemas = new List<DatabaseSchema>
        {
            CreateTestSchema("測試環境"),
            CreateTestSchema("正式環境")
        };

        // 測試環境缺少 Email 欄位
        targetSchemas[0].Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" }
            }
        });

        // 正式環境完全相同
        targetSchemas[1].Tables.Add(new SchemaTable
        {
            Schema = "dbo",
            Name = "Users",
            Columns = new List<SchemaColumn>
            {
                new() { Name = "Id", DataType = "INT" },
                new() { Name = "Email", DataType = "NVARCHAR", MaxLength = 200 }
            }
        });

        // Act
        var results = await _service.CompareMultipleAsync(baseSchema, targetSchemas);

        // Assert
        results.Should().HaveCount(2);
        results[0].TargetEnvironment.Should().Be("測試環境");
        results[0].HasDifferences.Should().BeTrue();
        results[1].TargetEnvironment.Should().Be("正式環境");
        results[1].HasDifferences.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static DatabaseSchema CreateTestSchema(string connectionName)
    {
        return new DatabaseSchema
        {
            ConnectionName = connectionName,
            DatabaseName = "TestDb",
            ServerName = "localhost",
            CollectedAt = DateTime.Now,
            Tables = new List<SchemaTable>(),
            Views = new List<SchemaProgramObject>(),
            StoredProcedures = new List<SchemaProgramObject>(),
            Functions = new List<SchemaProgramObject>(),
            Triggers = new List<SchemaProgramObject>()
        };
    }

    #endregion
}
