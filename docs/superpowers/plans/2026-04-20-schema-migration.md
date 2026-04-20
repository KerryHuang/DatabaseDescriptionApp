# Schema Migration 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以預設資料庫為基準，將目標資料庫的 Schema 同步至一致，支援差異分析、T-SQL 腳本預覽/下載/直接執行，以及完整執行報告匯出。

**Architecture:** 擴充現有 `SchemaCompareService`（不修改），新增 `SqlScriptGenerator`（純函數）、`SchemaMigrationService`（協調層）、`SchemaMigrationExecutor`（Infrastructure 層執行）三個服務。Desktop 層新增 `SchemaMigrationDocumentViewModel` 與 `SchemaMigrationDocumentView`。

**Tech Stack:** .NET 8、Dapper、Microsoft.Data.SqlClient、Avalonia 11、CommunityToolkit.Mvvm、xUnit、NSubstitute、FluentAssertions

---

## 檔案結構

### 新增檔案

| 檔案 | 職責 |
|------|------|
| `src/Specurai.Domain/Enums/MigrationLogStatus.cs` | 執行日誌狀態 enum |
| `src/Specurai.Domain/Entities/SchemaCompare/MigrationLogEntry.cs` | 單筆執行日誌記錄 |
| `src/Specurai.Domain/Entities/SchemaCompare/MigrationReport.cs` | 完整 Migration 報告 |
| `src/Specurai.Domain/Entities/SchemaCompare/MigrationAnalysis.cs` | 分析結果（含 DatabaseSchema + 分類差異） |
| `src/Specurai.Application/Services/ISqlScriptGenerator.cs` | T-SQL 產生器介面 |
| `src/Specurai.Application/Services/SqlScriptGenerator.cs` | T-SQL 產生器實作（純函數） |
| `src/Specurai.Application/Services/ISchemaMigrationService.cs` | Migration 服務介面 |
| `src/Specurai.Application/Services/SchemaMigrationService.cs` | Migration 服務實作 |
| `src/Specurai.Application/Services/ISchemaMigrationExecutor.cs` | 執行器介面 |
| `src/Specurai.Infrastructure/Services/SchemaMigrationExecutor.cs` | 執行器實作（直接對目標 DB 執行） |
| `src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs` | 差異表格每列 ViewModel |
| `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs` | Migration 主 ViewModel |
| `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml` | Migration UI（DataGrid） |
| `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml.cs` | Code-behind |
| `tests/Specurai.Application.Tests/Services/SqlScriptGeneratorTests.cs` | SqlScriptGenerator 單元測試 |
| `tests/Specurai.Application.Tests/Services/SchemaMigrationServiceTests.cs` | SchemaMigrationService 單元測試 |
| `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs` | ViewModel 單元測試 |

### 修改檔案

| 檔案 | 修改內容 |
|------|---------|
| `src/Specurai.Desktop/Program.cs` | 新增 DI 註冊 |
| `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs` | 新增 `OpenSchemaMigration` 命令 |
| `src/Specurai.Desktop/Views/MainWindow.axaml` | 新增選單項目 |
| `src/Specurai.Infrastructure/ServiceCollectionExtensions.cs` | 新增 `SchemaMigrationExecutor` 註冊 |

---

## Task 1：Domain 實體

**Files:**
- Create: `src/Specurai.Domain/Enums/MigrationLogStatus.cs`
- Create: `src/Specurai.Domain/Entities/SchemaCompare/MigrationLogEntry.cs`
- Create: `src/Specurai.Domain/Entities/SchemaCompare/MigrationReport.cs`
- Create: `src/Specurai.Domain/Entities/SchemaCompare/MigrationAnalysis.cs`

- [ ] **Step 1: 建立 MigrationLogStatus enum**

```csharp
// src/Specurai.Domain/Enums/MigrationLogStatus.cs
namespace Specurai.Domain.Enums;

/// <summary>
/// Migration 執行日誌狀態
/// </summary>
public enum MigrationLogStatus
{
    /// <summary>
    /// 執行成功
    /// </summary>
    Success = 0,

    /// <summary>
    /// 使用者略過（未勾選）
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// 執行失敗
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 高風險，不執行
    /// </summary>
    HighRisk = 3
}
```

- [ ] **Step 2: 建立 MigrationLogEntry**

```csharp
// src/Specurai.Domain/Entities/SchemaCompare/MigrationLogEntry.cs
using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Migration 執行日誌單筆記錄
/// </summary>
public class MigrationLogEntry
{
    /// <summary>
    /// 物件名稱（如 [dbo].[Users]）
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// 執行動作描述（如 ADD COLUMN、CREATE TABLE）
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 執行狀態
    /// </summary>
    public MigrationLogStatus Status { get; set; }

    /// <summary>
    /// 執行耗時（執行成功時才有值）
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// 錯誤訊息（Failed 時才有值）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 備註（如「高風險未執行」、「使用者取消」）
    /// </summary>
    public string? Note { get; set; }
}
```

- [ ] **Step 3: 建立 MigrationReport**

```csharp
// src/Specurai.Domain/Entities/SchemaCompare/MigrationReport.cs
namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Migration 完整執行報告
/// </summary>
public class MigrationReport
{
    /// <summary>
    /// 基準環境名稱
    /// </summary>
    public string BaseEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 目標環境名稱
    /// </summary>
    public string TargetEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 執行時間
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// 總耗時
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// 是否整體成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 整體錯誤訊息（失敗時）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 執行日誌清單
    /// </summary>
    public IList<MigrationLogEntry> Entries { get; set; } = new List<MigrationLogEntry>();

    /// <summary>
    /// 實際執行的 SQL 腳本
    /// </summary>
    public string AppliedScript { get; set; } = string.Empty;

    /// <summary>
    /// 成功執行的筆數
    /// </summary>
    public int SuccessCount => Entries.Count(e => e.Status == Enums.MigrationLogStatus.Success);

    /// <summary>
    /// 略過的筆數（使用者未勾選 + 高風險）
    /// </summary>
    public int SkippedCount => Entries.Count(e =>
        e.Status == Enums.MigrationLogStatus.Skipped ||
        e.Status == Enums.MigrationLogStatus.HighRisk);
}
```

- [ ] **Step 4: 建立 MigrationAnalysis**

```csharp
// src/Specurai.Domain/Entities/SchemaCompare/MigrationAnalysis.cs
using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Schema Migration 分析結果
/// </summary>
public class MigrationAnalysis
{
    /// <summary>
    /// 基準 DatabaseSchema（用於產生 SQL）
    /// </summary>
    public required DatabaseSchema BaseSchema { get; init; }

    /// <summary>
    /// 目標 DatabaseSchema
    /// </summary>
    public required DatabaseSchema TargetSchema { get; init; }

    /// <summary>
    /// 完整比對結果
    /// </summary>
    public required SchemaComparison Comparison { get; init; }

    /// <summary>
    /// 高/禁止風險差異（不可執行，僅顯示報告）
    /// </summary>
    public IReadOnlyList<SchemaDifference> BlockedDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel >= RiskLevel.High)
            .ToList();

    /// <summary>
    /// 中風險差異（需使用者確認才執行）
    /// </summary>
    public IReadOnlyList<SchemaDifference> WarnDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel == RiskLevel.Medium)
            .ToList();

    /// <summary>
    /// 低風險差異（預設勾選）
    /// </summary>
    public IReadOnlyList<SchemaDifference> SafeDifferences =>
        Comparison.Differences
            .Where(d => d.RiskLevel == RiskLevel.Low)
            .ToList();
}
```

