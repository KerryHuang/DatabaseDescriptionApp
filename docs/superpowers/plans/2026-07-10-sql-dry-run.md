# SQL Dry Run 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Desktop APP、MCP、CLI 三個介面新增 SQL Dry Run 能力：驗證單一 DML（INSERT/UPDATE/DELETE）語法、在交易中執行以取得影響筆數與前後資料對照，最後一律 ROLLBACK，絕不修改資料。

**Architecture:** Clean Architecture。Domain 新增 `DryRunResult` 實體與 `ISqlDryRunRepository` 介面；Infrastructure 新增 `SqlDryRunAnalyzer`（ScriptDom 純解析，無 DB 相依）與 `SqlDryRunRepository`（交易執行）；三個呈現層各自接上。設計文件：`docs/superpowers/specs/2026-07-10-sql-dry-run-design.md`。

**Tech Stack:** .NET 8、Microsoft.SqlServer.TransactSql.ScriptDom（T-SQL 解析）、Microsoft.Data.SqlClient、Dapper、xUnit + NSubstitute + FluentAssertions、Avalonia + CommunityToolkit.Mvvm、System.CommandLine + Spectre.Console、MCP SDK。

## Global Constraints

- 一律以繁體中文撰寫 UI 文字、註解、Commit 訊息
- Clean Architecture 分層：Domain 無相依；Application 只依 Domain；Infrastructure 依 Domain + Application；禁止反向引用
- Repository 介面放 `Specurai.Domain/Interfaces/`，實作放 `Specurai.Infrastructure/Repositories/`，外部服務實作放 `Specurai.Infrastructure/Services/`
- Repository 使用 `Func<string?>` 連線字串工廠模式；共用 DI 註冊在 `src/Specurai.Infrastructure/ServiceRegistration.cs` 的 `AddSpecuraiCore()`
- 實體使用 `required` + `init` 屬性，集合預設 `[]`
- ViewModel 使用 `[ObservableProperty]`、`[RelayCommand]`，必須有無參數設計時建構子
- 測試命名 `[Method]_[Condition]_[Expected]` 或繁中描述，xUnit + NSubstitute + FluentAssertions，TDD（先寫測試）
- 檔案 UTF-8 無 BOM
- **現有唯讀卡控完全不動**（`execute_readonly_sql`、`sql query`、桌面查詢），dry run 是獨立入口
- Dry Run 常數：預覽上限 100 筆、CommandTimeout 30 秒、僅允許恰好一句 INSERT/UPDATE/DELETE

---

### Task 1: Domain 實體 `DryRunResult` 與介面 `ISqlDryRunRepository`

**Files:**
- Create: `src/Specurai.Domain/Entities/DryRunResult.cs`
- Create: `src/Specurai.Domain/Interfaces/ISqlDryRunRepository.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/DryRunResultTests.cs`

**Interfaces:**
- Consumes: 無（最內層）
- Produces:
  - `enum DryRunStatementType { Unknown, Insert, Update, Delete }`
  - `class DryRunSyntaxError { required int Line; required int Column; required string Message }`
  - `class DryRunResult`（欄位見下方程式碼）
  - `interface ISqlDryRunRepository { Task<DryRunResult> DryRunAsync(string sql, CancellationToken ct = default); Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default); }`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Domain.Tests/Entities/DryRunResultTests.cs`：

```csharp
using System.Data;
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class DryRunResultTests
{
    [Fact(DisplayName = "DryRunResult: 集合屬性預設應為空集合")]
    public void DryRunResult_Default_CollectionsShouldBeEmpty()
    {
        var result = new DryRunResult { IsValid = true };

        result.SyntaxErrors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "DryRunResult: 預設值應為 Unknown 類型、無預覽、無錯誤")]
    public void DryRunResult_Default_ShouldHaveExpectedDefaults()
    {
        var result = new DryRunResult { IsValid = false };

        result.StatementType.Should().Be(DryRunStatementType.Unknown);
        result.AffectedRowCount.Should().Be(0);
        result.PreviewTable.Should().BeNull();
        result.PreviewTruncated.Should().BeFalse();
        result.RejectReason.Should().BeNull();
        result.ExecutionError.Should().BeNull();
    }

    [Fact(DisplayName = "DryRunResult: 所有屬性應可透過 init 設定")]
    public void DryRunResult_InitProperties_ShouldBeSettable()
    {
        var table = new DataTable();
        var result = new DryRunResult
        {
            IsValid = true,
            StatementType = DryRunStatementType.Update,
            SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 5, Message = "錯誤" }],
            AffectedRowCount = 3,
            PreviewTable = table,
            PreviewTruncated = true,
            Warnings = ["警告"],
            ExecutionError = "失敗",
            RejectReason = "原因"
        };

        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.SyntaxErrors.Should().ContainSingle().Which.Message.Should().Be("錯誤");
        result.SyntaxErrors[0].Line.Should().Be(1);
        result.SyntaxErrors[0].Column.Should().Be(5);
        result.AffectedRowCount.Should().Be(3);
        result.PreviewTable.Should().BeSameAs(table);
        result.PreviewTruncated.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be("警告");
        result.ExecutionError.Should().Be("失敗");
        result.RejectReason.Should().Be("原因");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~DryRunResultTests"`
Expected: 編譯失敗（`DryRunResult` 型別不存在）

- [ ] **Step 3: 建立 Domain 實體**

建立 `src/Specurai.Domain/Entities/DryRunResult.cs`：

```csharp
using System.Data;

namespace Specurai.Domain.Entities;

/// <summary>
/// Dry Run 陳述式類型
/// </summary>
public enum DryRunStatementType
{
    Unknown,
    Insert,
    Update,
    Delete
}

/// <summary>
/// Dry Run 語法錯誤明細
/// </summary>
public class DryRunSyntaxError
{
    /// <summary>錯誤所在行（1 起算）</summary>
    public required int Line { get; init; }

    /// <summary>錯誤所在列（1 起算）</summary>
    public required int Column { get; init; }

    /// <summary>錯誤訊息</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Dry Run 預演結果（永遠回滾，不會修改資料）
/// </summary>
public class DryRunResult
{
    /// <summary>語法與分類驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>陳述式類型</summary>
    public DryRunStatementType StatementType { get; init; } = DryRunStatementType.Unknown;

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（多語句、非 DML 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>影響筆數</summary>
    public int AffectedRowCount { get; init; }

    /// <summary>前後資料對照（無法提供時為 null，如 trigger fallback）</summary>
    public DataTable? PreviewTable { get; init; }

    /// <summary>預覽是否被截斷（影響筆數超過預覽上限 100 筆）</summary>
    public bool PreviewTruncated { get; init; }

    /// <summary>警告清單（IDENTITY 消耗、trigger fallback 等）</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>語法正確但實際執行會失敗時的錯誤訊息（如違反 FK 約束）</summary>
    public string? ExecutionError { get; init; }
}
```

