using FluentAssertions;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Entities;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// ColumnTypeGroupViewModel 測試
/// </summary>
public class ColumnTypeGroupViewModelTests
{
    #region 建構函式測試

    [Fact]
    public void Constructor_應建立空的Columns集合()
    {
        // Act
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.Columns.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ColumnName預設為空()
    {
        // Act
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.ColumnName.Should().BeEmpty();
    }

    #endregion

    #region Level 計算測試

    [Fact]
    public void Level_無欄位_應為Consistent()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.Level.Should().Be(ConsistencyLevel.Consistent);
    }

    [Fact]
    public void Level_所有欄位型態相同_應為Consistent()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });

        // Assert
        vm.Level.Should().Be(ConsistencyLevel.Consistent);
    }

    [Fact]
    public void Level_兩種型態且不一致比例低_應為Warning()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        // 90% 一致 (9個 int, 1個 bigint)
        for (int i = 0; i < 9; i++)
        {
            vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        }
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.Level.Should().Be(ConsistencyLevel.Warning);
    }

    [Fact]
    public void Level_三種以上型態_應為Severe()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "varchar(50)" });

        // Assert
        vm.Level.Should().Be(ConsistencyLevel.Severe);
    }

    [Fact]
    public void Level_不一致比例超過30percent_應為Severe()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        // 60% 一致 (3個 int, 2個 bigint) = 40% 不一致 > 30%
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.Level.Should().Be(ConsistencyLevel.Severe);
    }

    #endregion

    #region LevelText 測試

    [Fact]
    public void LevelText_Consistent等級_應為一致()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.LevelText.Should().Be("一致");
    }

    [Fact]
    public void LevelText_Warning等級_應為警告()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        for (int i = 0; i < 9; i++)
        {
            vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        }
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.LevelText.Should().Be("警告");
    }

    [Fact]
    public void LevelText_Severe等級_應為嚴重()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "varchar(50)" });

        // Assert
        vm.LevelText.Should().Be("嚴重");
    }

    #endregion

    #region PrimaryType 測試

    [Fact]
    public void PrimaryType_無欄位_應為空()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.PrimaryType.Should().BeEmpty();
    }

    [Fact]
    public void PrimaryType_應回傳出現最多的型態()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.PrimaryType.Should().Be("int");
    }

    [Fact]
    public void PrimaryType_型態比較應不區分大小寫()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "INT" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert - INT 和 int 視為相同型態
        vm.DistinctTypeCount.Should().Be(2);
    }

    #endregion

    #region TableCount 測試

    [Fact]
    public void TableCount_應回傳Columns數量()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int", TableName = "Users" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int", TableName = "Orders" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int", TableName = "Products" });

        // Assert
        vm.TableCount.Should().Be(3);
    }

    #endregion

    #region InconsistentCount 測試

    [Fact]
    public void InconsistentCount_全部一致_應為0()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });

        // Assert
        vm.InconsistentCount.Should().Be(0);
    }

    [Fact]
    public void InconsistentCount_應回傳與主要型態不同的數量()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.InconsistentCount.Should().Be(1);
    }

    #endregion

    #region DistinctTypeCount 測試

    [Fact]
    public void DistinctTypeCount_應回傳不同型態的數量()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "varchar(50)" });

        // Assert
        vm.DistinctTypeCount.Should().Be(3);
    }

    #endregion

    #region TypeSummary 測試

    [Fact]
    public void TypeSummary_無欄位_應為空()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel();

        // Assert
        vm.TypeSummary.Should().BeEmpty();
    }

    [Fact]
    public void TypeSummary_應顯示各型態數量()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" });

        // Assert
        vm.TypeSummary.Should().Contain("int × 2");
        vm.TypeSummary.Should().Contain("bigint × 1");
    }

    #endregion

    #region RefreshCalculatedProperties 測試

    [Fact]
    public void RefreshCalculatedProperties_應設定每個欄位的IsConsistent()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        var consistentColumn = new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" };
        var inconsistentColumn = new ColumnTypeInfo { ColumnName = "UserId", DataType = "bigint" };
        vm.Columns.Add(consistentColumn);
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });
        vm.Columns.Add(inconsistentColumn);

        // Act
        vm.RefreshCalculatedProperties();

        // Assert
        consistentColumn.IsConsistent.Should().BeTrue();
        inconsistentColumn.IsConsistent.Should().BeFalse();
    }

    [Fact]
    public void RefreshCalculatedProperties_應觸發屬性變更通知()
    {
        // Arrange
        var vm = new ColumnTypeGroupViewModel { ColumnName = "UserId" };
        vm.Columns.Add(new ColumnTypeInfo { ColumnName = "UserId", DataType = "int" });

        var changedProperties = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        // Act
        vm.RefreshCalculatedProperties();

        // Assert
        changedProperties.Should().Contain("Level");
        changedProperties.Should().Contain("LevelText");
        changedProperties.Should().Contain("PrimaryType");
        changedProperties.Should().Contain("TableCount");
        changedProperties.Should().Contain("InconsistentCount");
        changedProperties.Should().Contain("DistinctTypeCount");
        changedProperties.Should().Contain("TypeSummary");
    }

    #endregion
}

/// <summary>
/// ConsistencyLevel 列舉測試
/// </summary>
public class ConsistencyLevelTests
{
    [Fact]
    public void ConsistencyLevel_Consistent應為0()
    {
        // Assert
        ((int)ConsistencyLevel.Consistent).Should().Be(0);
    }

    [Fact]
    public void ConsistencyLevel_Warning應為1()
    {
        // Assert
        ((int)ConsistencyLevel.Warning).Should().Be(1);
    }

    [Fact]
    public void ConsistencyLevel_Severe應為2()
    {
        // Assert
        ((int)ConsistencyLevel.Severe).Should().Be(2);
    }

    [Fact]
    public void ConsistencyLevel_數值順序應為Consistent小於Warning小於Severe()
    {
        // Assert
        ((int)ConsistencyLevel.Consistent).Should().BeLessThan((int)ConsistencyLevel.Warning);
        ((int)ConsistencyLevel.Warning).Should().BeLessThan((int)ConsistencyLevel.Severe);
    }
}