- [ ] **Step 5: 執行測試確認無編譯錯誤**

```bash
dotnet build src/Specurai.Domain
```

預期：Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Enums/MigrationLogStatus.cs \
        src/Specurai.Domain/Entities/SchemaCompare/MigrationLogEntry.cs \
        src/Specurai.Domain/Entities/SchemaCompare/MigrationReport.cs \
        src/Specurai.Domain/Entities/SchemaCompare/MigrationAnalysis.cs
git commit -m "feat: 新增 Schema Migration 相關 Domain 實體"
```

---

## Task 2：ISqlScriptGenerator + SqlScriptGenerator

**Files:**
- Create: `src/Specurai.Application/Services/ISqlScriptGenerator.cs`
- Create: `src/Specurai.Application/Services/SqlScriptGenerator.cs`
- Test: `tests/Specurai.Application.Tests/Services/SqlScriptGeneratorTests.cs`

- [ ] **Step 1: 撰寫失敗測試（新增表格）**

```csharp
// tests/Specurai.Application.Tests/Services/SqlScriptGeneratorTests.cs
using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Application.Tests.Services;

public class SqlScriptGeneratorTests
{
    private readonly ISqlScriptGenerator _generator = new SqlScriptGenerator();

    private static DatabaseSchema CreateBaseSchema(string name = "基準環境")
    {
        var schema = new DatabaseSchema { ConnectionName = name };
        var table = new SchemaTable { Schema = "dbo", Name = "Products" };
        table.Columns.Add(new SchemaColumn
        {
            Name = "Id", DataType = "INT", IsNullable = false, IsIdentity = true
        });
        table.Columns.Add(new SchemaColumn
        {
            Name = "Name", DataType = "NVARCHAR", MaxLength = 200, IsNullable = false
        });
        schema.Tables.Add(table);
        return schema;
    }