建立 `src/Specurai.Domain/Interfaces/ISqlDryRunRepository.cs`：

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL Dry Run Repository 介面：預演單一 DML（INSERT/UPDATE/DELETE），
/// 在交易中執行以取得影響筆數與前後資料對照，最後一律 ROLLBACK，絕不修改資料。
/// </summary>
public interface ISqlDryRunRepository
{
    /// <summary>
    /// 使用預設連線預演單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> DryRunAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 使用指定連線字串預演單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default);
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~DryRunResultTests"`
Expected: PASS（3 個測試）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Domain/Entities/DryRunResult.cs src/Specurai.Domain/Interfaces/ISqlDryRunRepository.cs tests/Specurai.Domain.Tests/Entities/DryRunResultTests.cs
git commit -m "feat: 新增 SQL Dry Run 的 Domain 實體與 Repository 介面

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `SqlDryRunAnalyzer.Analyze`（ScriptDom 解析、驗證、分類）

**Files:**
- Modify: `src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`（加 ScriptDom 套件）
- Create: `src/Specurai.Infrastructure/Services/SqlDryRunAnalysis.cs`
- Create: `src/Specurai.Infrastructure/Services/SqlDryRunAnalyzer.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/SqlDryRunAnalyzerTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `DryRunStatementType`、`DryRunSyntaxError`
- Produces:
  - `class SqlDryRunAnalysis { required bool IsValid; DryRunStatementType StatementType; IReadOnlyList<DryRunSyntaxError> SyntaxErrors; string? RejectReason; string? TargetSchema; string? TargetTable; bool HasUserOutputClause }`
  - `class SqlDryRunAnalyzer { SqlDryRunAnalysis Analyze(string sql) }`（Task 3 會再加 `RewriteWithOutput`）

- [ ] **Step 1: 加入 NuGet 套件**

在 `src/Specurai.Infrastructure/Specurai.Infrastructure.csproj` 的 `<ItemGroup>`（PackageReference 區塊）加入：

```xml
<PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="161.9142.1" />
```

Run: `dotnet restore src/Specurai.Infrastructure/Specurai.Infrastructure.csproj`
Expected: 還原成功。若該版本不存在，改跑 `dotnet add src/Specurai.Infrastructure package Microsoft.SqlServer.TransactSql.ScriptDom` 取得最新穩定版。

- [ ] **Step 2: 寫失敗測試**

建立 `tests/Specurai.Infrastructure.Tests/Services/SqlDryRunAnalyzerTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class SqlDryRunAnalyzerTests
{
    private readonly SqlDryRunAnalyzer _analyzer = new();

    [Fact(DisplayName = "Analyze: 合法 INSERT 應通過並分類為 Insert")]
    public void Analyze_ValidInsert_ShouldBeValidInsert()
    {
        var result = _analyzer.Analyze("INSERT INTO dbo.Users (Name) VALUES (N'測試')");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Insert);
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 合法 UPDATE 應通過並分類為 Update")]
    public void Analyze_ValidUpdate_ShouldBeValidUpdate()
    {
        var result = _analyzer.Analyze("UPDATE Users SET Name = N'新名' WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.TargetSchema.Should().BeNull();
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 合法 DELETE 應通過並分類為 Delete")]
    public void Analyze_ValidDelete_ShouldBeValidDelete()
    {
        var result = _analyzer.Analyze("DELETE FROM dbo.Users WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Delete);
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 註解開頭的 DML 應通過（現有前綴檢查會誤擋的情況）")]
    public void Analyze_DmlWithLeadingComment_ShouldBeValid()
    {
        var result = _analyzer.Analyze("-- 調整名稱\nUPDATE Users SET Name = N'x' WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
    }

    [Fact(DisplayName = "Analyze: CTE 包裝的 UPDATE 應通過，但目標表無法解析為 null")]
    public void Analyze_CteUpdate_ShouldBeValidWithNullTarget()
    {
        var sql = "WITH cte AS (SELECT * FROM Users WHERE Id < 10) UPDATE cte SET Name = N'x'";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.TargetTable.Should().BeNull();
    }

    [Fact(DisplayName = "Analyze: UPDATE 別名目標應解析回 FROM 子句中的實際資料表")]
    public void Analyze_UpdateWithAliasTarget_ShouldResolveActualTable()
    {
        var sql = "UPDATE u SET u.Name = N'x' FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id WHERE o.Id = 5";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: SELECT 應被拒絕")]
    public void Analyze_Select_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("SELECT * FROM Users");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Theory(DisplayName = "Analyze: DDL/TRUNCATE/EXEC 應被拒絕")]
    [InlineData("DROP TABLE Users")]
    [InlineData("TRUNCATE TABLE Users")]
    [InlineData("EXEC sp_help")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("ALTER TABLE Users ADD C INT")]
    public void Analyze_NonDml_ShouldBeRejected(string sql)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Fact(DisplayName = "Analyze: 多個陳述式應被拒絕")]
    public void Analyze_MultipleStatements_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("DELETE FROM A WHERE Id = 1; DELETE FROM B WHERE Id = 2;");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅允許單一");
    }

    [Fact(DisplayName = "Analyze: 空白輸入應被拒絕")]
    public void Analyze_EmptyInput_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("   ");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未偵測到");
    }

    [Fact(DisplayName = "Analyze: 語法錯誤應回報行列位置")]
    public void Analyze_SyntaxError_ShouldReportLineAndColumn()
    {
        var result = _analyzer.Analyze("UPDATE Users SET WHERE Id = 1");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
        result.SyntaxErrors[0].Line.Should().BeGreaterThan(0);
        result.SyntaxErrors[0].Column.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Analyze: INSERT ... EXEC 應被拒絕")]
    public void Analyze_InsertExec_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("INSERT INTO T EXEC dbo.SomeProc");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("INSERT ... EXEC");
    }

    [Fact(DisplayName = "Analyze: 字串常值內含 DELETE 關鍵字的 INSERT 應通過")]
    public void Analyze_KeywordInsideStringLiteral_ShouldBeValid()
    {
        var result = _analyzer.Analyze("INSERT INTO Logs (Message) VALUES (N'DELETE FROM X 已執行')");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Insert);
    }

    [Fact(DisplayName = "Analyze: 使用者已自帶 OUTPUT 子句應標記 HasUserOutputClause")]
    public void Analyze_UserOutputClause_ShouldBeFlagged()
    {
        var result = _analyzer.Analyze("DELETE FROM Users OUTPUT deleted.* WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.HasUserOutputClause.Should().BeTrue();
    }

    [Fact(DisplayName = "Analyze: 未帶 OUTPUT 子句 HasUserOutputClause 應為 false")]
    public void Analyze_NoOutputClause_ShouldNotBeFlagged()
    {
        var result = _analyzer.Analyze("DELETE FROM Users WHERE Id = 1");

        result.HasUserOutputClause.Should().BeFalse();
    }
}
```

- [ ] **Step 3: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunAnalyzerTests"`
Expected: 編譯失敗（`SqlDryRunAnalyzer` 不存在）

- [ ] **Step 4: 實作 Analyzer**

建立 `src/Specurai.Infrastructure/Services/SqlDryRunAnalysis.cs`：

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL Dry Run 分析結果（純離線解析，不碰資料庫）
/// </summary>
public class SqlDryRunAnalysis
{
    /// <summary>語法與分類驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>陳述式類型</summary>
    public DryRunStatementType StatementType { get; init; } = DryRunStatementType.Unknown;

    /// <summary>語法錯誤明細</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（多語句、非 DML 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>目標資料表 Schema（無法解析時為 null）</summary>
    public string? TargetSchema { get; init; }

    /// <summary>目標資料表名稱（無法解析時為 null，如 CTE 目標）</summary>
    public string? TargetTable { get; init; }

    /// <summary>使用者是否已自帶 OUTPUT / OUTPUT INTO 子句</summary>
    public bool HasUserOutputClause { get; init; }
}
```

建立 `src/Specurai.Infrastructure/Services/SqlDryRunAnalyzer.cs`：

```csharp
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL Dry Run 分析器：以 ScriptDom 解析、驗證、分類單一 DML（純離線，不碰資料庫）
/// </summary>
public class SqlDryRunAnalyzer
{
    /// <summary>
    /// 解析並驗證 SQL：必須恰好一句 INSERT/UPDATE/DELETE
    /// </summary>
    public SqlDryRunAnalysis Analyze(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                SyntaxErrors = parseErrors
                    .Select(e => new DryRunSyntaxError { Line = e.Line, Column = e.Column, Message = e.Message })
                    .ToList()
            };
        }

        var statements = ((TSqlScript)fragment).Batches
            .SelectMany(b => b.Statements)
            .ToList();

        if (statements.Count == 0)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        if (statements.Count > 1)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = $"偵測到 {statements.Count} 個陳述式，dry run 僅允許單一 DML 陳述式。"
            };
        }