    [Fact]
    public void Generate_新增表格差異_腳本應包含CREATE_TABLE()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("CREATE TABLE [dbo].[Products]");
        script.ApplyScript.Should().Contain("BEGIN TRANSACTION");
        script.ApplyScript.Should().Contain("COMMIT TRANSACTION");
        script.ApplyScript.Should().Contain("ROLLBACK TRANSACTION");
    }

    [Fact]
    public void Generate_新增欄位差異_腳本應包含ALTER_TABLE_ADD()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Products].[Name]",
            DifferenceType = DifferenceType.Added,
            RiskLevel = RiskLevel.Low
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Products] ADD [Name]");
    }

    [Fact]
    public void Generate_修改欄位長度差異_腳本應包含ALTER_COLUMN()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();
        var diff = new SchemaDifference
        {
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Products].[Name]",
            DifferenceType = DifferenceType.Modified,
            PropertyName = "MaxLength",
            SourceValue = "500",
            RiskLevel = RiskLevel.Medium
        };

        // Act
        var script = _generator.Generate([diff], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().Contain("ALTER TABLE [dbo].[Products] ALTER COLUMN [Name]");
        script.ApplyScript.Should().Contain("NVARCHAR(500)");
    }

    [Fact]
    public void Generate_差異清單為空_應產生空腳本()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();

        // Act
        var script = _generator.Generate([], baseSchema, "基準", "目標");

        // Assert
        script.ApplyScript.Should().NotBeNullOrEmpty();
        script.Differences.Should().BeEmpty();
    }

    [Fact]
    public void Generate_腳本應包含標頭註解()
    {
        // Arrange
        var baseSchema = CreateBaseSchema();

        // Act
        var script = _generator.Generate([], baseSchema, "Production", "Staging");

        // Assert
        script.ApplyScript.Should().Contain("基準環境：Production");
        script.ApplyScript.Should().Contain("目標環境：Staging");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Application.Tests --filter "SqlScriptGeneratorTests" -v minimal
```

預期：FAIL（ISqlScriptGenerator 不存在）

- [ ] **Step 3: 建立 ISqlScriptGenerator 介面**

```csharp
// src/Specurai.Application/Services/ISqlScriptGenerator.cs
using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// T-SQL Migration 腳本產生器（純函數，無 I/O 相依）
/// </summary>
public interface ISqlScriptGenerator
{
    /// <summary>
    /// 根據選取的差異清單產生 T-SQL Migration 腳本
    /// </summary>
    /// <param name="selectedDifferences">使用者選取要執行的差異</param>
    /// <param name="baseSchema">基準 DatabaseSchema（用於查詢完整物件結構）</param>
    /// <param name="baseEnvName">基準環境名稱（用於腳本標頭）</param>
    /// <param name="targetEnvName">目標環境名稱（用於腳本標頭）</param>
    SyncScript Generate(
        IList<SchemaDifference> selectedDifferences,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName);
}
```

- [ ] **Step 4: 實作 SqlScriptGenerator**

```csharp
// src/Specurai.Application/Services/SqlScriptGenerator.cs
using System.Text;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Application.Services;

/// <summary>
/// T-SQL Migration 腳本產生器
/// </summary>
public class SqlScriptGenerator : ISqlScriptGenerator
{
    public SyncScript Generate(
        IList<SchemaDifference> selectedDifferences,
        DatabaseSchema baseSchema,
        string baseEnvName,
        string targetEnvName)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, baseEnvName, targetEnvName);

        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine("BEGIN TRY");
        sb.AppendLine();

        foreach (var diff in selectedDifferences)
        {
            var sql = GenerateSqlForDifference(diff, baseSchema);
            if (!string.IsNullOrWhiteSpace(sql))
            {
                sb.AppendLine($"    -- [{RiskLevelText(diff.RiskLevel)}] {diff.Description ?? diff.ObjectName}");
                sb.AppendLine($"    {sql}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("    COMMIT TRANSACTION;");
        sb.AppendLine("    PRINT N'Migration 成功完成';");
        sb.AppendLine();
        sb.AppendLine("END TRY");
        sb.AppendLine("BEGIN CATCH");
        sb.AppendLine("    ROLLBACK TRANSACTION;");
        sb.AppendLine("    PRINT N'發生錯誤，已自動回滾：' + ERROR_MESSAGE();");
        sb.AppendLine("    THROW;");
        sb.AppendLine("END CATCH;");

        return new SyncScript
        {
            TargetEnvironment = targetEnvName,
            GeneratedAt = DateTime.Now,
            ApplyScript = sb.ToString(),
            Differences = selectedDifferences
        };
    }

    private static void AppendHeader(StringBuilder sb, string baseEnvName, string targetEnvName)
    {
        sb.AppendLine("-- ================================================");
        sb.AppendLine("-- Schema Migration Script");
        sb.AppendLine($"-- 基準環境：{baseEnvName}");
        sb.AppendLine($"-- 目標環境：{targetEnvName}");
        sb.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- ================================================");
        sb.AppendLine();
    }

    private static string GenerateSqlForDifference(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        return diff.ObjectType switch
        {
            SchemaObjectType.Table => GenerateTableSql(diff, baseSchema),
            SchemaObjectType.Column => GenerateColumnSql(diff, baseSchema),
            SchemaObjectType.Index => GenerateIndexSql(diff, baseSchema),
            SchemaObjectType.Constraint => GenerateConstraintSql(diff, baseSchema),
            SchemaObjectType.View => GenerateProgramObjectSql(diff, baseSchema, "VIEW"),
            SchemaObjectType.StoredProcedure => GenerateProgramObjectSql(diff, baseSchema, "PROCEDURE"),
            SchemaObjectType.Function => GenerateProgramObjectSql(diff, baseSchema, "FUNCTION"),
            SchemaObjectType.Trigger => GenerateProgramObjectSql(diff, baseSchema, "TRIGGER"),
            _ => string.Empty
        };
    }

    private static string GenerateTableSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (schema, tableName) = ParseTwoParts(diff.ObjectName);
        var table = baseSchema.GetTable(schema, tableName);
        if (table == null) return $"-- 無法找到表格定義：{diff.ObjectName}";

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{table.Schema}].[{table.Name}] (");

        var columnDefs = new List<string>();
        foreach (var col in table.Columns)
        {
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var identity = col.IsIdentity ? " IDENTITY(1,1)" : string.Empty;
            var defaultVal = string.IsNullOrEmpty(col.DefaultValue)
                ? string.Empty
                : $" DEFAULT {col.DefaultValue}";
            var dataType = col.GetFullDataType();
            var collation = string.IsNullOrEmpty(col.Collation) ? string.Empty : $" COLLATE {col.Collation}";
            columnDefs.Add($"    [{col.Name}] {dataType}{collation}{identity} {nullable}{defaultVal}");
        }

        sb.AppendLine(string.Join(",\n", columnDefs));
        sb.Append(");");
        return sb.ToString();
    }

    private static string GenerateColumnSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        var (schema, tableName, columnName) = ParseThreeParts(diff.ObjectName);
        var table = baseSchema.GetTable(schema, tableName);

        if (diff.DifferenceType == DifferenceType.Added)
        {
            var col = table?.GetColumn(columnName);
            if (col == null) return $"-- 無法找到欄位定義：{diff.ObjectName}";

            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            var defaultVal = string.IsNullOrEmpty(col.DefaultValue)
                ? string.Empty
                : $" DEFAULT {col.DefaultValue}";
            return $"ALTER TABLE [{schema}].[{tableName}] ADD [{col.Name}] {col.GetFullDataType()} {nullable}{defaultVal};";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            var col = table?.GetColumn(columnName);
            if (col == null) return $"-- 無法找到欄位定義：{diff.ObjectName}";

            // 使用 SourceValue 作為新的長度（基準值）
            var newLength = int.TryParse(diff.SourceValue, out var len) ? len : col.MaxLength;
            var dataType = newLength.HasValue ? $"{col.DataType}({newLength})" : col.DataType;
            var nullable = col.IsNullable ? "NULL" : "NOT NULL";
            return $"ALTER TABLE [{schema}].[{tableName}] ALTER COLUMN [{columnName}] {dataType} {nullable};";
        }

        return string.Empty;
    }

    private static string GenerateIndexSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (schema, tableName, indexName) = ParseThreeParts(diff.ObjectName);
        var table = baseSchema.GetTable(schema, tableName);
        var index = table?.Indexes.FirstOrDefault(i =>
            i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase));

        if (index == null) return $"-- 無法找到索引定義：{diff.ObjectName}";

        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var clustered = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
        var columns = string.Join(", ", index.Columns.Select(c => $"[{c}]"));
        var include = index.IncludeColumns.Count > 0
            ? $" INCLUDE ({string.Join(", ", index.IncludeColumns.Select(c => $"[{c}]"))})"
            : string.Empty;
        var filter = string.IsNullOrEmpty(index.FilterDefinition)
            ? string.Empty
            : $" WHERE {index.FilterDefinition}";

        return $"CREATE {unique}{clustered}INDEX [{index.Name}] ON [{schema}].[{tableName}] ({columns}){include}{filter};";
    }

    private static string GenerateConstraintSql(SchemaDifference diff, DatabaseSchema baseSchema)
    {
        if (diff.DifferenceType != DifferenceType.Added)
            return string.Empty;

        var (schema, tableName, constraintName) = ParseThreeParts(diff.ObjectName);
        var table = baseSchema.GetTable(schema, tableName);
        var constraint = table?.Constraints.FirstOrDefault(c =>
            c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase));

        if (constraint == null) return $"-- 無法找到約束定義：{diff.ObjectName}";

        return constraint.ConstraintType switch
        {
            ConstraintType.Unique =>
                $"ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [{constraint.Name}] UNIQUE ({string.Join(", ", constraint.Columns.Select(c => $"[{c}]"))});",
            ConstraintType.Default =>
                $"ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [{constraint.Name}] DEFAULT {constraint.Definition} FOR [{constraint.Columns.FirstOrDefault()}];",
            _ => $"-- 不支援自動產生此約束類型：{constraint.ConstraintType}"
        };
    }

    private static string GenerateProgramObjectSql(SchemaDifference diff, DatabaseSchema baseSchema, string objectTypeSql)
    {
        var (schema, objName) = ParseTwoParts(diff.ObjectName);

        SchemaProgramObject? obj = objectTypeSql switch
        {
            "VIEW" => baseSchema.Views.FirstOrDefault(v =>
                v.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
                v.Name.Equals(objName, StringComparison.OrdinalIgnoreCase)),
            "PROCEDURE" => baseSchema.StoredProcedures.FirstOrDefault(p =>
                p.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
                p.Name.Equals(objName, StringComparison.OrdinalIgnoreCase)),
            "FUNCTION" => baseSchema.Functions.FirstOrDefault(f =>
                f.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
                f.Name.Equals(objName, StringComparison.OrdinalIgnoreCase)),
            "TRIGGER" => baseSchema.Triggers.FirstOrDefault(t =>
                t.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
                t.Name.Equals(objName, StringComparison.OrdinalIgnoreCase)),
            _ => null
        };

        if (obj?.Definition == null)
            return $"-- 無法找到物件定義：{diff.ObjectName}";

        if (diff.DifferenceType == DifferenceType.Added)
        {
            // 確保使用 CREATE
            var def = obj.Definition.Trim();
            if (def.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase))
                def = "CREATE " + def[6..];
            return def + ";";
        }

        if (diff.DifferenceType == DifferenceType.Modified)
        {
            // 確保使用 ALTER
            var def = obj.Definition.Trim();
            if (def.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase))
                def = "ALTER " + def[7..];
            return def + ";";
        }

        return string.Empty;
    }

    // 解析 [schema].[name] → (schema, name)
    private static (string schema, string name) ParseTwoParts(string objectName)
    {
        var clean = objectName.Replace("[", "").Replace("]", "");
        var parts = clean.Split('.');
        return parts.Length >= 2 ? (parts[0], parts[1]) : ("dbo", clean);
    }

    // 解析 [schema].[table].[column] → (schema, table, column)
    private static (string schema, string table, string column) ParseThreeParts(string objectName)
    {
        var clean = objectName.Replace("[", "").Replace("]", "");
        var parts = clean.Split('.');
        return parts.Length >= 3
            ? (parts[0], parts[1], parts[2])
            : ("dbo", parts.Length >= 2 ? parts[0] : string.Empty, parts[^1]);
    }

    private static string RiskLevelText(RiskLevel level) => level switch
    {
        RiskLevel.Low => "低風險",
        RiskLevel.Medium => "中風險",
        _ => "未知"
    };
}
```

- [ ] **Step 5: 確認 SchemaConstraint 有 ConstraintType 和 Definition 屬性**

讀取 `src/Specurai.Domain/Entities/SchemaCompare/SchemaConstraint.cs`，確認 `ConstraintType`、`Columns`、`Definition`、`Name` 屬性存在。若 `Definition` 屬性不存在，在 SchemaConstraint 中新增：

```csharp
public string? Definition { get; set; }
```

- [ ] **Step 6: 執行測試**

```bash
dotnet test tests/Specurai.Application.Tests --filter "SqlScriptGeneratorTests" -v minimal
```

預期：5 tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Application/Services/ISqlScriptGenerator.cs \
        src/Specurai.Application/Services/SqlScriptGenerator.cs \
        tests/Specurai.Application.Tests/Services/SqlScriptGeneratorTests.cs
git commit -m "feat: 新增 SqlScriptGenerator 純函數 T-SQL 腳本產生器"
```

---

## Task 3：ISchemaMigrationService + SchemaMigrationService

**Files:**
- Create: `src/Specurai.Application/Services/ISchemaMigrationService.cs`
- Create: `src/Specurai.Application/Services/SchemaMigrationService.cs`
- Test: `tests/Specurai.Application.Tests/Services/SchemaMigrationServiceTests.cs`

- [ ] **Step 1: 撰寫失敗測試**

```csharp
// tests/Specurai.Application.Tests/Services/SchemaMigrationServiceTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests.Services;

public class SchemaMigrationServiceTests
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _schemaCompareService;
    private readonly ISchemaMigrationService _service;

    public SchemaMigrationServiceTests()
    {
        _schemaCollector = Substitute.For<ISchemaCollector>();
        _schemaCompareService = Substitute.For<ISchemaCompareService>();
        _service = new SchemaMigrationService(_schemaCollector, _schemaCompareService);
    }

    [Fact]
    public async Task AnalyzeAsync_正常呼叫_應回傳MigrationAnalysis()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var comparison = new SchemaComparison
        {
            BaseEnvironment = "基準",
            TargetEnvironment = "目標"
        };

        _schemaCollector.CollectAsync("base-conn", "基準", Arg.Any<CancellationToken>())
            .Returns(baseSchema);
        _schemaCollector.CollectAsync("target-conn", "目標", Arg.Any<CancellationToken>())
            .Returns(targetSchema);
        _schemaCompareService.CompareAsync(baseSchema, targetSchema)
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.Should().NotBeNull();
        result.BaseSchema.Should().Be(baseSchema);
        result.TargetSchema.Should().Be(targetSchema);
        result.Comparison.Should().Be(comparison);
    }

    [Fact]
    public async Task AnalyzeAsync_含高風險差異_應分類到BlockedDifferences()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var highRiskDiff = new SchemaDifference
        {
            RiskLevel = RiskLevel.High,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Email]"
        };
        var comparison = new SchemaComparison
        {
            Differences = new List<SchemaDifference> { highRiskDiff }
        };

        _schemaCollector.CollectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(baseSchema, targetSchema);
        _schemaCompareService.CompareAsync(Arg.Any<DatabaseSchema>(), Arg.Any<DatabaseSchema>())
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.BlockedDifferences.Should().ContainSingle();
        result.WarnDifferences.Should().BeEmpty();
        result.SafeDifferences.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_含低風險差異_應分類到SafeDifferences()
    {
        // Arrange
        var baseSchema = new DatabaseSchema { ConnectionName = "基準" };
        var targetSchema = new DatabaseSchema { ConnectionName = "目標" };
        var lowRiskDiff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Low,
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]"
        };
        var comparison = new SchemaComparison
        {
            Differences = new List<SchemaDifference> { lowRiskDiff }
        };

        _schemaCollector.CollectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(baseSchema, targetSchema);
        _schemaCompareService.CompareAsync(Arg.Any<DatabaseSchema>(), Arg.Any<DatabaseSchema>())
            .Returns(comparison);

        // Act
        var result = await _service.AnalyzeAsync("base-conn", "target-conn", "基準", "目標");

        // Assert
        result.SafeDifferences.Should().ContainSingle();
        result.BlockedDifferences.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Application.Tests --filter "SchemaMigrationServiceTests" -v minimal
```

預期：FAIL（ISchemaMigrationService 不存在）

- [ ] **Step 3: 建立 ISchemaMigrationService 介面**

```csharp
// src/Specurai.Application/Services/ISchemaMigrationService.cs
using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 分析協調服務介面
/// </summary>
public interface ISchemaMigrationService
{
    /// <summary>
    /// 分析基準與目標資料庫的 Schema 差異並進行風險分類
    /// </summary>
    Task<MigrationAnalysis> AnalyzeAsync(
        string baseConnectionString,
        string targetConnectionString,
        string baseEnvName,
        string targetEnvName,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: 實作 SchemaMigrationService**

```csharp
// src/Specurai.Application/Services/SchemaMigrationService.cs
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 分析協調服務實作
/// </summary>
public class SchemaMigrationService : ISchemaMigrationService
{
    private readonly ISchemaCollector _schemaCollector;
    private readonly ISchemaCompareService _schemaCompareService;

    public SchemaMigrationService(
        ISchemaCollector schemaCollector,
        ISchemaCompareService schemaCompareService)
    {
        _schemaCollector = schemaCollector;
        _schemaCompareService = schemaCompareService;
    }

    public async Task<MigrationAnalysis> AnalyzeAsync(
        string baseConnectionString,
        string targetConnectionString,
        string baseEnvName,
        string targetEnvName,
        CancellationToken ct = default)
    {
        var baseSchema = await _schemaCollector.CollectAsync(baseConnectionString, baseEnvName, ct);
        var targetSchema = await _schemaCollector.CollectAsync(targetConnectionString, targetEnvName, ct);
        var comparison = await _schemaCompareService.CompareAsync(baseSchema, targetSchema);

        return new MigrationAnalysis
        {
            BaseSchema = baseSchema,
            TargetSchema = targetSchema,
            Comparison = comparison
        };
    }
}
```

- [ ] **Step 5: 執行測試**

```bash
dotnet test tests/Specurai.Application.Tests --filter "SchemaMigrationServiceTests" -v minimal
```

預期：3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Application/Services/ISchemaMigrationService.cs \
        src/Specurai.Application/Services/SchemaMigrationService.cs \
        tests/Specurai.Application.Tests/Services/SchemaMigrationServiceTests.cs
git commit -m "feat: 新增 SchemaMigrationService 分析協調服務"
```

---

## Task 4：ISchemaMigrationExecutor + SchemaMigrationExecutor

**Files:**
- Create: `src/Specurai.Application/Services/ISchemaMigrationExecutor.cs`
- Create: `src/Specurai.Infrastructure/Services/SchemaMigrationExecutor.cs`

> 注意：`SchemaMigrationExecutor` 位於 Infrastructure 層，因為它需要直接執行 SQL Server 指令。整合測試需要真實 DB，這裡只測試 Application 介面層。

- [ ] **Step 1: 建立 ISchemaMigrationExecutor 介面**