        return statements[0] switch
        {
            InsertStatement insert => AnalyzeInsert(insert),
            UpdateStatement update => AnalyzeUpdate(update),
            DeleteStatement delete => AnalyzeDelete(delete),
            _ => new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run；SELECT、DDL、EXEC、TRUNCATE 等語法不允許。"
            }
        };
    }

    private static SqlDryRunAnalysis AnalyzeInsert(InsertStatement insert)
    {
        var spec = insert.InsertSpecification;

        // INSERT ... EXEC 會執行預存程序，無法安全預演
        if (spec.InsertSource is ExecuteInsertSource)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "INSERT ... EXEC 會執行預存程序，無法安全預演，不允許 dry run。"
            };
        }

        var (schema, table) = ResolveTarget(spec.Target, fromClause: null, insert.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Insert,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    private static SqlDryRunAnalysis AnalyzeUpdate(UpdateStatement update)
    {
        var spec = update.UpdateSpecification;
        var (schema, table) = ResolveTarget(spec.Target, spec.FromClause, update.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Update,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    private static SqlDryRunAnalysis AnalyzeDelete(DeleteStatement delete)
    {
        var spec = delete.DeleteSpecification;
        var (schema, table) = ResolveTarget(spec.Target, spec.FromClause, delete.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Delete,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    /// <summary>
    /// 解析 DML 目標為實際資料表名稱：
    /// 目標是 CTE 時無法解析（回傳 null）；目標是 FROM 子句別名時解析回實際資料表。
    /// </summary>
    private static (string? Schema, string? Table) ResolveTarget(
        TableReference target, FromClause? fromClause, WithCtesAndXmlNamespaces? ctes)
    {
        if (target is not NamedTableReference named)
            return (null, null);

        var baseName = named.SchemaObject.BaseIdentifier.Value;

        // 目標名稱是 CTE：不是實體資料表
        if (ctes?.CommonTableExpressions.Any(c =>
                string.Equals(c.ExpressionName.Value, baseName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return (null, null);
        }

        // 目標名稱是 FROM 子句中的別名：解析為實際資料表
        if (fromClause != null)
        {
            foreach (var reference in FlattenTableReferences(fromClause.TableReferences))
            {
                if (reference is NamedTableReference n &&
                    string.Equals(n.Alias?.Value, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    return (n.SchemaObject.SchemaIdentifier?.Value, n.SchemaObject.BaseIdentifier.Value);
                }
            }
        }

        return (named.SchemaObject.SchemaIdentifier?.Value, baseName);
    }

    private static IEnumerable<TableReference> FlattenTableReferences(IEnumerable<TableReference> references)
    {
        foreach (var reference in references)
        {
            if (reference is QualifiedJoin join)
            {
                foreach (var inner in FlattenTableReferences([join.FirstTableReference, join.SecondTableReference]))
                    yield return inner;
            }
            else
            {
                yield return reference;
            }
        }
    }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunAnalyzerTests"`
Expected: PASS（16 個測試）。若個別測試失敗，多半是 ScriptDom API 名稱差異（如屬性名 `WithCtesAndXmlNamespaces`）——以編譯錯誤訊息與套件實際 API 修正實作，不要改測試的預期行為。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Infrastructure/Specurai.Infrastructure.csproj src/Specurai.Infrastructure/Services/SqlDryRunAnalysis.cs src/Specurai.Infrastructure/Services/SqlDryRunAnalyzer.cs tests/Specurai.Infrastructure.Tests/Services/SqlDryRunAnalyzerTests.cs
git commit -m "feat: 新增 SqlDryRunAnalyzer 以 ScriptDom 解析驗證單一 DML

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `SqlDryRunAnalyzer.RewriteWithOutput`（OUTPUT 子句注入）

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/SqlDryRunAnalyzer.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/SqlDryRunAnalyzerTests.cs`（追加測試）

**Interfaces:**
- Consumes: Task 2 的 `SqlDryRunAnalyzer`
- Produces: `string RewriteWithOutput(string sql, IReadOnlyList<string>? updateColumns = null)` — 前置條件：sql 已通過 `Analyze` 驗證

- [ ] **Step 1: 寫失敗測試**

在 `SqlDryRunAnalyzerTests.cs` 追加：

```csharp
    [Fact(DisplayName = "RewriteWithOutput: INSERT 應注入 OUTPUT inserted.*")]
    public void RewriteWithOutput_Insert_ShouldInjectInsertedStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("INSERT INTO dbo.Users (Name) VALUES (N'測試')");

        rewritten.Should().ContainEquivalentOf("output inserted.*");
    }

    [Fact(DisplayName = "RewriteWithOutput: DELETE 應注入 OUTPUT deleted.*")]
    public void RewriteWithOutput_Delete_ShouldInjectDeletedStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("DELETE FROM dbo.Users WHERE Id = 1");

        rewritten.Should().ContainEquivalentOf("output deleted.*");
        rewritten.Should().ContainEquivalentOf("where");
    }

    [Fact(DisplayName = "RewriteWithOutput: UPDATE 有欄位清單應注入舊/新別名欄位")]
    public void RewriteWithOutput_UpdateWithColumns_ShouldInjectAliasedColumns()
    {
        var rewritten = _analyzer.RewriteWithOutput(
            "UPDATE Users SET Name = N'x' WHERE Id = 1",
            ["Id", "Name"]);

        rewritten.Should().Contain("[舊_Id]");
        rewritten.Should().Contain("[新_Id]");
        rewritten.Should().Contain("[舊_Name]");
        rewritten.Should().Contain("[新_Name]");
        rewritten.Should().ContainEquivalentOf("deleted.Id");
        rewritten.Should().ContainEquivalentOf("inserted.Name");
    }

    [Fact(DisplayName = "RewriteWithOutput: UPDATE 無欄位清單應退回 deleted.*, inserted.*")]
    public void RewriteWithOutput_UpdateWithoutColumns_ShouldFallbackToStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("UPDATE Users SET Name = N'x' WHERE Id = 1");

        rewritten.Should().ContainEquivalentOf("output deleted.*");
        rewritten.Should().ContainEquivalentOf("inserted.*");
    }

    [Fact(DisplayName = "RewriteWithOutput: 使用者已自帶 OUTPUT 應沿用不重複注入")]
    public void RewriteWithOutput_UserOutputClause_ShouldNotInjectAgain()
    {
        var rewritten = _analyzer.RewriteWithOutput("DELETE FROM Users OUTPUT deleted.Id WHERE Id = 1");

        // 只有一個 OUTPUT，且是使用者原本的欄位
        System.Text.RegularExpressions.Regex.Matches(rewritten, "OUTPUT", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
        rewritten.Should().ContainEquivalentOf("deleted.Id");
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunAnalyzerTests"`
Expected: 編譯失敗（`RewriteWithOutput` 不存在）

- [ ] **Step 3: 實作 RewriteWithOutput**

在 `SqlDryRunAnalyzer.cs` 追加方法：

```csharp
    /// <summary>
    /// 注入 OUTPUT 子句以擷取前後資料對照。
    /// 前置條件：sql 已通過 Analyze 驗證（單一 DML）。
    /// UPDATE 提供 updateColumns 時產生 舊_欄位/新_欄位 別名對照；未提供時退回 deleted.*, inserted.*。
    /// 使用者已自帶 OUTPUT 子句時不重複注入。
    /// </summary>
    public string RewriteWithOutput(string sql, IReadOnlyList<string>? updateColumns = null)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out _);
        var statement = ((TSqlScript)fragment).Batches.SelectMany(b => b.Statements).Single();

        switch (statement)
        {
            case InsertStatement insert when insert.InsertSpecification.OutputClause == null
                                          && insert.InsertSpecification.OutputIntoClause == null:
                insert.InsertSpecification.OutputClause = BuildStarOutput("inserted");
                break;

            case DeleteStatement delete when delete.DeleteSpecification.OutputClause == null
                                          && delete.DeleteSpecification.OutputIntoClause == null:
                delete.DeleteSpecification.OutputClause = BuildStarOutput("deleted");
                break;

            case UpdateStatement update when update.UpdateSpecification.OutputClause == null
                                          && update.UpdateSpecification.OutputIntoClause == null:
                update.UpdateSpecification.OutputClause = updateColumns is { Count: > 0 }
                    ? BuildAliasedUpdateOutput(updateColumns)
                    : BuildStarOutput("deleted", "inserted");
                break;
        }

        var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase
        });
        generator.GenerateScript(fragment, out var rewritten);
        return rewritten;
    }

    private static OutputClause BuildStarOutput(params string[] qualifiers)
    {
        var clause = new OutputClause();
        foreach (var qualifier in qualifiers)
        {
            clause.SelectColumns.Add(new SelectStarExpression
            {
                Qualifier = new MultiPartIdentifier
                {
                    Identifiers = { new Identifier { Value = qualifier } }
                }
            });
        }
        return clause;
    }

    private static OutputClause BuildAliasedUpdateOutput(IReadOnlyList<string> columns)
    {
        var clause = new OutputClause();
        foreach (var column in columns)
        {
            clause.SelectColumns.Add(BuildAliasedColumn("deleted", column, $"舊_{column}"));
            clause.SelectColumns.Add(BuildAliasedColumn("inserted", column, $"新_{column}"));
        }
        return clause;
    }

    private static SelectScalarExpression BuildAliasedColumn(string qualifier, string column, string alias) => new()
    {
        Expression = new ColumnReferenceExpression
        {
            MultiPartIdentifier = new MultiPartIdentifier
            {
                Identifiers =
                {
                    new Identifier { Value = qualifier },
                    new Identifier { Value = column, QuoteType = QuoteType.SquareBracket }
                }
            }
        },
        ColumnName = new IdentifierOrValueExpression
        {
            Identifier = new Identifier { Value = alias, QuoteType = QuoteType.SquareBracket }
        }
    };
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunAnalyzerTests"`
Expected: PASS（21 個測試）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Services/SqlDryRunAnalyzer.cs tests/Specurai.Infrastructure.Tests/Services/SqlDryRunAnalyzerTests.cs
git commit -m "feat: SqlDryRunAnalyzer 新增 OUTPUT 子句注入以擷取前後資料對照

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `SqlDryRunRepository`（交易執行 + 一律回滾）與 DI 註冊

**Files:**
- Create: `src/Specurai.Infrastructure/Repositories/SqlDryRunRepository.cs`
- Modify: `src/Specurai.Infrastructure/ServiceRegistration.cs`（`ISqlQueryRepository` 註冊之後加一筆）
- Test: `tests/Specurai.Infrastructure.Tests/Repositories/SqlDryRunRepositoryTests.cs`
- Modify: `tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs`（若該檔以逐一 Resolve 驗證註冊，追加 `ISqlDryRunRepository`；先閱讀該檔現況再比照追加）

**Interfaces:**
- Consumes: Task 1 `ISqlDryRunRepository`/`DryRunResult`、Task 2-3 `SqlDryRunAnalyzer`
- Produces: `class SqlDryRunRepository : ISqlDryRunRepository`，建構子 `SqlDryRunRepository(Func<string?> connectionStringProvider)`；DI 可解析 `ISqlDryRunRepository`

- [ ] **Step 1: 寫失敗測試（可離線驗證的部分）**

建立 `tests/Specurai.Infrastructure.Tests/Repositories/SqlDryRunRepositoryTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlDryRunRepositoryTests
{
    [Fact(DisplayName = "DryRunAsync: 未設定連線字串應擲出例外")]
    public async Task DryRunAsync_NoConnectionString_ShouldThrow()
    {
        var repo = new SqlDryRunRepository(() => null);

        var act = () => repo.DryRunAsync("DELETE FROM T WHERE Id = 1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未設定資料庫連線*");
    }

    [Fact(DisplayName = "DryRunAsync: 語法錯誤應直接回報，不嘗試連線")]
    public async Task DryRunAsync_SyntaxError_ShouldReturnWithoutConnecting()
    {
        // 連線字串指向不存在的主機：若有嘗試連線會逾時或擲例外，
        // 此測試同時驗證「語法錯誤在連線前短路」
        var repo = new SqlDryRunRepository(() => "Server=invalid-host;Database=x;Connect Timeout=1;Encrypt=False");

        var result = await repo.DryRunAsync("UPDATE Users SET WHERE Id = 1");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "DryRunAsync: 非 DML 應直接拒絕，不嘗試連線")]
    public async Task DryRunAsync_NonDml_ShouldRejectWithoutConnecting()
    {
        var repo = new SqlDryRunRepository(() => "Server=invalid-host;Database=x;Connect Timeout=1;Encrypt=False");

        var result = await repo.DryRunAsync("DROP TABLE Users");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunRepositoryTests"`
Expected: 編譯失敗（`SqlDryRunRepository` 不存在）

- [ ] **Step 3: 實作 Repository**

建立 `src/Specurai.Infrastructure/Repositories/SqlDryRunRepository.cs`：

```csharp
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL Dry Run Repository 實作：在交易中執行單一 DML 擷取預演結果，最後一律 ROLLBACK
/// </summary>
public class SqlDryRunRepository : ISqlDryRunRepository
{
    /// <summary>前後對照預覽筆數上限</summary>
    private const int PreviewRowLimit = 100;

    /// <summary>SQL Server 錯誤 334：目標表有觸發程序時，OUTPUT 子句（無 INTO）不允許使用</summary>
    private const int TriggerOutputErrorNumber = 334;

    private readonly Func<string?> _connectionStringProvider;
    private readonly SqlDryRunAnalyzer _analyzer = new();

    public SqlDryRunRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<DryRunResult> DryRunAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await DryRunAsync(sql, connectionString, ct);
    }

    public async Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default)
    {
        // 離線解析與驗證：不通過就不連資料庫
        var analysis = _analyzer.Analyze(sql);
        if (!analysis.IsValid)
        {
            return new DryRunResult
            {
                IsValid = false,
                StatementType = analysis.StatementType,
                SyntaxErrors = analysis.SyntaxErrors,
                RejectReason = analysis.RejectReason
            };
        }

        var warnings = new List<string>();
        if (analysis.StatementType == DryRunStatementType.Insert)
            warnings.Add("若目標資料表有 IDENTITY 欄位，序號在回滾後仍會被消耗。");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // OUTPUT 注入：UPDATE 需先查目標表欄位以產生 舊_欄位/新_欄位 別名對照
        string rewrittenSql;
        if (analysis.HasUserOutputClause)
        {
            rewrittenSql = sql;
        }
        else if (analysis.StatementType == DryRunStatementType.Update)
        {
            var columns = await GetTableColumnsAsync(connection, analysis.TargetSchema, analysis.TargetTable, ct);
            if (columns.Count == 0)
                warnings.Add("無法解析目標資料表欄位，前後對照以 deleted/inserted 全欄位呈現。");
            rewrittenSql = _analyzer.RewriteWithOutput(sql, columns);
        }
        else
        {
            rewrittenSql = _analyzer.RewriteWithOutput(sql);
        }

        try
        {
            return await ExecutePreviewAsync(connection, rewrittenSql, analysis, warnings, ct);
        }
        catch (SqlException ex) when (ex.Number == TriggerOutputErrorNumber && !analysis.HasUserOutputClause)
        {
            // 目標表有觸發程序：退回原句執行，只回報影響筆數
            warnings.Add("目標資料表有觸發程序（Trigger），無法提供前後資料對照，僅回報影響筆數。");
            return await ExecuteCountOnlyAsync(connection, sql, analysis, warnings, ct);
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
    }

    /// <summary>
    /// 在交易中執行含 OUTPUT 的 DML，讀取前後對照後回滾
    /// </summary>
    private static async Task<DryRunResult> ExecutePreviewAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            using var reader = await command.ExecuteReaderAsync(ct);

            var preview = new DataTable();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                // 未別名的 deleted.*/inserted.* 會產生重複欄位名稱，加序號避免 DataTable 衝突
                if (preview.Columns.Contains(name))
                    name = $"{name}_{i}";
                preview.Columns.Add(name, typeof(object));
            }

            var total = 0;
            while (await reader.ReadAsync(ct))
            {
                total++;
                if (total > PreviewRowLimit)
                    continue;

                var row = preview.NewRow();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                preview.Rows.Add(row);
            }

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = total,
                PreviewTable = preview,
                PreviewTruncated = total > PreviewRowLimit,
                Warnings = warnings
            };
        }
        finally
        {
            // 一律回滾（不使用呼叫端的取消權杖，確保回滾必定送出）
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Trigger fallback：在交易中執行原句，只取得影響筆數後回滾
    /// </summary>
    private static async Task<DryRunResult> ExecuteCountOnlyAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            var affected = await command.ExecuteNonQueryAsync(ct);

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = affected,
                Warnings = warnings
            };
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 查詢目標資料表的欄位清單（依 column_id 排序）
    /// </summary>
    private static async Task<List<string>> GetTableColumnsAsync(
        SqlConnection connection, string? schema, string? table, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(table))
            return [];

        const string sql = @"
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(@FullName)
            ORDER BY c.column_id";

        var escapedTable = table.Replace("]", "]]");
        var fullName = string.IsNullOrEmpty(schema)
            ? $"[{escapedTable}]"
            : $"[{schema.Replace("]", "]]")}].[{escapedTable}]";

        var result = await connection.QueryAsync<string>(
            new CommandDefinition(sql, new { FullName = fullName }, cancellationToken: ct));
        return result.ToList();
    }
}
```

- [ ] **Step 4: DI 註冊**

在 `src/Specurai.Infrastructure/ServiceRegistration.cs` 第 33-34 行（`ISqlQueryRepository` 註冊）之後加入：

```csharp
        services.AddSingleton<ISqlDryRunRepository>(sp =>
            new SqlDryRunRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
```

- [ ] **Step 5: 更新 DI Smoke 測試**

先閱讀 `tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs`：若該檔逐一 Resolve 各服務，比照現有寫法追加一筆 `ISqlDryRunRepository` 的解析驗證；若是自動掃描全部註冊則不需修改。

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDryRunRepositoryTests" && dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 全部 PASS

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/SqlDryRunRepository.cs src/Specurai.Infrastructure/ServiceRegistration.cs tests/Specurai.Infrastructure.Tests/Repositories/SqlDryRunRepositoryTests.cs tests/Specurai.McpServer.Tests/DiResolutionSmokeTests.cs
git commit -m "feat: 新增 SqlDryRunRepository 交易執行與回滾並註冊 DI

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: MCP 工具 `dry_run_sql`

**Files:**
- Modify: `src/Specurai.McpServer/Tools/SqlTools.cs`
- Test: `tests/Specurai.McpServer.Tests/SqlToolsTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 1 `ISqlDryRunRepository`、`DryRunResult`
- Produces: MCP 工具 `dry_run_sql`（方法名 `DryRunSql`），回傳 JSON 字串

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.McpServer.Tests/SqlToolsTests.cs`：

```csharp
using System.Data;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class SqlToolsTests
{
    [Fact(DisplayName = "dry_run_sql: 驗證失敗應回傳拒絕原因且標記未變更")]
    public async Task DryRunSql_Invalid_ShouldReturnRejectReason()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run"
            });

        var result = await SqlTools.DryRunSql(repo, "DROP TABLE T");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("RejectReason").GetString().Should().Contain("僅支援");
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "dry_run_sql: 語法錯誤應回傳行列明細")]
    public async Task DryRunSql_SyntaxError_ShouldReturnErrorDetails()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 18, Message = "Incorrect syntax near WHERE" }]
            });

        var result = await SqlTools.DryRunSql(repo, "UPDATE T SET WHERE");

        using var doc = JsonDocument.Parse(result);
        var error = doc.RootElement.GetProperty("SyntaxErrors").EnumerateArray().First();
        error.GetProperty("Line").GetInt32().Should().Be(1);
        error.GetProperty("Column").GetInt32().Should().Be(18);
        error.GetProperty("Message").GetString().Should().Contain("WHERE");
    }

    [Fact(DisplayName = "dry_run_sql: 成功預演應回傳筆數、預覽與 RolledBack=true")]
    public async Task DryRunSql_Success_ShouldReturnPreviewAndRolledBack()
    {
        var preview = new DataTable();
        preview.Columns.Add("舊_Name", typeof(object));
        preview.Columns.Add("新_Name", typeof(object));
        preview.Rows.Add("張三", "張三丰");

        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = preview,
                Warnings = ["警告一"]
            });

        var result = await SqlTools.DryRunSql(repo, "UPDATE Users SET Name = N'張三丰' WHERE Id = 1");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("StatementType").GetString().Should().Be("Update");
        doc.RootElement.GetProperty("AffectedRowCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("RolledBack").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("PreviewColumns").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainInOrder("舊_Name", "新_Name");
        doc.RootElement.GetProperty("PreviewRows").EnumerateArray().Should().HaveCount(1);
        doc.RootElement.GetProperty("Warnings").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("警告一");
    }

    [Fact(DisplayName = "dry_run_sql: 執行期錯誤應回傳 ExecutionError")]
    public async Task DryRunSql_ExecutionError_ShouldReturnError()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                ExecutionError = "此語句實際執行將會失敗：REFERENCE 條件約束衝突"
            });

        var result = await SqlTools.DryRunSql(repo, "DELETE FROM Users WHERE Id = 1");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("ExecutionError").GetString().Should().Contain("REFERENCE");
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "dry_run_sql: Repository 擲例外應回傳友善錯誤")]
    public async Task DryRunSql_RepositoryThrows_ShouldReturnFriendlyError()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DryRunResult>>(_ => throw new InvalidOperationException("未設定資料庫連線"));

        var result = await SqlTools.DryRunSql(repo, "DELETE FROM T");

        result.Should().Contain("Dry run 執行失敗");
        result.Should().Contain("未設定資料庫連線");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~SqlToolsTests"`
Expected: 編譯失敗（`SqlTools.DryRunSql` 不存在）

- [ ] **Step 3: 實作 MCP 工具**

在 `src/Specurai.McpServer/Tools/SqlTools.cs`：

（a）在 `GetCreateTableSql` 方法之後加入新工具：

```csharp
    /// <summary>
    /// Dry Run 預演單一 DML（一律回滾）
    /// </summary>
    [McpServerTool, Description("以 Dry Run 預演單一 DML（INSERT/UPDATE/DELETE）：驗證語法、在交易中執行以取得影響筆數與前後資料對照，最後一律 ROLLBACK，絕不修改資料")]
    public static async Task<string> DryRunSql(
        ISqlDryRunRepository sqlDryRunRepository,
        [Description("要預演的單一 DML 陳述式（INSERT/UPDATE/DELETE）")] string sql)
    {
        try
        {
            var result = await sqlDryRunRepository.DryRunAsync(sql);

            if (!result.IsValid)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = false,
                    result.RejectReason,
                    SyntaxErrors = result.SyntaxErrors.Select(e => new { e.Line, e.Column, e.Message }),
                    DatabaseChanged = false
                }, JsonOptions);
            }

            if (result.ExecutionError != null)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = true,
                    StatementType = result.StatementType.ToString(),
                    result.ExecutionError,
                    result.Warnings,
                    RolledBack = true,
                    DatabaseChanged = false
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                Valid = true,
                StatementType = result.StatementType.ToString(),
                result.AffectedRowCount,
                PreviewColumns = result.PreviewTable?.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName).ToArray(),
                PreviewRows = result.PreviewTable == null ? null : DataTableToRows(result.PreviewTable),
                result.PreviewTruncated,
                result.Warnings,
                RolledBack = true,
                DatabaseChanged = false
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Dry run 執行失敗：{ex.Message}";
        }
    }
```

（b）將既有 `DataTableToJson` 中的列轉換抽成共用方法，並讓 `DataTableToJson` 改用它（DRY）：

```csharp
    private static List<Dictionary<string, object?>> DataTableToRows(DataTable dataTable)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow row in dataTable.Rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DataColumn col in dataTable.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static string DataTableToJson(DataTable dataTable)
    {
        var rows = DataTableToRows(dataTable);

        var result = new
        {
            RowCount = rows.Count,
            Columns = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray(),
            Rows = rows
        };

        return JsonSerializer.Serialize(result, JsonOptions);
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 全部 PASS（新增 5 個 + 既有測試不受影響）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/SqlTools.cs tests/Specurai.McpServer.Tests/SqlToolsTests.cs
git commit -m "feat: MCP 新增 dry_run_sql 工具預演 DML 並一律回滾

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: CLI 命令 `sql dry-run`

**Files:**
- Modify: `src/Specurai.Cli/Commands/SqlCommand.cs`

**Interfaces:**
- Consumes: Task 1 `ISqlDryRunRepository`、`DryRunResult`（DI 已在 Task 4 註冊，CLI 沿用 `Program.Services`）
- Produces: CLI 子命令 `sql dry-run "<sql>"`，支援全域 `--json` 模式；失敗時 exit code 1

（CLI 專案沒有對應測試專案，本 Task 以建置與手動煙霧測試驗證。）

- [ ] **Step 1: 實作命令**

在 `src/Specurai.Cli/Commands/SqlCommand.cs`：

（a）`Create()` 中加入（`CreateQueryCommand()` 之後）：

```csharp
        command.AddCommand(CreateDryRunCommand());
```

（b）加入新方法（放在 `CreateQueryCommand()` 之後）：

```csharp
    private static Command CreateDryRunCommand()
    {
        var sqlArg = new Argument<string>("sql", "單一 DML 陳述式（INSERT/UPDATE/DELETE）");
        var command = new Command("dry-run", "預演 DML：驗證語法、回報影響筆數與前後資料對照，一律回滾不修改資料") { sqlArg };

        command.SetHandler(async (sql) =>
        {
            var repo = Program.Services.GetRequiredService<ISqlDryRunRepository>();

            try
            {
                var result = await repo.DryRunAsync(sql);

                if (CliOutput.JsonMode)
                {
                    OutputJson(result);
                    if (!result.IsValid || result.ExecutionError != null)
                        Environment.ExitCode = 1;
                    return;
                }

                if (!result.IsValid)
                {
                    foreach (var error in result.SyntaxErrors)
                        CliOutput.Error($"語法錯誤（第 {error.Line} 行第 {error.Column} 列）：{error.Message}");
                    if (result.RejectReason != null)
                        CliOutput.Error(result.RejectReason);
                    Environment.ExitCode = 1;
                    return;
                }

                if (result.ExecutionError != null)
                {
                    CliOutput.Error(result.ExecutionError);
                    foreach (var warning in result.Warnings)
                        CliOutput.Warning(warning);
                    CliOutput.Info("已回滾，資料庫未變更。");
                    Environment.ExitCode = 1;
                    return;
                }

                CliOutput.Info($"語法：有效（{result.StatementType}）");
                CliOutput.Info($"影響筆數：{result.AffectedRowCount} 筆");

                if (result.PreviewTable is { Rows.Count: > 0 })
                {
                    var table = new Table().Title("前後資料對照");
                    foreach (DataColumn col in result.PreviewTable.Columns)
                        table.AddColumn(col.ColumnName.EscapeMarkup());

                    foreach (DataRow row in result.PreviewTable.Rows)
                    {
                        var cells = new string[result.PreviewTable.Columns.Count];
                        for (var i = 0; i < result.PreviewTable.Columns.Count; i++)
                            cells[i] = (row[i] == DBNull.Value ? "" : row[i]?.ToString() ?? "").EscapeMarkup();
                        table.AddRow(cells);
                    }
                    AnsiConsole.Write(table);

                    if (result.PreviewTruncated)
                        CliOutput.Info($"預覽僅顯示前 {result.PreviewTable.Rows.Count} 筆。");
                }

                foreach (var warning in result.Warnings)
                    CliOutput.Warning(warning);

                CliOutput.Info("已回滾，資料庫未變更。");
            }
            catch (Exception ex)
            {
                CliOutput.Error($"Dry run 失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, sqlArg);

        return command;

        static void OutputJson(Specurai.Domain.Entities.DryRunResult result)
        {
            var previewRows = new List<Dictionary<string, object?>>();
            if (result.PreviewTable != null)
            {
                foreach (DataRow row in result.PreviewTable.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in result.PreviewTable.Columns)
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                    previewRows.Add(dict);
                }
            }

            CliOutput.Success(new
            {
                Valid = result.IsValid,
                StatementType = result.StatementType.ToString(),
                result.RejectReason,
                SyntaxErrors = result.SyntaxErrors.Select(e => new { e.Line, e.Column, e.Message }).ToList(),
                result.AffectedRowCount,
                PreviewRows = previewRows,
                result.PreviewTruncated,
                result.Warnings,
                result.ExecutionError,
                RolledBack = result.IsValid,
                DatabaseChanged = false
            }, previewRows.Count);
        }
    }
```

注意：若 `CliOutput.Success` 的簽章與上述不符（先閱讀 `src/Specurai.Cli/Output/CliOutput.cs` 確認），比照 `CreateQueryCommand` 既有用法調整。

- [ ] **Step 2: 建置驗證**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: 建置成功，無警告錯誤

- [ ] **Step 3: 煙霧測試（不需真實連線的路徑）**

Run: `dotnet run --project src/Specurai.Cli -- sql dry-run "DROP TABLE X"`
Expected: 顯示「僅支援 INSERT/UPDATE/DELETE …」錯誤，exit code 1（`echo $?` 或 `$LASTEXITCODE` 為 1）

Run: `dotnet run --project src/Specurai.Cli -- sql dry-run "UPDATE T SET WHERE"`
Expected: 顯示語法錯誤（含行列）

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Cli/Commands/SqlCommand.cs
git commit -m "feat: CLI 新增 sql dry-run 命令預演 DML

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Desktop SQL 查詢分頁加入 Dry Run

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs:112-127`（建構子）與 `:413,428`（建立 SqlQueryDocumentViewModel 處）
- Modify: `src/Specurai.Desktop/Program.cs:80-88`（MainWindowViewModel DI 註冊）
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelTests.cs`（追加）

**Interfaces:**
- Consumes: Task 1 `ISqlDryRunRepository`、`DryRunResult`、`DryRunStatementType`
- Produces:
  - `SqlQueryDocumentViewModel` DI 建構子改為 `(ISqlQueryRepository, IConnectionManager, ISqlDryRunRepository? sqlDryRunRepository = null)`（選擇性參數，既有呼叫端與測試不需改）
  - 新增 `[RelayCommand] DryRunAsync`（產生 `DryRunCommand`）、`[ObservableProperty] string _dryRunWarnings`、計算屬性 `bool HasDryRunWarnings`
  - `MainWindowViewModel` DI 建構子在 `ISqlQueryRepository sqlQueryRepository` 之後新增必要參數 `ISqlDryRunRepository sqlDryRunRepository`

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelTests.cs` 追加（檔頭已有的 using 之外，確認有 `using System.Data;`、`using Specurai.Domain.Entities;`；`_sqlQueryRepository`、`_connectionManager` 為既有欄位）：

```csharp
    [Fact]
    public void 初始狀態_DryRunWarnings應為空字串()
    {
        var vm = new SqlQueryDocumentViewModel();

        vm.DryRunWarnings.Should().BeEmpty();
        vm.HasDryRunWarnings.Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_成功預演_應顯示筆數與回滾訊息並載入預覽()
    {
        var preview = new DataTable();
        preview.Columns.Add("舊_Name", typeof(object));
        preview.Columns.Add("新_Name", typeof(object));
        preview.Rows.Add("張三", "張三丰");

        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = preview,
                Warnings = ["測試警告"]
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "UPDATE Users SET Name = N'張三丰' WHERE Id = 1"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.QueryResults.Should().HaveCount(1);
        vm.RowCount.Should().Be(1);
        vm.StatusMessage.Should().Contain("影響 1 筆");
        vm.StatusMessage.Should().Contain("已回滾");
        vm.DryRunWarnings.Should().Contain("測試警告");
        vm.HasDryRunWarnings.Should().BeTrue();
    }

    [Fact]
    public async Task DryRun_語法錯誤_應顯示行列訊息()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 18, Message = "Incorrect syntax" }]
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "UPDATE T SET WHERE"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("語法錯誤");
        vm.StatusMessage.Should().Contain("第 1 行");
        vm.QueryResults.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_被拒絕_應顯示拒絕原因()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run"
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "DROP TABLE X"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Fact]
    public async Task DryRun_執行期錯誤_應顯示ExecutionError()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                ExecutionError = "此語句實際執行將會失敗：REFERENCE 條件約束衝突"
            });

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "DELETE FROM Users WHERE Id = 1"
        };

        await vm.DryRunCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("實際執行將會失敗");
        vm.StatusMessage.Should().Contain("REFERENCE");
    }

    [Fact]
    public async Task 執行一般查詢_應清除DryRun警告()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Insert,
                AffectedRowCount = 1,
                PreviewTable = new DataTable(),
                Warnings = ["IDENTITY 警告"]
            });
        _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DataTable());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "INSERT INTO T (A) VALUES (1)"
        };

        await vm.DryRunCommand.ExecuteAsync(null);
        vm.HasDryRunWarnings.Should().BeTrue();

        vm.SqlText = "SELECT 1 AS A";
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.HasDryRunWarnings.Should().BeFalse();
    }
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~SqlQueryDocumentViewModelTests"`
Expected: 編譯失敗（`DryRunCommand`、`DryRunWarnings` 不存在）

- [ ] **Step 3: 實作 ViewModel**

修改 `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`：

（a）新增欄位（`_sqlQueryRepository` 之後）：

```csharp
    private readonly ISqlDryRunRepository? _sqlDryRunRepository;
```

（b）DI 建構子改為（保持既有內容，加第三個選擇性參數與指派）：

```csharp
    public SqlQueryDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IConnectionManager connectionManager,
        ISqlDryRunRepository? sqlDryRunRepository = null)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
        _sqlDryRunRepository = sqlDryRunRepository;
        // …其餘既有內容不變
    }
```

（c）新增可觀察屬性與計算屬性（`_executionTimeMs` 之後）：

```csharp
    [ObservableProperty]
    private string _dryRunWarnings = string.Empty;

    /// <summary>是否有 Dry Run 警告需要顯示（供警告列 IsVisible 綁定）</summary>
    public bool HasDryRunWarnings => !string.IsNullOrEmpty(DryRunWarnings);

    partial void OnDryRunWarningsChanged(string value) => OnPropertyChanged(nameof(HasDryRunWarnings));
```

（d）新增命令（`ExecuteQueryAsync` 之後）：

```csharp
    /// <summary>
    /// Dry Run 預演 DML：交易中執行取得影響筆數與前後對照，一律回滾
    /// </summary>
    [RelayCommand]
    private async Task DryRunAsync()
    {
        if (_sqlDryRunRepository == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        try
        {
            IsExecuting = true;
            StatusMessage = "Dry Run 執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;

            var stopwatch = Stopwatch.StartNew();
            var result = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlDryRunRepository.DryRunAsync(SqlText.Trim(), _localConnectionString)
                : await _sqlDryRunRepository.DryRunAsync(SqlText.Trim());
            stopwatch.Stop();
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (!result.IsValid)
            {
                StatusMessage = result.SyntaxErrors.Count > 0
                    ? $"語法錯誤（第 {result.SyntaxErrors[0].Line} 行第 {result.SyntaxErrors[0].Column} 列）：{result.SyntaxErrors[0].Message}"
                    : result.RejectReason ?? "Dry run 驗證未通過";
                return;
            }

            if (result.ExecutionError != null)
            {
                StatusMessage = result.ExecutionError;
                DryRunWarnings = string.Join("\n", result.Warnings);
                return;
            }

            if (result.PreviewTable != null)
            {
                foreach (DataColumn col in result.PreviewTable.Columns)
                {
                    ResultColumns.Add(new DataGridTextColumn
                    {
                        Header = col.ColumnName,
                        Binding = new Avalonia.Data.Binding($"[{col.ColumnName}]"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                    });
                }

                foreach (DataRow row in result.PreviewTable.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in result.PreviewTable.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                    }
                    QueryResults.Add(dict);
                }
            }

            RowCount = result.AffectedRowCount;
            DryRunWarnings = string.Join("\n", result.Warnings);
            var truncatedNote = result.PreviewTruncated ? $"（預覽僅顯示前 {QueryResults.Count} 筆）" : "";
            StatusMessage = $"Dry Run 完成：影響 {result.AffectedRowCount} 筆（{result.StatementType}）{truncatedNote}｜已回滾，資料庫未變更，耗時 {ExecutionTimeMs} ms";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dry run 失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }
```

（e）在既有 `ExecuteQueryAsync` 的 `try` 區塊開頭（`ResultColumns.Clear();` 之後）加一行清除警告：

```csharp
            DryRunWarnings = string.Empty;
```

- [ ] **Step 4: 串接 MainWindowViewModel 與 DI**

修改 `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`：

（a）新增欄位（`_sqlQueryRepository` 之後）：

```csharp
    private readonly ISqlDryRunRepository? _sqlDryRunRepository;
```

（b）DI 建構子在 `ISqlQueryRepository sqlQueryRepository,` 之後新增參數 `ISqlDryRunRepository sqlDryRunRepository,`，並在指派區加：

```csharp
        _sqlDryRunRepository = sqlDryRunRepository;
```

（c）第 413 行與第 428 行的 `new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)` 改為：

```csharp
        var doc = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository);
```

修改 `src/Specurai.Desktop/Program.cs` 第 80-88 行的 MainWindowViewModel 註冊，在 `sp.GetRequiredService<ISqlQueryRepository>(),` 之後加：

```csharp
                sp.GetRequiredService<ISqlDryRunRepository>(),
```

- [ ] **Step 5: 修改 AXAML**

修改 `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`：

（a）工具列「執行」按鈕之後（`ClearQueryCommand` 按鈕之前）加入：

```xml
                    <Button Command="{Binding DryRunCommand}"
                            IsEnabled="{Binding !IsExecuting}"
                            HotKey="F6"
                            ToolTip.Tip="預演 DML（INSERT/UPDATE/DELETE）：交易中執行後一律回滾，不會修改資料">
                        <StackPanel Orientation="Horizontal" Spacing="5">
                            <TextBlock Text="🧪" FontSize="14"/>
                            <TextBlock Text="Dry Run (F6)"/>
                        </StackPanel>
                    </Button>
```

（b）最外層 Grid 的 `RowDefinitions="Auto,*,Auto"` 改為 `RowDefinitions="Auto,Auto,*,Auto"`，在工具列 Border 之後插入警告列，並將原本 `Grid.Row="1"` 的主內容區改為 `Grid.Row="2"`、原本 `Grid.Row="2"` 的狀態列改為 `Grid.Row="3"`：

```xml
        <!-- Dry Run 警告列 -->
        <Border Grid.Row="1" Background="#33FFA500" Padding="10,5"
                IsVisible="{Binding HasDryRunWarnings}">
            <TextBlock Text="{Binding DryRunWarnings}" TextWrapping="Wrap" FontWeight="SemiBold"/>
        </Border>
```

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 全部 PASS（新增 6 個 + 既有測試不受影響——SqlQueryDocumentViewModel 第三參數是選擇性的，既有呼叫端不需改）

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs src/Specurai.Desktop/Program.cs src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelTests.cs
git commit -m "feat: 桌面 SQL 查詢分頁新增 Dry Run 按鈕與警告列

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: 文件更新與整體驗證

**Files:**
- Modify: `README.md`（約 461-467 行的 MCP 工具表格；若有 CLI 命令一覽也一併補）
- Modify: `docs/McpServerREADME.md`（第 104 行「SQL 查詢」列）

**Interfaces:**
- Consumes: 前述所有 Task 的成果
- Produces: 文件與整體驗證通過

- [ ] **Step 1: 更新文件**

`docs/McpServerREADME.md` 第 104 行，在 `execute_readonly_sql` 之後加入 `dry_run_sql`：

```markdown
| SQL 查詢 | `execute_readonly_sql`、`dry_run_sql`、`search_columns`、`search_columns_multi_database`、`get_create_table_sql` |
```

`README.md` 約 464 行的工具表格加一列：

```markdown
| `dry_run_sql` | Dry Run 預演單一 DML（一律回滾） |
```

並搜尋 README.md 中 CLI `sql query` 的說明區塊（若存在），比照補上：

```markdown
| `sql dry-run "<sql>"` | 預演 DML：影響筆數與前後對照，一律回滾 |
```

- [ ] **Step 2: 全方案建置與測試**

Run: `dotnet build && dotnet test`
Expected: 建置成功、全部測試 PASS（原 604 + 新增約 35 個）

- [ ] **Step 3: 真實資料庫手動驗證**

需在有測試資料庫連線的環境執行（透過 CLI 或重新發布後的 MCP；republish MCP 前記得先結束執行中的 Specurai 行程，避免 DLL 鎖定）：

1. `sql dry-run "UPDATE <某測試表> SET <欄位> = <新值> WHERE <條件>"` → 確認顯示 舊_/新_ 欄位對照與筆數
2. 再 `sql query "SELECT ..."` 查同一條件 → 確認資料**未**被修改（回滾生效）
3. `sql dry-run "INSERT ..."` → 確認 IDENTITY 警告出現
4. `sql dry-run "DELETE ... WHERE <違反 FK 的條件>"`（若有 FK 測試表）→ 確認回報「實際執行將會失敗」
5. 對有 trigger 的資料表 dry run（若有）→ 確認 fallback 警告與筆數
6. 桌面 App：開 SQL 查詢分頁按 Dry Run → 確認結果格與警告列顯示

- [ ] **Step 4: Commit**

```bash
git add README.md docs/McpServerREADME.md
git commit -m "docs: 補充 dry_run_sql 工具與 sql dry-run 命令說明

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: 程式碼審查**

依專案規範，完成後使用 `superpowers:requesting-code-review` 技能進行程式碼審查，再回報完成。