```csharp
// src/Specurai.Application/Services/ISchemaMigrationExecutor.cs
using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 執行器介面
/// </summary>
public interface ISchemaMigrationExecutor
{
    /// <summary>
    /// 執行 Migration 腳本並回傳執行報告
    /// </summary>
    /// <param name="script">要執行的同步腳本</param>
    /// <param name="targetConnectionString">目標資料庫連線字串</param>
    /// <param name="ct">取消權杖</param>
    Task<MigrationReport> ExecuteAsync(
        SyncScript script,
        string targetConnectionString,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: 實作 SchemaMigrationExecutor（Infrastructure 層）**

```csharp
// src/Specurai.Infrastructure/Services/SchemaMigrationExecutor.cs
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// Schema Migration 執行器（直接對 SQL Server 執行）
/// </summary>
public class SchemaMigrationExecutor : ISchemaMigrationExecutor
{
    public async Task<MigrationReport> ExecuteAsync(
        SyncScript script,
        string targetConnectionString,
        CancellationToken ct = default)
    {
        var report = new MigrationReport
        {
            BaseEnvironment = string.Empty,
            TargetEnvironment = script.TargetEnvironment,
            ExecutedAt = DateTime.Now,
            AppliedScript = script.ApplyScript
        };

        // 準備日誌：高風險/Forbidden 列為 HighRisk；選取的列為待執行
        foreach (var diff in script.Differences)
        {
            report.Entries.Add(new MigrationLogEntry
            {
                ObjectName = diff.ObjectName,
                Action = GetActionText(diff),
                Status = MigrationLogStatus.Success, // 稍後若失敗會更新
                Note = null
            });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(script.ApplyScript, connection);
            command.CommandTimeout = 300; // 5 分鐘逾時
            await command.ExecuteNonQueryAsync(ct);

            sw.Stop();
            report.TotalDuration = sw.Elapsed;
            report.IsSuccess = true;

            foreach (var entry in report.Entries)
                entry.Duration = sw.Elapsed / report.Entries.Count; // 平均耗時（整體交易）
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.TotalDuration = sw.Elapsed;
            report.IsSuccess = false;
            report.ErrorMessage = ex.Message;

            foreach (var entry in report.Entries.Where(e => e.Status == MigrationLogStatus.Success))
            {
                entry.Status = MigrationLogStatus.Failed;
                entry.ErrorMessage = ex.Message;
            }
        }

        return report;
    }

    private static string GetActionText(SchemaDifference diff)
    {
        return diff.DifferenceType switch
        {
            DifferenceType.Added => diff.ObjectType switch
            {
                SchemaObjectType.Table => "CREATE TABLE",
                SchemaObjectType.Column => "ADD COLUMN",
                SchemaObjectType.Index => "CREATE INDEX",
                SchemaObjectType.Constraint => "ADD CONSTRAINT",
                SchemaObjectType.View => "CREATE VIEW",
                SchemaObjectType.StoredProcedure => "CREATE PROCEDURE",
                SchemaObjectType.Function => "CREATE FUNCTION",
                SchemaObjectType.Trigger => "CREATE TRIGGER",
                _ => "ADD"
            },
            DifferenceType.Modified => "ALTER " + diff.ObjectType.ToString().ToUpper(),
            _ => diff.DifferenceType.ToString()
        };
    }
}
```

- [ ] **Step 3: 確認編譯通過**

```bash
dotnet build src/Specurai.Infrastructure
```

預期：Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Application/Services/ISchemaMigrationExecutor.cs \
        src/Specurai.Infrastructure/Services/SchemaMigrationExecutor.cs
git commit -m "feat: 新增 SchemaMigrationExecutor 執行器（Infrastructure 層）"
```

---

## Task 5：MigrationDifferenceRowViewModel

**Files:**
- Create: `src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/MigrationDifferenceRowViewModelTests.cs`

- [ ] **Step 1: 撰寫失敗測試**

```csharp
// tests/Specurai.Desktop.Tests/ViewModels/MigrationDifferenceRowViewModelTests.cs
using FluentAssertions;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Desktop.Tests.ViewModels;

public class MigrationDifferenceRowViewModelTests
{
    [Fact]
    public void Constructor_低風險差異_IsExecutable應為True且預設勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Low,
            ObjectType = SchemaObjectType.Table,
            ObjectName = "[dbo].[Products]",
            DifferenceType = DifferenceType.Added
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeTrue();
        vm.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Constructor_中風險差異_IsExecutable應為True且預設勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Medium,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Users].[Phone]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeTrue();
        vm.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Constructor_高風險差異_IsExecutable應為False且不可勾選()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.High,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Orders].[Amount]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeFalse();
        vm.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void Constructor_禁止差異_IsExecutable應為False()
    {
        // Arrange
        var diff = new SchemaDifference
        {
            RiskLevel = RiskLevel.Forbidden,
            ObjectType = SchemaObjectType.Column,
            ObjectName = "[dbo].[Orders].[Id]",
            DifferenceType = DifferenceType.Modified
        };

        // Act
        var vm = new MigrationDifferenceRowViewModel(diff);

        // Assert
        vm.IsExecutable.Should().BeFalse();
    }

    [Fact]
    public void RiskLevelText_各風險等級_應回傳對應中文文字()
    {
        // Arrange & Act & Assert
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Low })
            .RiskLevelText.Should().Be("🟢 低風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Medium })
            .RiskLevelText.Should().Be("🟡 中風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.High })
            .RiskLevelText.Should().Be("🔴 高風險");
        new MigrationDifferenceRowViewModel(new SchemaDifference { RiskLevel = RiskLevel.Forbidden })
            .RiskLevelText.Should().Be("🔴 禁止");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests --filter "MigrationDifferenceRowViewModelTests" -v minimal
```

預期：FAIL

- [ ] **Step 3: 實作 MigrationDifferenceRowViewModel**

```csharp
// src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Migration 差異表格每列 ViewModel
/// </summary>
public partial class MigrationDifferenceRowViewModel : ViewModelBase
{
    public SchemaDifference Difference { get; }

    [ObservableProperty]
    private bool _isSelected;

    public bool IsExecutable => Difference.RiskLevel < RiskLevel.High;

    public string RiskLevelText => Difference.RiskLevel switch
    {
        RiskLevel.Low => "🟢 低風險",
        RiskLevel.Medium => "🟡 中風險",
        RiskLevel.High => "🔴 高風險",
        RiskLevel.Forbidden => "🔴 禁止",
        _ => "未知"
    };

    public string ObjectTypeText => Difference.ObjectType switch
    {
        SchemaObjectType.Table => "表格",
        SchemaObjectType.Column => "欄位",
        SchemaObjectType.Index => "索引",
        SchemaObjectType.Constraint => "約束",
        SchemaObjectType.View => "檢視表",
        SchemaObjectType.StoredProcedure => "預存程序",
        SchemaObjectType.Function => "函數",
        SchemaObjectType.Trigger => "觸發程序",
        _ => Difference.ObjectType.ToString()
    };

    public string DifferenceTypeText => Difference.DifferenceType switch
    {
        DifferenceType.Added => "新增",
        DifferenceType.Modified => Difference.PropertyName ?? "修改",
        _ => Difference.DifferenceType.ToString()
    };

    public MigrationDifferenceRowViewModel(SchemaDifference difference)
    {
        Difference = difference;
        _isSelected = IsExecutable; // 可執行的預設勾選
    }
}
```

- [ ] **Step 4: 執行測試**

```bash
dotnet test tests/Specurai.Desktop.Tests --filter "MigrationDifferenceRowViewModelTests" -v minimal
```

預期：5 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MigrationDifferenceRowViewModel.cs \
        tests/Specurai.Desktop.Tests/ViewModels/MigrationDifferenceRowViewModelTests.cs
git commit -m "feat: 新增 MigrationDifferenceRowViewModel 差異列 ViewModel"
```

---

## Task 6：SchemaMigrationDocumentViewModel

**Files:**
- Create: `src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs`

- [ ] **Step 1: 撰寫失敗測試**

```csharp
// tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.Tests.ViewModels;

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
            new() { Name = "開發環境" },
            new() { Name = "正式環境" }
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
    public void CanExecuteMigration_無選取差異_應回傳False()
    {
        // Arrange
        var vm = new SchemaMigrationDocumentViewModel();

        // Act & Assert
        vm.ExecuteMigrationCommand.CanExecute(null).Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
dotnet test tests/Specurai.Desktop.Tests --filter "SchemaMigrationDocumentViewModelTests" -v minimal
```

預期：FAIL

- [ ] **Step 3: 實作 SchemaMigrationDocumentViewModel**

```csharp
// src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Schema Migration 主 ViewModel
/// </summary>
public partial class SchemaMigrationDocumentViewModel : DocumentViewModel
{
    private readonly ISchemaMigrationService? _migrationService;
    private readonly ISqlScriptGenerator? _scriptGenerator;
    private readonly ISchemaMigrationExecutor? _executor;
    private readonly IConnectionManager? _connectionManager;

    private MigrationAnalysis? _currentAnalysis;

    public override string DocumentType => "SchemaMigration";
    public override string DocumentKey => DocumentType;

    [ObservableProperty]
    private ConnectionProfile? _selectedBaseProfile;

    [ObservableProperty]
    private ConnectionProfile? _selectedTargetProfile;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = "請選擇基準資料庫與目標資料庫";

    [ObservableProperty]
    private MigrationReport? _lastReport;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<MigrationDifferenceRowViewModel> DifferenceRows { get; } = [];

    // 設計時建構函式
    public SchemaMigrationDocumentViewModel()
    {
        Title = "Schema Migration";
        Icon = "🔄";
    }

    public SchemaMigrationDocumentViewModel(
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        ISchemaMigrationExecutor executor,
        IConnectionManager connectionManager)
    {
        Title = "Schema Migration";
        Icon = "🔄";
        _migrationService = migrationService;
        _scriptGenerator = scriptGenerator;
        _executor = connectionManager;
        _connectionManager = connectionManager;

        LoadProfiles();
    }

    private void LoadProfiles()
    {
        ConnectionProfiles.Clear();
        foreach (var profile in _connectionManager?.GetAllProfiles() ?? [])
            ConnectionProfiles.Add(profile);
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (_migrationService == null || SelectedBaseProfile == null || SelectedTargetProfile == null)
            return;

        IsAnalyzing = true;
        StatusMessage = "正在分析 Schema 差異...";
        DifferenceRows.Clear();

        try
        {
            var baseConn = SelectedBaseProfile.GetConnectionString();
            var targetConn = SelectedTargetProfile.GetConnectionString();

            _currentAnalysis = await _migrationService.AnalyzeAsync(
                baseConn, targetConn,
                SelectedBaseProfile.Name, SelectedTargetProfile.Name);

            foreach (var diff in _currentAnalysis.Comparison.Differences)
                DifferenceRows.Add(new MigrationDifferenceRowViewModel(diff));

            var total = DifferenceRows.Count;
            var blocked = _currentAnalysis.BlockedDifferences.Count;
            StatusMessage = $"分析完成：共 {total} 項差異，其中 {blocked} 項高風險（不可執行）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分析失敗：{ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
            AnalyzeCommand.NotifyCanExecuteChanged();
            ExecuteMigrationCommand.NotifyCanExecuteChanged();
            PreviewSqlCommand.NotifyCanExecuteChanged();
            DownloadSqlCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanAnalyze() =>
        !IsAnalyzing && !IsExecuting &&
        SelectedBaseProfile != null && SelectedTargetProfile != null &&
        SelectedBaseProfile != SelectedTargetProfile;

    partial void OnSelectedBaseProfileChanged(ConnectionProfile? value)
        => AnalyzeCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTargetProfileChanged(ConnectionProfile? value)
        => AnalyzeCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanExecuteMigration))]
    private async Task ExecuteMigrationAsync()
    {
        if (_executor == null || _scriptGenerator == null || _currentAnalysis == null)
            return;

        var selected = DifferenceRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        if (selected.Count == 0)
        {
            StatusMessage = "未選取任何可執行的差異項目";
            return;
        }

        IsExecuting = true;
        StatusMessage = "正在執行 Migration...";

        try
        {
            var script = _scriptGenerator.Generate(
                selected,
                _currentAnalysis.BaseSchema,
                _currentAnalysis.BaseSchema.ConnectionName,
                _currentAnalysis.TargetSchema.ConnectionName);

            var targetConn = SelectedTargetProfile!.GetConnectionString();
            LastReport = await _executor.ExecuteAsync(script, targetConn);

            StatusMessage = LastReport.IsSuccess
                ? $"Migration 完成：{LastReport.SuccessCount} 項成功，{LastReport.SkippedCount} 項略過"
                : $"Migration 失敗：{LastReport.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private bool CanExecuteMigration() =>
        !IsAnalyzing && !IsExecuting &&
        DifferenceRows.Any(r => r.IsSelected && r.IsExecutable);

    [RelayCommand(CanExecute = nameof(CanGenerateScript))]
    private void PreviewSql()
    {
        if (_scriptGenerator == null || _currentAnalysis == null) return;

        var selected = DifferenceRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        var script = _scriptGenerator.Generate(
            selected,
            _currentAnalysis.BaseSchema,
            _currentAnalysis.BaseSchema.ConnectionName,
            _currentAnalysis.TargetSchema.ConnectionName);

        StatusMessage = $"腳本預覽（{script.ApplyScript.Length} 字元）";
    }

    [RelayCommand(CanExecute = nameof(CanGenerateScript))]
    private async Task DownloadSqlAsync()
    {
        if (_scriptGenerator == null || _currentAnalysis == null) return;

        var selected = DifferenceRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        var script = _scriptGenerator.Generate(
            selected,
            _currentAnalysis.BaseSchema,
            _currentAnalysis.BaseSchema.ConnectionName,
            _currentAnalysis.TargetSchema.ConnectionName);

        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存 Migration SQL",
            SuggestedFileName = $"migration_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            FileTypeChoices = [new FilePickerFileType("SQL 檔案") { Patterns = ["*.sql"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(script.ApplyScript);
            StatusMessage = "SQL 腳本已儲存";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task DownloadReportAsync()
    {
        if (LastReport == null) return;

        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存執行報告",
            SuggestedFileName = $"migration_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("文字檔案") { Patterns = ["*.txt"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync($"Migration 執行報告");
            await writer.WriteLineAsync($"目標環境：{LastReport.TargetEnvironment}");
            await writer.WriteLineAsync($"執行時間：{LastReport.ExecutedAt:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync($"總耗時：{LastReport.TotalDuration.TotalSeconds:F2} 秒");
            await writer.WriteLineAsync($"結果：{(LastReport.IsSuccess ? "成功" : "失敗")}");
            await writer.WriteLineAsync(new string('-', 60));
            foreach (var entry in LastReport.Entries)
            {
                var duration = entry.Duration.HasValue ? $"{entry.Duration.Value.TotalMilliseconds:F0}ms" : "-";
                var status = entry.Status switch
                {
                    MigrationLogStatus.Success => "✅",
                    MigrationLogStatus.Failed => "❌",
                    MigrationLogStatus.Skipped => "⏭️",
                    MigrationLogStatus.HighRisk => "⚠️",
                    _ => "?"
                };
                await writer.WriteLineAsync($"{status} {entry.ObjectName} | {entry.Action} | {duration} | {entry.Note ?? entry.ErrorMessage ?? ""}");
            }
            StatusMessage = "報告已儲存";
        }
    }

    private bool CanGenerateScript() =>
        _currentAnalysis != null && DifferenceRows.Any(r => r.IsSelected && r.IsExecutable);

    private bool CanExportReport() => LastReport != null;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in DifferenceRows.Where(r => r.IsExecutable))
            row.IsSelected = true;
        ExecuteMigrationCommand.NotifyCanExecuteChanged();
        PreviewSqlCommand.NotifyCanExecuteChanged();
        DownloadSqlCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var row in DifferenceRows.Where(r => r.IsExecutable))
            row.IsSelected = false;
        ExecuteMigrationCommand.NotifyCanExecuteChanged();
        PreviewSqlCommand.NotifyCanExecuteChanged();
        DownloadSqlCommand.NotifyCanExecuteChanged();
    }
}
```

> **注意**：`SelectedTargetProfile!.GetConnectionString()` 需確認 `ConnectionProfile` 有此方法。若無，替換為 `ConnectionStringHelper.Build(SelectedTargetProfile)` 或查看現有 ViewModel 如何取得連線字串。

- [ ] **Step 4: 修正建構函式中 `_executor` 賦值錯誤**

Step 3 程式碼中 `_executor = connectionManager;` 是錯誤的，應修正為：

```csharp
_executor = executor;
```

- [ ] **Step 5: 執行測試**

```bash
dotnet test tests/Specurai.Desktop.Tests --filter "SchemaMigrationDocumentViewModelTests" -v minimal
```

預期：3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SchemaMigrationDocumentViewModel.cs \
        tests/Specurai.Desktop.Tests/ViewModels/SchemaMigrationDocumentViewModelTests.cs
git commit -m "feat: 新增 SchemaMigrationDocumentViewModel 主 ViewModel"
```

---

## Task 7：SchemaMigrationDocumentView.axaml（UI）

**Files:**
- Create: `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml`
- Create: `src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml.cs`

- [ ] **Step 1: 建立 code-behind**

```csharp
// src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml.cs
using Avalonia.Controls;

namespace Specurai.Desktop.Views;

public partial class SchemaMigrationDocumentView : UserControl
{
    public SchemaMigrationDocumentView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: 建立 AXAML 主 UI**

```xml
<!-- src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Specurai.Desktop.ViewModels"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="750"
             x:Class="Specurai.Desktop.Views.SchemaMigrationDocumentView"
             x:DataType="vm:SchemaMigrationDocumentViewModel">

    <Design.DataContext>
        <vm:SchemaMigrationDocumentViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,Auto,*,Auto,Auto">

        <!-- 工具列：連線選擇 + 操作按鈕 -->
        <Border Grid.Row="0"
                Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}"
                Padding="10,8">
            <WrapPanel Orientation="Horizontal" ItemWidth="Auto">
                <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,0,16,0">
                    <TextBlock Text="基準資料庫：" VerticalAlignment="Center"/>
                    <ComboBox ItemsSource="{Binding ConnectionProfiles}"
                              SelectedItem="{Binding SelectedBaseProfile}"
                              DisplayMemberBinding="{Binding Name}"
                              MinWidth="180"/>
                </StackPanel>

                <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,0,16,0">
                    <TextBlock Text="目標資料庫：" VerticalAlignment="Center"/>
                    <ComboBox ItemsSource="{Binding ConnectionProfiles}"
                              SelectedItem="{Binding SelectedTargetProfile}"
                              DisplayMemberBinding="{Binding Name}"
                              MinWidth="180"/>
                </StackPanel>

                <Button Command="{Binding AnalyzeCommand}"
                        IsEnabled="{Binding !IsAnalyzing}"
                        Margin="0,0,8,0">
                    <StackPanel Orientation="Horizontal" Spacing="5">
                        <TextBlock Text="🔍"/>
                        <TextBlock Text="分析差異"/>
                    </StackPanel>
                </Button>

                <Separator Margin="0,0,8,0"/>

                <Button Command="{Binding PreviewSqlCommand}" Margin="0,0,4,0"
                        ToolTip.Tip="預覽將執行的 T-SQL 腳本">
                    <TextBlock Text="預覽 SQL"/>
                </Button>
                <Button Command="{Binding DownloadSqlCommand}" Margin="0,0,4,0"
                        ToolTip.Tip="下載 .sql 腳本檔案">
                    <TextBlock Text="⬇️ 下載 SQL"/>
                </Button>
                <Button Command="{Binding ExecuteMigrationCommand}"
                        IsEnabled="{Binding !IsExecuting}"
                        ToolTip.Tip="對目標資料庫執行 Migration">
                    <StackPanel Orientation="Horizontal" Spacing="5">
                        <TextBlock Text="▶"/>
                        <TextBlock Text="執行 Migration"/>
                    </StackPanel>
                </Button>
            </WrapPanel>
        </Border>

        <!-- 全選/取消全選 -->
        <Border Grid.Row="1" Padding="10,4"
                Background="{DynamicResource SystemControlBackgroundAltHighBrush}">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="勾選：" VerticalAlignment="Center" FontSize="12"/>
                <Button Command="{Binding SelectAllCommand}" FontSize="12" Padding="6,2">
                    <TextBlock Text="全選"/>
                </Button>
                <Button Command="{Binding DeselectAllCommand}" FontSize="12" Padding="6,2">
                    <TextBlock Text="取消全選"/>
                </Button>
            </StackPanel>
        </Border>

        <!-- 差異分析表格 -->
        <DataGrid Grid.Row="2"
                  ItemsSource="{Binding DifferenceRows}"
                  AutoGenerateColumns="False"
                  CanUserSortColumns="True"
                  CanUserResizeColumns="True"
                  IsReadOnly="False"
                  GridLinesVisibility="Horizontal">
            <DataGrid.Columns>
                <!-- 執行（勾選框）-->
                <DataGridTemplateColumn Header="執行" Width="60" CanUserSort="False">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate x:DataType="vm:MigrationDifferenceRowViewModel">
                            <CheckBox IsChecked="{Binding IsSelected}"
                                      IsEnabled="{Binding IsExecutable}"
                                      HorizontalAlignment="Center"
                                      VerticalAlignment="Center"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>

                <!-- 風險 -->
                <DataGridTextColumn Header="風險"
                                    Binding="{Binding RiskLevelText}"
                                    Width="90"
                                    IsReadOnly="True"/>

                <!-- 物件類型 -->
                <DataGridTextColumn Header="物件類型"
                                    Binding="{Binding ObjectTypeText}"
                                    Width="90"
                                    IsReadOnly="True"/>

                <!-- 物件名稱 -->
                <DataGridTextColumn Header="物件名稱"
                                    Binding="{Binding Difference.ObjectName}"
                                    Width="220"
                                    IsReadOnly="True"/>

                <!-- 差異類型 -->
                <DataGridTextColumn Header="差異類型"
                                    Binding="{Binding DifferenceTypeText}"
                                    Width="110"
                                    IsReadOnly="True"/>

                <!-- 基準值 -->
                <DataGridTextColumn Header="基準值"
                                    Binding="{Binding Difference.SourceValue}"
                                    Width="120"
                                    IsReadOnly="True"/>

                <!-- 目標值 -->
                <DataGridTextColumn Header="目標值"
                                    Binding="{Binding Difference.TargetValue}"
                                    Width="120"
                                    IsReadOnly="True"/>

                <!-- 說明 -->
                <DataGridTextColumn Header="說明"
                                    Binding="{Binding Difference.Description}"
                                    Width="*"
                                    IsReadOnly="True"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- 執行報告表格（有報告時顯示）-->
        <Border Grid.Row="3"
                IsVisible="{Binding LastReport, Converter={x:Static ObjectConverters.IsNotNull}}"
                BorderBrush="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                BorderThickness="0,1,0,0">
            <Grid RowDefinitions="Auto,*" MaxHeight="200">
                <Border Grid.Row="0" Padding="10,6"
                        Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}">
                    <StackPanel Orientation="Horizontal" Spacing="12">
                        <TextBlock Text="📋 執行報告" FontWeight="Bold"/>
                        <Button Command="{Binding DownloadReportCommand}" FontSize="12" Padding="6,2">
                            <TextBlock Text="⬇️ 下載報告"/>
                        </Button>
                    </StackPanel>
                </Border>
                <DataGrid Grid.Row="1"
                          ItemsSource="{Binding LastReport.Entries}"
                          AutoGenerateColumns="False"
                          IsReadOnly="True"
                          CanUserSortColumns="False"
                          GridLinesVisibility="Horizontal">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="80"/>
                        <DataGridTextColumn Header="物件名稱" Binding="{Binding ObjectName}" Width="220"/>
                        <DataGridTextColumn Header="動作" Binding="{Binding Action}" Width="120"/>
                        <DataGridTextColumn Header="耗時" Binding="{Binding Duration}" Width="80"/>
                        <DataGridTextColumn Header="備註" Binding="{Binding Note}" Width="*"/>
                    </DataGrid.Columns>
                </DataGrid>
            </Grid>
        </Border>

        <!-- 狀態列 -->
        <Border Grid.Row="4"
                Padding="10,5"
                Background="{DynamicResource SystemControlBackgroundChromeLowBrush}">
            <StackPanel Orientation="Horizontal" Spacing="10">
                <ProgressBar IsIndeterminate="True"
                             IsVisible="{Binding IsAnalyzing}"
                             Width="100" Height="8"/>
                <ProgressBar IsIndeterminate="True"
                             IsVisible="{Binding IsExecuting}"
                             Width="100" Height="8"/>
                <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" FontSize="12"/>
            </StackPanel>
        </Border>

    </Grid>
</UserControl>
```

- [ ] **Step 3: 確認編譯通過**

```bash
dotnet build src/Specurai.Desktop
```

預期：Build succeeded（若有 AXAML 編譯錯誤依訊息修正）

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml \
        src/Specurai.Desktop/Views/SchemaMigrationDocumentView.axaml.cs
git commit -m "feat: 新增 SchemaMigrationDocumentView AXAML 介面"
```

---

## Task 8：DI 註冊 + 選單入口

**Files:**
- Modify: `src/Specurai.Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `src/Specurai.Desktop/Program.cs`
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Specurai.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1: 確認 ServiceCollectionExtensions.cs 路徑與現有寫法**

```bash
cat src/Specurai.Infrastructure/ServiceCollectionExtensions.cs
```

確認 `AddSpecuraiCore()` 中如何註冊服務，找到合適的插入點。

- [ ] **Step 2: 在 Infrastructure 中註冊 SchemaMigrationExecutor**

開啟 `src/Specurai.Infrastructure/ServiceCollectionExtensions.cs`，在 `AddSpecuraiCore()` 方法中新增：

```csharp
services.AddSingleton<ISchemaMigrationExecutor, SchemaMigrationExecutor>();
```

同時在頂部新增 using（若尚未有）：
```csharp
using Specurai.Application.Services;
using Specurai.Infrastructure.Services;
```

- [ ] **Step 3: 在 Program.cs 中註冊 Application 服務與 ViewModel**

開啟 `src/Specurai.Desktop/Program.cs`，在 `ConfigureServices()` 中新增：

```csharp
// Application 服務
services.AddSingleton<ISqlScriptGenerator, SqlScriptGenerator>();
services.AddSingleton<ISchemaMigrationService, SchemaMigrationService>();

// ViewModel
services.AddTransient<SchemaMigrationDocumentViewModel>(sp =>
    new SchemaMigrationDocumentViewModel(
        sp.GetRequiredService<ISchemaMigrationService>(),
        sp.GetRequiredService<ISqlScriptGenerator>(),
        sp.GetRequiredService<ISchemaMigrationExecutor>(),
        sp.GetRequiredService<IConnectionManager>()));
```

同時在頂部新增 using（若尚未有）：
```csharp
using Specurai.Application.Services;
```

- [ ] **Step 4: 在 MainWindowViewModel 新增 OpenSchemaMigration 命令**

開啟 `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`，在 `OpenSchemaCompare()` 方法下方新增：

```csharp
[RelayCommand]
private void OpenSchemaMigration()
{
    var existing = Documents.OfType<SchemaMigrationDocumentViewModel>().FirstOrDefault();
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var doc = App.Services?.GetRequiredService<SchemaMigrationDocumentViewModel>()
        ?? new SchemaMigrationDocumentViewModel();
    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
}
```

- [ ] **Step 5: 在 MainWindow.axaml 新增選單項目**

開啟 `src/Specurai.Desktop/Views/MainWindow.axaml`，找到 SchemaCompare 的選單項目（或工具列按鈕），在其下方新增：

```xml
<MenuItem Header="Schema Migration"
          Command="{Binding OpenSchemaMigrationCommand}"
          ToolTip.Tip="以基準資料庫為標準，同步目標資料庫 Schema"/>
```

- [ ] **Step 6: 全部建置並執行測試**

```bash
dotnet build
dotnet test
```

預期：Build succeeded，所有測試通過

- [ ] **Step 7: 執行應用程式確認 UI 可開啟**

```bash
dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj
```

開啟選單，點選「Schema Migration」，確認畫面正常顯示。

- [ ] **Step 8: Commit**

```bash
git add src/Specurai.Infrastructure/ServiceCollectionExtensions.cs \
        src/Specurai.Desktop/Program.cs \
        src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs \
        src/Specurai.Desktop/Views/MainWindow.axaml
git commit -m "feat: 完成 Schema Migration 功能 DI 註冊與選單整合"
```

---

## 自我審查

### Spec 覆蓋確認

| 需求 | 對應 Task |
|------|-----------|
| 基準 vs 目標差異分析 | Task 3 SchemaMigrationService |
| 三層風險分類（🔴🟡🟢） | Task 1 MigrationAnalysis + Task 5 RowViewModel |
| T-SQL + 交易包裝腳本 | Task 2 SqlScriptGenerator |
| 表格式差異顯示 | Task 7 DataGrid |
| 勾選框（執行欄第一欄） | Task 7 DataGrid 第一欄 |
| 直接執行 Migration | Task 4 Executor + Task 6 ViewModel |
| 下載 .sql 腳本 | Task 6 DownloadSqlCommand |
| 下載執行日誌 | Task 6 DownloadReportCommand |
| 高風險不可執行 | Task 5 IsExecutable |
| 執行失敗自動 ROLLBACK | Task 4 SchemaMigrationExecutor（交易在 SQL 腳本內）|
| 連線失敗顯示錯誤 | Task 6 AnalyzeAsync try/catch |

### 型態一致性確認

- `MigrationAnalysis.BlockedDifferences` → `IReadOnlyList<SchemaDifference>` ✅
- `ISqlScriptGenerator.Generate(IList<SchemaDifference>, DatabaseSchema, string, string)` → `SyncScript` ✅
- `ISchemaMigrationExecutor.ExecuteAsync(SyncScript, string)` → `MigrationReport` ✅
- `MigrationDifferenceRowViewModel.Difference` → `SchemaDifference` ✅
