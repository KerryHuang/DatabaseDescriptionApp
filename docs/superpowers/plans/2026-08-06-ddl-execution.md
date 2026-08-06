# DDL 執行能力（僅限非正式環境）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增一條獨立的 DDL 執行管線（白名單物件級 DDL、多句批次、預演 + confirm 閘門、Production 一律拒絕），開通 MCP、CLI、Desktop 三入口。

**Architecture:** 平行複製既有 DML 管線骨架——Infrastructure 的 `SqlDdlScriptAnalyzer`（ScriptDom 離線白名單驗證）與 `SqlDdlExecuteRepository`（單一交易逐批執行），Application 的 `DdlExecutionService`（連線解析 + Production 防線 + confirm 分流），再接三入口。規格見 `docs/superpowers/specs/2026-08-06-ddl-execution-design.md`。

**Tech Stack:** .NET 8、Microsoft.SqlServer.TransactSql.ScriptDom（`TSql160Parser`，Infrastructure 已引用）、Microsoft.Data.SqlClient、xUnit + NSubstitute + FluentAssertions、CommunityToolkit.Mvvm、System.CommandLine、MCP SDK。

## Global Constraints

- UI 文字、註解、Commit 訊息、測試 DisplayName 一律繁體中文；測試命名 `[Method]_[Condition]_[Expected]`。
- Clean Architecture：Domain 無相依；Application 只相依 Domain；Repository 介面放 `Specurai.Domain/Interfaces/`、實作放 `Specurai.Infrastructure/Repositories/`；DI 統一在 `src/Specurai.Infrastructure/ServiceRegistration.cs`（三端共用）。
- TDD：每個任務先寫失敗測試再實作（純資料類別除外）。
- git：禁止 `git add -A`／`git add .`，一律逐檔指名 add；commit 前 `git status` 驗 staged。
- 測試指令：`dotnet test tests/<專案>/<專案>.csproj --filter "FullyQualifiedName~<類名>"`；全套 `dotnet test`。
- 白名單 fail-closed：不在名單的語句類型一律拒絕（含 XML/Spatial/Columnstore/FullText 等特殊索引語句——它們是獨立的 ScriptDom 類別，不繼承 `CreateIndexStatement`，自然被擋下，屬預期行為）。

---

### Task 1: Domain — 結果實體與 Repository 介面

**Files:**
- Create: `src/Specurai.Domain/Entities/DdlExecutionResult.cs`
- Create: `src/Specurai.Domain/Interfaces/ISqlDdlExecuteRepository.cs`

**Interfaces:**
- Consumes: 既有 `DryRunSyntaxError`（`src/Specurai.Domain/Entities/DryRunResult.cs:19`）。
- Produces: `DdlStatementSummary { int Index, string Type, string? ObjectName, int BatchIndex }`、`DdlExecutionResult { bool IsValid, IReadOnlyList<DryRunSyntaxError> SyntaxErrors, string? RejectReason, IReadOnlyList<DdlStatementSummary> Statements, string? ExecutionError, int? FailedBatchIndex, bool Committed, bool CommitUncertain }`、`ISqlDdlExecuteRepository.ExecuteAsync(string script, string connectionString, bool commit, CancellationToken ct = default)` → `Task<DdlExecutionResult>`。後續所有任務都用這組型別。

純資料類別與介面，無邏輯可測，以建置驗證後直接 commit。

- [ ] **Step 1: 建立 `DdlExecutionResult.cs`**

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// DDL 逐句摘要：驗證通過後回報整批要動哪些物件
/// </summary>
public class DdlStatementSummary
{
    /// <summary>語句序號（全 script 連續，1 起算）</summary>
    public required int Index { get; init; }

    /// <summary>語句類型（如 CREATE TABLE、DROP INDEX）</summary>
    public required string Type { get; init; }

    /// <summary>目標物件名稱（無法解析時為 null，如 ALTER INDEX ALL）</summary>
    public string? ObjectName { get; init; }

    /// <summary>所屬 GO 批次（1 起算）</summary>
    public required int BatchIndex { get; init; }
}

/// <summary>
/// DDL 預演／執行結果：confirm=false 一律回滾；confirm=true 成功時 COMMIT（見 <see cref="Committed"/>）。
/// </summary>
public class DdlExecutionResult
{
    /// <summary>語法與白名單驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（非白名單語句、正式環境、空 script 等）</summary>
    public string? RejectReason { get; init; }

    /// <summary>逐句摘要（驗證通過後提供）</summary>
    public IReadOnlyList<DdlStatementSummary> Statements { get; init; } = [];

    /// <summary>語法正確但實際執行失敗時的錯誤訊息（整批已回滾）</summary>
    public string? ExecutionError { get; init; }

    /// <summary>執行失敗的 GO 批次索引（1 起算；SQL 錯誤訊息本身含行號可再定位）</summary>
    public int? FailedBatchIndex { get; init; }

    /// <summary>是否已 COMMIT 變更 schema（預演一律 false）</summary>
    public bool Committed { get; init; }

    /// <summary>COMMIT 失敗、交易結果不確定時為 true；預演與一般執行失敗（COMMIT 前即失敗）皆為 false</summary>
    public bool CommitUncertain { get; init; }
}
```

- [ ] **Step 2: 建立 `ISqlDdlExecuteRepository.cs`**

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL DDL 執行 Repository 介面：驗證白名單物件級 DDL 批次後在單一交易中逐批執行，
/// commit=false 一律 ROLLBACK（預演）、commit=true 全部成功才 COMMIT。
/// 環境限制（Production 拒絕）由 Application 層的 IDdlExecutionService 把關，
/// 呼叫端不應繞過該服務直接使用本介面。
/// </summary>
public interface ISqlDdlExecuteRepository
{
    /// <summary>
    /// 使用指定連線字串執行 DDL script（可含多句與 GO）
    /// </summary>
    Task<DdlExecutionResult> ExecuteAsync(
        string script, string connectionString, bool commit, CancellationToken ct = default);
}
```

- [ ] **Step 3: 建置驗證**

Run: `dotnet build src/Specurai.Domain/Specurai.Domain.csproj`
Expected: Build succeeded, 0 Warning（新檔案）

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Domain/Entities/DdlExecutionResult.cs src/Specurai.Domain/Interfaces/ISqlDdlExecuteRepository.cs
git commit -m "feat: 新增 DDL 執行結果實體與 Repository 介面"
```

---

### Task 2: Infrastructure — `SqlDdlScriptAnalyzer`（ScriptDom 白名單驗證）

**Files:**
- Create: `src/Specurai.Infrastructure/Services/SqlDdlScriptAnalysis.cs`
- Create: `src/Specurai.Infrastructure/Services/SqlDdlScriptAnalyzer.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/SqlDdlScriptAnalyzerTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `DdlStatementSummary`、`DryRunSyntaxError`。
- Produces: `SqlDdlScriptAnalysis { bool IsValid, IReadOnlyList<DryRunSyntaxError> SyntaxErrors, string? RejectReason, IReadOnlyList<DdlStatementSummary> Statements, IReadOnlyList<string> Batches }`、`SqlDdlScriptAnalyzer.Analyze(string script)` → `SqlDdlScriptAnalysis`。Task 3 的 Repository 內部使用。

- [ ] **Step 1: 寫失敗測試**

```csharp
using FluentAssertions;
using Specurai.Infrastructure.Services;
using Xunit;

namespace Specurai.Infrastructure.Tests.Services;

public class SqlDdlScriptAnalyzerTests
{
    private readonly SqlDdlScriptAnalyzer _analyzer = new();

    [Theory(DisplayName = "Analyze_白名單DDL_應通過並回報類型")]
    [InlineData("CREATE TABLE dbo.T1 (Id INT NOT NULL)", "CREATE TABLE")]
    [InlineData("ALTER TABLE dbo.T1 ADD C2 NVARCHAR(50) NULL", "ALTER TABLE")]
    [InlineData("DROP TABLE dbo.T1", "DROP TABLE")]
    [InlineData("CREATE NONCLUSTERED INDEX IX_T1_C2 ON dbo.T1 (C2)", "CREATE INDEX")]
    [InlineData("ALTER INDEX IX_T1_C2 ON dbo.T1 REBUILD", "ALTER INDEX")]
    [InlineData("DROP INDEX IX_T1_C2 ON dbo.T1", "DROP INDEX")]
    [InlineData("CREATE VIEW dbo.V1 AS SELECT 1 AS A", "CREATE VIEW")]
    [InlineData("CREATE OR ALTER VIEW dbo.V1 AS SELECT 1 AS A", "CREATE OR ALTER VIEW")]
    [InlineData("DROP VIEW dbo.V1", "DROP VIEW")]
    [InlineData("CREATE PROCEDURE dbo.P1 AS BEGIN SELECT 1 END", "CREATE PROCEDURE")]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.P1 AS BEGIN SELECT 1 END", "CREATE OR ALTER PROCEDURE")]
    [InlineData("DROP PROCEDURE dbo.P1", "DROP PROCEDURE")]
    [InlineData("CREATE FUNCTION dbo.F1() RETURNS INT AS BEGIN RETURN 1 END", "CREATE FUNCTION")]
    [InlineData("DROP FUNCTION dbo.F1", "DROP FUNCTION")]
    [InlineData("CREATE TRIGGER dbo.TR1 ON dbo.T1 AFTER INSERT AS BEGIN SET NOCOUNT ON END", "CREATE TRIGGER")]
    [InlineData("DROP TRIGGER dbo.TR1", "DROP TRIGGER")]
    [InlineData("CREATE SCHEMA app", "CREATE SCHEMA")]
    [InlineData("DROP SCHEMA app", "DROP SCHEMA")]
    public void Analyze_白名單DDL_應通過並回報類型(string sql, string expectedType)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Statements.Should().HaveCount(1);
        result.Statements[0].Type.Should().Be(expectedType);
        result.Batches.Should().HaveCount(1);
    }

    [Theory(DisplayName = "Analyze_非白名單語句_應拒絕")]
    [InlineData("CREATE DATABASE X")]
    [InlineData("ALTER DATABASE X SET RECOVERY SIMPLE")]
    [InlineData("DROP DATABASE X")]
    [InlineData("TRUNCATE TABLE dbo.T1")]
    [InlineData("GRANT SELECT ON dbo.T1 TO SomeUser")]
    [InlineData("CREATE USER SomeUser WITHOUT LOGIN")]
    [InlineData("CREATE LOGIN SomeLogin WITH PASSWORD = 'x'")]
    [InlineData("EXEC dbo.P1")]
    [InlineData("SELECT 1")]
    [InlineData("INSERT INTO dbo.T1 (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.T1 SET Id = 1")]
    [InlineData("DELETE FROM dbo.T1")]
    public void Analyze_非白名單語句_應拒絕(string sql)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("白名單");
    }

    [Fact(DisplayName = "Analyze_混合批次含DML_應拒絕並指明句序")]
    public void Analyze_混合批次含DML_應拒絕並指明句序()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT)\nGO\nINSERT INTO dbo.T1 (Id) VALUES (1)";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("第 2 句");
    }

    [Fact(DisplayName = "Analyze_GO分批_應正確切批並標記批次索引")]
    public void Analyze_GO分批_應正確切批並標記批次索引()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT)\nGO\nCREATE OR ALTER PROCEDURE dbo.P1 AS BEGIN SELECT 1 END";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Batches.Should().HaveCount(2);
        result.Statements.Should().HaveCount(2);
        result.Statements[0].BatchIndex.Should().Be(1);
        result.Statements[1].BatchIndex.Should().Be(2);
        result.Statements[1].Index.Should().Be(2);
        result.Batches[1].Should().Contain("PROCEDURE");
        result.Batches[1].Should().NotContain("CREATE TABLE");
    }

    [Fact(DisplayName = "Analyze_同批次多句DDL_應全數列入摘要")]
    public void Analyze_同批次多句DDL_應全數列入摘要()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT);\nCREATE NONCLUSTERED INDEX IX_T1_Id ON dbo.T1 (Id);";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Batches.Should().HaveCount(1);
        result.Statements.Should().HaveCount(2);
        result.Statements[1].BatchIndex.Should().Be(1);
    }

    [Fact(DisplayName = "Analyze_應解析目標物件名稱")]
    public void Analyze_應解析目標物件名稱()
    {
        var result = _analyzer.Analyze("CREATE TABLE dbo.T1 (Id INT)");

        result.Statements[0].ObjectName.Should().Be("[dbo].[T1]");
    }

    [Fact(DisplayName = "Analyze_語法錯誤_應回報明細")]
    public void Analyze_語法錯誤_應回報明細()
    {
        var result = _analyzer.Analyze("CREATE TABBLE dbo.T1 (Id INT)");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }

    [Theory(DisplayName = "Analyze_空Script_應拒絕")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GO")]
    public void Analyze_空Script_應拒絕(string script)
    {
        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未偵測到");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDdlScriptAnalyzerTests"`
Expected: 編譯失敗（`SqlDdlScriptAnalyzer` 不存在）

- [ ] **Step 3: 建立 `SqlDdlScriptAnalysis.cs`**

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// DDL script 離線分析結果：驗證通過時附逐句摘要與 GO 批次切分
/// </summary>
public class SqlDdlScriptAnalysis
{
    /// <summary>語法與白名單驗證是否通過</summary>
    public required bool IsValid { get; init; }

    /// <summary>語法錯誤明細（語法解析失敗時）</summary>
    public IReadOnlyList<DryRunSyntaxError> SyntaxErrors { get; init; } = [];

    /// <summary>非語法錯誤的拒絕原因（非白名單語句、空 script）</summary>
    public string? RejectReason { get; init; }

    /// <summary>逐句摘要</summary>
    public IReadOnlyList<DdlStatementSummary> Statements { get; init; } = [];

    /// <summary>依 GO 切分的可執行批次文字（依原始順序）</summary>
    public IReadOnlyList<string> Batches { get; init; } = [];
}
```

- [ ] **Step 4: 建立 `SqlDdlScriptAnalyzer.cs`**

```csharp
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL DDL script 分析器：以 ScriptDom 解析、逐句比對白名單、依 GO 切批（純離線，不碰資料庫）。
/// 白名單採 fail-closed：不在名單的語句類型一律拒絕。
/// </summary>
public class SqlDdlScriptAnalyzer
{
    /// <summary>
    /// 解析並驗證 DDL script：每一句都必須是白名單內的物件級 DDL
    /// </summary>
    public SqlDdlScriptAnalysis Analyze(string script)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(script), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            return new SqlDdlScriptAnalysis
            {
                IsValid = false,
                SyntaxErrors = parseErrors
                    .Select(e => new DryRunSyntaxError { Line = e.Line, Column = e.Column, Message = e.Message })
                    .ToList()
            };
        }

        var statements = new List<DdlStatementSummary>();
        var batches = new List<string>();
        var index = 0;

        foreach (var batch in ((TSqlScript)fragment).Batches)
        {
            if (batch.Statements.Count == 0)
                continue;

            var batchIndex = batches.Count + 1;
            foreach (var statement in batch.Statements)
            {
                index++;
                var type = ClassifyAllowed(statement);
                if (type == null)
                {
                    return new SqlDdlScriptAnalysis
                    {
                        IsValid = false,
                        RejectReason = $"第 {index} 句（{statement.GetType().Name}）不在允許的 DDL 白名單；" +
                            "僅允許 TABLE/INDEX/VIEW/PROCEDURE/FUNCTION/TRIGGER/SCHEMA 的物件級 CREATE/ALTER/DROP，" +
                            "庫級操作、TRUNCATE、權限語句、EXEC 與 DML 一律拒絕。"
                    };
                }

                statements.Add(new DdlStatementSummary
                {
                    Index = index,
                    Type = type,
                    ObjectName = GetObjectName(statement),
                    BatchIndex = batchIndex
                });
            }

            batches.Add(GetBatchText(batch));
        }

        if (statements.Count == 0)
        {
            return new SqlDdlScriptAnalysis
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        return new SqlDdlScriptAnalysis
        {
            IsValid = true,
            Statements = statements,
            Batches = batches
        };
    }

    /// <summary>
    /// 白名單分類：允許的語句回傳顯示用類型名稱，否則回傳 null（fail-closed）。
    /// AlterTableStatement 是所有 ALTER TABLE 變體的抽象基底，單一 pattern 即涵蓋；
    /// XML/Spatial/Columnstore/FullText 等特殊索引是獨立類別，不繼承 CreateIndexStatement，自然被擋。
    /// </summary>
    private static string? ClassifyAllowed(TSqlStatement statement) => statement switch
    {
        CreateTableStatement => "CREATE TABLE",
        AlterTableStatement => "ALTER TABLE",
        DropTableStatement => "DROP TABLE",
        CreateIndexStatement => "CREATE INDEX",
        AlterIndexStatement => "ALTER INDEX",
        DropIndexStatement => "DROP INDEX",
        CreateViewStatement => "CREATE VIEW",
        AlterViewStatement => "ALTER VIEW",
        CreateOrAlterViewStatement => "CREATE OR ALTER VIEW",
        DropViewStatement => "DROP VIEW",
        CreateProcedureStatement => "CREATE PROCEDURE",
        AlterProcedureStatement => "ALTER PROCEDURE",
        CreateOrAlterProcedureStatement => "CREATE OR ALTER PROCEDURE",
        DropProcedureStatement => "DROP PROCEDURE",
        CreateFunctionStatement => "CREATE FUNCTION",
        AlterFunctionStatement => "ALTER FUNCTION",
        CreateOrAlterFunctionStatement => "CREATE OR ALTER FUNCTION",
        DropFunctionStatement => "DROP FUNCTION",
        CreateTriggerStatement => "CREATE TRIGGER",
        AlterTriggerStatement => "ALTER TRIGGER",
        CreateOrAlterTriggerStatement => "CREATE OR ALTER TRIGGER",
        DropTriggerStatement => "DROP TRIGGER",
        CreateSchemaStatement => "CREATE SCHEMA",
        AlterSchemaStatement => "ALTER SCHEMA",
        DropSchemaStatement => "DROP SCHEMA",
        _ => null
    };

    /// <summary>
    /// 解析目標物件名稱（顯示用，best-effort）：無法解析時回傳 null（如 ALTER INDEX ALL）。
    /// Drop 類別繼承 DropObjectsStatement，與 ViewStatementBody 等 Body 抽象基底無繼承關係，
    /// pattern 順序不影響比對結果，僅依可讀性排列。
    /// </summary>
    private static string? GetObjectName(TSqlStatement statement) => statement switch
    {
        CreateTableStatement s => Format(s.SchemaObjectName),
        AlterTableStatement s => Format(s.SchemaObjectName),
        DropTableStatement s => Format(s.Objects.FirstOrDefault()),
        CreateIndexStatement s => s.Name?.Value,
        AlterIndexStatement s => s.Name?.Value,
        DropIndexStatement s => s.DropIndexClauses.OfType<DropIndexClause>().FirstOrDefault()?.Index?.Value,
        DropViewStatement s => Format(s.Objects.FirstOrDefault()),
        ViewStatementBody s => Format(s.SchemaObjectName),
        DropProcedureStatement s => Format(s.Objects.FirstOrDefault()),
        ProcedureStatementBody s => Format(s.ProcedureReference?.Name),
        DropFunctionStatement s => Format(s.Objects.FirstOrDefault()),
        FunctionStatementBody s => Format(s.Name),
        DropTriggerStatement s => Format(s.Objects.FirstOrDefault()),
        TriggerStatementBody s => Format(s.Name),
        CreateSchemaStatement s => s.Name?.Value,
        AlterSchemaStatement s => s.Name?.Value,
        DropSchemaStatement s => Format(s.Schema),
        _ => null
    };

    private static string? Format(SchemaObjectName? name) =>
        name == null ? null : string.Join(".", name.Identifiers.Select(i => $"[{i.Value}]"));

    /// <summary>
    /// 以 token 流重建批次原文（保留原始格式與註解）
    /// </summary>
    private static string GetBatchText(TSqlBatch batch)
    {
        var tokens = batch.ScriptTokenStream;
        var sb = new StringBuilder();
        for (var i = batch.FirstTokenIndex; i <= batch.LastTokenIndex; i++)
            sb.Append(tokens[i].Text);
        return sb.ToString();
    }
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDdlScriptAnalyzerTests"`
Expected: 全數 PASS。若個別 ScriptDom 類別名或屬性名與實際 API 有出入（如 `DropIndexClause`、`DropSchemaStatement.Schema`），以編譯錯誤訊息為準修正實作，不改測試的行為斷言。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Infrastructure/Services/SqlDdlScriptAnalysis.cs src/Specurai.Infrastructure/Services/SqlDdlScriptAnalyzer.cs tests/Specurai.Infrastructure.Tests/Services/SqlDdlScriptAnalyzerTests.cs
git commit -m "feat: 新增 DDL script 分析器（ScriptDom 白名單驗證與 GO 切批）"
```

---

### Task 3: Infrastructure — `SqlDdlExecuteRepository`（單一交易逐批執行）

**Files:**
- Create: `src/Specurai.Infrastructure/Repositories/SqlDdlExecuteRepository.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Repositories/SqlDdlExecuteRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 `ISqlDdlExecuteRepository`／`DdlExecutionResult`、Task 2 `SqlDdlScriptAnalyzer`。
- Produces: `SqlDdlExecuteRepository : ISqlDdlExecuteRepository`（無建構參數）。Task 4 的 Service 透過介面使用。

實際資料庫行為（交易、回滾）不寫自動化測試（既有測試套件皆離線），只測「驗證不過就不連線」的離線路徑；資料庫行為由 Task 8 手動驗證涵蓋。

- [ ] **Step 1: 寫失敗測試**

```csharp
using FluentAssertions;
using Specurai.Infrastructure.Repositories;
using Xunit;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlDdlExecuteRepositoryTests
{
    [Fact(DisplayName = "ExecuteAsync_驗證不過_應直接回拒絕不連線")]
    public async Task ExecuteAsync_驗證不過_應直接回拒絕不連線()
    {
        var repository = new SqlDdlExecuteRepository();

        // 連線字串指向不存在的主機：驗證若有連線會逾時失敗，藉此證明拒絕發生在離線階段
        var result = await repository.ExecuteAsync(
            "TRUNCATE TABLE dbo.T1",
            "Server=unreachable.invalid;Database=x;Connect Timeout=1;TrustServerCertificate=True",
            commit: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("白名單");
        result.Committed.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_語法錯誤_應回報明細不連線")]
    public async Task ExecuteAsync_語法錯誤_應回報明細不連線()
    {
        var repository = new SqlDdlExecuteRepository();

        var result = await repository.ExecuteAsync(
            "CREATE TABBLE dbo.T1 (Id INT)",
            "Server=unreachable.invalid;Database=x;Connect Timeout=1;TrustServerCertificate=True",
            commit: false);

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDdlExecuteRepositoryTests"`
Expected: 編譯失敗（`SqlDdlExecuteRepository` 不存在）

- [ ] **Step 3: 建立 `SqlDdlExecuteRepository.cs`**

```csharp
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL DDL 執行 Repository 實作：離線驗證通過後在單一交易中依 GO 批次逐批執行，
/// 任一批失敗即整批回滾；commit=false 一律 ROLLBACK（預演）、commit=true 全部成功才 COMMIT。
/// SQL Server 物件級 DDL 皆為 transactional，回滾可靠。
/// </summary>
public class SqlDdlExecuteRepository : ISqlDdlExecuteRepository
{
    private readonly SqlDdlScriptAnalyzer _analyzer = new();

    public async Task<DdlExecutionResult> ExecuteAsync(
        string script, string connectionString, bool commit, CancellationToken ct = default)
    {
        // 離線解析與驗證：不通過就不連資料庫
        var analysis = _analyzer.Analyze(script);
        if (!analysis.IsValid)
        {
            return new DdlExecutionResult
            {
                IsValid = false,
                SyntaxErrors = analysis.SyntaxErrors,
                RejectReason = analysis.RejectReason
            };
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            for (var i = 0; i < analysis.Batches.Count; i++)
            {
                try
                {
                    await using var command = new SqlCommand(analysis.Batches[i], connection, transaction)
                    {
                        CommandTimeout = 60
                    };
                    await command.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex)
                {
                    return new DdlExecutionResult
                    {
                        IsValid = true,
                        Statements = analysis.Statements,
                        FailedBatchIndex = i + 1,
                        ExecutionError = commit
                            ? $"第 {i + 1} 批執行失敗（整批已回滾）：{ex.Message}"
                            : $"第 {i + 1} 批實際執行將會失敗：{ex.Message}"
                    };
                }
            }

            if (commit)
            {
                try
                {
                    // 交易收尾不使用呼叫端的取消權杖，確保必定送出
                    await transaction.CommitAsync(CancellationToken.None);
                    committed = true;
                }
                catch (SqlException ex)
                {
                    // COMMIT 本身失敗（如提交過程中斷線）：結果不確定，不能宣稱已回滾
                    return new DdlExecutionResult
                    {
                        IsValid = true,
                        Statements = analysis.Statements,
                        ExecutionError = $"COMMIT 失敗，交易結果不確定，請查詢資料庫確認：{ex.Message}",
                        CommitUncertain = true
                    };
                }
            }

            return new DdlExecutionResult
            {
                IsValid = true,
                Statements = analysis.Statements,
                Committed = committed
            };
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (SqlException)
                {
                    // 本地 rollback 失敗不得蓋掉原始例外：連線已斷時 SQL Server 會自行回滾未提交交易，
                    // 本地 rollback 失敗不代表資料風險，吞掉即可
                }
            }
        }
    }
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDdlExecuteRepositoryTests"`
Expected: 2 PASS

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/SqlDdlExecuteRepository.cs tests/Specurai.Infrastructure.Tests/Repositories/SqlDdlExecuteRepositoryTests.cs
git commit -m "feat: 新增 DDL 執行 Repository（單一交易逐批執行，任一批失敗整批回滾）"
```

---

### Task 4: Application — `DdlExecutionService`（Production 防線 + confirm 分流）與 DI 註冊

**Files:**
- Create: `src/Specurai.Application/Services/IDdlExecutionService.cs`
- Create: `src/Specurai.Application/Services/DdlExecutionService.cs`
- Modify: `src/Specurai.Infrastructure/ServiceRegistration.cs`（DML 註冊區塊之後，約 :104）
- Test: `tests/Specurai.Application.Tests/Services/DdlExecutionServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 `ISqlDdlExecuteRepository`、既有 `IConnectionManager`（`GetCurrentProfile()`、`GetEnabledProfiles()`、`GetCurrentConnectionString()`、`GetConnectionString(Guid)`）。
- Produces: `IDdlExecutionService.ExecuteAsync(string script, bool confirm, Guid? profileId = null, CancellationToken ct = default)` → `Task<DdlExecutionResult>`。三入口（Task 5/6/7）都用這個介面。

- [ ] **Step 1: 寫失敗測試**

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Application.Tests.Services;

public class DdlExecutionServiceTests
{
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();
    private readonly ISqlDdlExecuteRepository _repository = Substitute.For<ISqlDdlExecuteRepository>();

    private DdlExecutionService CreateService() => new(_connectionManager, _repository);

    private static ConnectionProfile Profile(
        DatabaseEnvironment environment = DatabaseEnvironment.Staging, string name = "測試連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Environment = environment
    };

    private const string Ddl = "CREATE TABLE dbo.T1 (Id INT)";

    [Fact(DisplayName = "ExecuteAsync_目前連線為正式環境_應拒絕且不呼叫Repository")]
    public async Task ExecuteAsync_目前連線為正式環境_應拒絕且不呼叫Repository()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Production, "正式庫"));

        var result = await CreateService().ExecuteAsync(Ddl, confirm: true);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
        result.RejectReason.Should().Contain("正式庫");
        await _repository.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_未設定目前連線_應拒絕")]
    public async Task ExecuteAsync_未設定目前連線_應拒絕()
    {
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未設定目前連線");
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId不存在_應拒絕不落回目前連線")]
    public async Task ExecuteAsync_指定profileId不存在_應拒絕不落回目前連線()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetEnabledProfiles().Returns([]);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false, profileId: Guid.NewGuid());

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("找不到指定的連線設定");
        await _repository.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_連線字串為空_應拒絕")]
    public async Task ExecuteAsync_連線字串為空_應拒絕()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetCurrentConnectionString().Returns((string?)null);

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("連線字串");
    }

    [Theory(DisplayName = "ExecuteAsync_confirm旗標_應轉為commit傳遞")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_confirm旗標_應轉為commit傳遞(bool confirm)
    {
        _connectionManager.GetCurrentProfile().Returns(Profile());
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _repository.ExecuteAsync(Ddl, "conn", confirm, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Committed = confirm });

        var result = await CreateService().ExecuteAsync(Ddl, confirm);

        result.IsValid.Should().BeTrue();
        await _repository.Received(1).ExecuteAsync(Ddl, "conn", confirm, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId_應使用該連線字串")]
    public async Task ExecuteAsync_指定profileId_應使用該連線字串()
    {
        var target = Profile(name: "目標連線");
        _connectionManager.GetEnabledProfiles().Returns([target]);
        _connectionManager.GetConnectionString(target.Id).Returns("target-conn");
        _repository.ExecuteAsync(Ddl, "target-conn", false, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true });

        var result = await CreateService().ExecuteAsync(Ddl, confirm: false, profileId: target.Id);

        result.IsValid.Should().BeTrue();
        await _repository.Received(1).ExecuteAsync(Ddl, "target-conn", false, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~DdlExecutionServiceTests"`
Expected: 編譯失敗（`DdlExecutionService` 不存在）

- [ ] **Step 3: 建立 `IDdlExecutionService.cs`**

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// DDL 執行服務：環境閘門與 confirm 分流的唯一所在。
/// Production 連線一律拒絕（不連資料庫）；
/// confirm=false 走交易內預演（一律回滾）、confirm=true 走實際執行（COMMIT）。
/// </summary>
public interface IDdlExecutionService
{
    /// <summary>
    /// 執行 DDL script（白名單物件級 DDL，可含多句與 GO）
    /// </summary>
    /// <param name="script">DDL script</param>
    /// <param name="confirm">false 僅預演；true 實際執行並 COMMIT</param>
    /// <param name="profileId">目標連線設定檔（null 表示目前連線，跟隨資料庫覆寫）</param>
    Task<DdlExecutionResult> ExecuteAsync(
        string script, bool confirm, Guid? profileId = null, CancellationToken ct = default);
}
```

- [ ] **Step 4: 建立 `DdlExecutionService.cs`**

```csharp
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// DDL 執行服務實作
/// </summary>
public class DdlExecutionService : IDdlExecutionService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ISqlDdlExecuteRepository _executeRepository;

    public DdlExecutionService(
        IConnectionManager connectionManager,
        ISqlDdlExecuteRepository executeRepository)
    {
        _connectionManager = connectionManager;
        _executeRepository = executeRepository;
    }

    public async Task<DdlExecutionResult> ExecuteAsync(
        string script, bool confirm, Guid? profileId = null, CancellationToken ct = default)
    {
        // 解析目標連線：指定 profileId 時不得靜默落回目前連線
        var profile = profileId == null
            ? _connectionManager.GetCurrentProfile()
            : _connectionManager.GetEnabledProfiles().FirstOrDefault(p => p.Id == profileId.Value);

        if (profile == null)
            return Reject(profileId == null
                ? "未設定目前連線，無法執行 DDL。"
                : "找不到指定的連線設定（可能已停用），請改選其他連線。");

        if (profile.Environment == DatabaseEnvironment.Production)
            return Reject($"連線「{profile.Name}」為正式環境，不允許執行 DDL。");

        var connectionString = profileId == null
            ? _connectionManager.GetCurrentConnectionString()
            : _connectionManager.GetConnectionString(profileId.Value);

        if (string.IsNullOrEmpty(connectionString))
            return Reject("無法取得連線字串，請確認連線設定。");

        return await _executeRepository.ExecuteAsync(script, connectionString, commit: confirm, ct);
    }

    private static DdlExecutionResult Reject(string reason)
        => new() { IsValid = false, RejectReason = reason };
}
```

- [ ] **Step 5: 在 `ServiceRegistration.cs` 註冊**

在「Application - DML 執行（非正式環境限定）」區塊（約 :100-104）之後加入：

```csharp
        // Infrastructure + Application - DDL 執行（非正式環境限定）
        services.AddSingleton<ISqlDdlExecuteRepository, SqlDdlExecuteRepository>();
        services.AddSingleton<IDdlExecutionService, DdlExecutionService>();
```

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~DdlExecutionServiceTests"`
Expected: 7 PASS（5 Fact + 2 Theory case）

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Application/Services/IDdlExecutionService.cs src/Specurai.Application/Services/DdlExecutionService.cs src/Specurai.Infrastructure/ServiceRegistration.cs tests/Specurai.Application.Tests/Services/DdlExecutionServiceTests.cs
git commit -m "feat: 新增 DDL 執行服務（Production 防線 + confirm 分流）並註冊 DI"
```

---

### Task 5: MCP 入口 — `execute_ddl` 工具

**Files:**
- Modify: `src/Specurai.McpServer/Tools/SqlTools.cs`（在 `ExecuteSql` 方法之後新增）
- Test: `tests/Specurai.McpServer.Tests/DdlToolTests.cs`（新檔）
- Modify: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`（theory 加一列 InlineData）

**Interfaces:**
- Consumes: Task 4 `IDdlExecutionService`。
- Produces: `SqlTools.ExecuteDdl(IDdlExecutionService ddlExecutionService, string script, bool confirm = false)` → `Task<string>`（JSON）。

- [ ] **Step 1: 寫失敗測試**

`tests/Specurai.McpServer.Tests/DdlToolTests.cs`：

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;
using Xunit;

namespace Specurai.McpServer.Tests;

public class DdlToolTests
{
    private readonly IDdlExecutionService _service = Substitute.For<IDdlExecutionService>();

    private const string Ddl = "CREATE TABLE dbo.T1 (Id INT)";

    private static DdlStatementSummary Summary() => new()
    {
        Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1
    };

    [Fact(DisplayName = "execute_ddl: confirm 應原樣傳遞給服務")]
    public async Task ExecuteDdl_應原樣傳遞confirm()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Statements = [Summary()], Committed = true });

        await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        await _service.Received(1).ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "execute_ddl: 預演結果應含 Hint 且 DatabaseChanged=false")]
    public async Task ExecuteDdl_預演_應含Hint()
    {
        _service.ExecuteAsync(Ddl, false, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Statements = [Summary()], Committed = false });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: false);

        json.Should().Contain("confirm:true");
        json.Should().Contain("\"DatabaseChanged\": false");
        json.Should().Contain("[dbo].[T1]");
    }

    [Fact(DisplayName = "execute_ddl: 拒絕時應回報原因")]
    public async Task ExecuteDdl_拒絕_應回報原因()
    {
        _service.ExecuteAsync(Ddl, false, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = false, RejectReason = "連線「X」為正式環境，不允許執行 DDL。" });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: false);

        json.Should().Contain("正式環境");
        json.Should().Contain("\"Valid\": false");
    }

    [Fact(DisplayName = "execute_ddl: 執行失敗應回報失敗批次")]
    public async Task ExecuteDdl_執行失敗_應回報失敗批次()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true, Statements = [Summary()],
                ExecutionError = "第 1 批執行失敗（整批已回滾）：物件已存在", FailedBatchIndex = 1
            });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        json.Should().Contain("\"FailedBatchIndex\": 1");
        json.Should().Contain("\"Committed\": false");
    }

    [Fact(DisplayName = "execute_ddl: CommitUncertain 時 Committed/DatabaseChanged 應為 null")]
    public async Task ExecuteDdl_CommitUncertain_三態應為null()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true, Statements = [Summary()],
                ExecutionError = "COMMIT 失敗，交易結果不確定，請查詢資料庫確認：斷線", CommitUncertain = true
            });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        json.Should().Contain("\"Committed\": null");
        json.Should().Contain("\"DatabaseChanged\": null");
        json.Should().Contain("\"CommitUncertain\": true");
    }
}
```

`ConfirmGateTests.cs` 的 theory（:14-22）加一列：

```csharp
    [InlineData(typeof(SqlTools), nameof(SqlTools.ExecuteDdl))]
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj --filter "FullyQualifiedName~DdlToolTests"`
Expected: 編譯失敗（`ExecuteDdl` 不存在）

- [ ] **Step 3: 在 `SqlTools.cs` 新增 `ExecuteDdl`（`ExecuteSql` 方法之後、`DataTableToRows` 之前）**

```csharp
    /// <summary>
    /// 實際執行 DDL 批次（僅限非正式環境；預設預演，confirm:true 才 COMMIT）
    /// </summary>
    [McpServerTool, Description("實際執行 DDL 批次：CREATE/ALTER/DROP 的 TABLE、INDEX、VIEW、PROCEDURE、FUNCTION、TRIGGER、SCHEMA，可含多句與 GO（⚠️ 破壞性操作：confirm:true 時會 COMMIT 變更 schema；僅限非正式環境連線，Production 一律拒絕；庫級操作、TRUNCATE、權限語句一律拒絕；預設 confirm=false 僅預演——在交易內實際執行驗證後回滾，需 confirm:true 才真正寫入）")]
    public static async Task<string> ExecuteDdl(
        IDdlExecutionService ddlExecutionService,
        [Description("要執行的 DDL script（可含多句與 GO）")] string script,
        [Description("是否實際執行（預設 false 僅預演）")] bool confirm = false)
    {
        try
        {
            var result = await ddlExecutionService.ExecuteAsync(script, confirm);

            if (!result.IsValid)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = false,
                    result.RejectReason,
                    SyntaxErrors = result.SyntaxErrors.Select(e => new { e.Line, e.Column, e.Message }),
                    Committed = false,
                    DatabaseChanged = false
                }, JsonOptions);
            }

            var statements = result.Statements
                .Select(s => new { s.Index, s.Type, s.ObjectName, s.BatchIndex });

            if (result.ExecutionError != null)
            {
                // COMMIT 結果不確定時，Committed／DatabaseChanged 都不能斷言為 false，改輸出 null
                bool? committed = result.CommitUncertain ? null : false;
                bool? databaseChanged = result.CommitUncertain ? null : false;

                return JsonSerializer.Serialize(new
                {
                    Valid = true,
                    Statements = statements,
                    result.ExecutionError,
                    result.FailedBatchIndex,
                    Committed = committed,
                    result.CommitUncertain,
                    DatabaseChanged = databaseChanged
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                Valid = true,
                Statements = statements,
                result.Committed,
                DatabaseChanged = result.Committed,
                Hint = result.Committed ? null : "以上為預演結果（已回滾）。確認無誤後加 confirm:true 實際執行。"
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"DDL 執行失敗：{ex.Message}";
        }
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 全數 PASS（含 ConfirmGateTests 新列）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/SqlTools.cs tests/Specurai.McpServer.Tests/DdlToolTests.cs tests/Specurai.McpServer.Tests/ConfirmGateTests.cs
git commit -m "feat: 新增 execute_ddl MCP 工具（白名單 DDL 批次，僅限非正式環境）"
```

---

### Task 6: CLI 入口 — `specurai sql ddl` 子命令

**Files:**
- Modify: `src/Specurai.Cli/Commands/SqlCommand.cs`

**Interfaces:**
- Consumes: Task 4 `IDdlExecutionService`、既有 `CliOutput`（`JsonMode`、`Success`、`Error`、`Warning`、`Info`）、`Program.Services`。
- Produces: `sql ddl [script] [--file <路徑>] [--confirm]` 子命令。

CLI 無測試專案（既有慣例），以建置 + Task 8 手動驗證涵蓋。

- [ ] **Step 1: 在 `Create()`（:16-24）註冊子命令**

```csharp
        command.AddCommand(CreateDdlCommand());
```

（加在 `command.AddCommand(CreateExecuteCommand());` 之後）

- [ ] **Step 2: 新增 `CreateDdlCommand` 與 JSON DTO（放在 `CreateExecuteCommand` 方法之後）**

```csharp
    private static Command CreateDdlCommand()
    {
        var scriptArg = new Argument<string?>("script", () => null, "DDL script（可含多句與 GO；與 --file 二擇一）");
        var fileOption = new Option<string?>("--file", "從檔案讀取 DDL script（與 script 引數二擇一）");
        var confirmOption = new Option<bool>("--confirm", "實際執行並 COMMIT（未指定時僅預演）");
        var command = new Command("ddl",
            "執行 DDL 批次（僅限非正式環境；預設先預演，加 --confirm 才變更 schema）")
            { scriptArg, fileOption, confirmOption };

        command.SetHandler(async (script, file, confirm) =>
        {
            // script 引數與 --file 二擇一
            var hasScript = !string.IsNullOrWhiteSpace(script);
            var hasFile = !string.IsNullOrEmpty(file);
            if (hasScript == hasFile)
            {
                CliOutput.Error("請提供 script 引數或 --file 其中之一（不可同時提供或皆缺）。");
                Environment.ExitCode = 1;
                return;
            }

            if (hasFile)
            {
                if (!File.Exists(file))
                {
                    CliOutput.Error($"找不到檔案：{file}");
                    Environment.ExitCode = 1;
                    return;
                }
                script = await File.ReadAllTextAsync(file!);
            }

            var service = Program.Services.GetRequiredService<IDdlExecutionService>();

            try
            {
                var result = await service.ExecuteAsync(script!, confirm);

                if (CliOutput.JsonMode)
                {
                    OutputDdlJson(result);
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

                var table = new Table().Title("DDL 語句摘要");
                table.AddColumn("#");
                table.AddColumn("類型");
                table.AddColumn("物件");
                table.AddColumn("批次");
                foreach (var s in result.Statements)
                {
                    table.AddRow(
                        s.Index.ToString(),
                        s.Type.EscapeMarkup(),
                        (s.ObjectName ?? "").EscapeMarkup(),
                        s.BatchIndex.ToString());
                }
                AnsiConsole.Write(table);

                if (result.ExecutionError != null)
                {
                    CliOutput.Error(result.ExecutionError);
                    CliOutput.Info(result.CommitUncertain
                        ? "交易結果不確定，請查詢資料庫確認。"
                        : "已回滾，資料庫未變更。");
                    Environment.ExitCode = 1;
                    return;
                }

                if (result.Committed)
                {
                    CliOutput.Info("已 COMMIT，schema 已變更。");
                }
                else
                {
                    CliOutput.Info("以上為預演結果（已回滾）。確認無誤後加 --confirm 實際執行。");
                }
            }
            catch (Exception ex)
            {
                CliOutput.Error($"DDL 執行失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, scriptArg, fileOption, confirmOption);

        return command;
    }

    private static void OutputDdlJson(Specurai.Domain.Entities.DdlExecutionResult result)
    {
        // COMMIT 結果不確定時，RolledBack／DatabaseChanged／Committed 一律輸出 JSON null（同 DML 規則）
        bool? rolledBack = result.CommitUncertain ? null : result.IsValid && !result.Committed;
        bool? databaseChanged = result.CommitUncertain ? null : result.Committed;
        bool? committed = result.CommitUncertain ? null : result.Committed;

        CliOutput.Success(new DdlJsonResult
        {
            Valid = result.IsValid,
            RejectReason = result.RejectReason,
            SyntaxErrors = result.SyntaxErrors.Select(e => new { e.Line, e.Column, e.Message }).ToList(),
            Statements = result.Statements
                .Select(s => new { s.Index, s.Type, s.ObjectName, s.BatchIndex }).ToList(),
            ExecutionError = result.ExecutionError,
            FailedBatchIndex = result.FailedBatchIndex,
            RolledBack = rolledBack,
            DatabaseChanged = databaseChanged,
            Committed = committed,
            CommitUncertain = result.CommitUncertain
        }, result.Statements.Count);
    }

    /// <summary>
    /// sql ddl JSON 輸出結構。RolledBack／DatabaseChanged／Committed 在 COMMIT
    /// 結果不確定時皆為 null，需 JsonIgnore(Never) 覆寫全域的 WhenWritingNull 設定，
    /// 確保欄位一定出現（值為 JSON null）而非被整個省略。
    /// </summary>
    private class DdlJsonResult
    {
        public bool Valid { get; init; }
        public string? RejectReason { get; init; }
        public required IReadOnlyList<object> SyntaxErrors { get; init; }
        public required IReadOnlyList<object> Statements { get; init; }
        public string? ExecutionError { get; init; }
        public int? FailedBatchIndex { get; init; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public bool? RolledBack { get; init; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public bool? DatabaseChanged { get; init; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public bool? Committed { get; init; }

        public bool CommitUncertain { get; init; }
    }
```

（`File` 需要 `using System.IO;`——`SqlCommand.cs` 現有 using 若無則補上。）

- [ ] **Step 3: 建置驗證**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded

- [ ] **Step 4: 煙霧測試（不需資料庫）**

Run: `dotnet run --project src/Specurai.Cli -- sql ddl "TRUNCATE TABLE dbo.T1"`
Expected: 錯誤輸出含「白名單」，exit code 1（驗證離線拒絕路徑接通）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Cli/Commands/SqlCommand.cs
git commit -m "feat: 新增 sql ddl CLI 子命令（支援 --file 與 --confirm）"
```

---

### Task 7: Desktop 入口 — SQL 查詢頁「執行 DDL」

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`（:31、:125、:134、:422、:438）
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`（:53-60 執行 DML 按鈕之後）
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelDdlTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 4 `IDdlExecutionService`、既有 `ConfirmExecuteCallback`、`GetEffectiveSql()`、`AddToHistory(string)`。
- Produces: `SqlQueryDocumentViewModel.CanExecuteDdl`（bool 計算屬性）、`ExecuteDdlCommand`；DI 建構函式尾端新增選擇性參數 `IDdlExecutionService? ddlExecutionService = null`（既有呼叫端不需改也能編譯，但 MainWindowViewModel 要傳入真值）。

- [ ] **Step 1: 寫失敗測試**

```csharp
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests.ViewModels;

public class SqlQueryDocumentViewModelDdlTests
{
    private static ConnectionProfile Profile(DatabaseEnvironment environment) => new()
    {
        Name = "測試連線",
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Environment = environment
    };

    private static SqlQueryDocumentViewModel CreateViewModel(
        DatabaseEnvironment environment,
        IDdlExecutionService? ddlService)
    {
        var profile = Profile(environment);
        var connectionManager = Substitute.For<IConnectionManager>();
        connectionManager.GetEnabledProfiles().Returns([profile]);
        connectionManager.GetCurrentProfile().Returns(profile);

        return new SqlQueryDocumentViewModel(
            Substitute.For<ISqlQueryRepository>(),
            connectionManager,
            ddlExecutionService: ddlService);
    }

    [Fact(DisplayName = "設計時建構_CanExecuteDdl_應為false")]
    public void 設計時建構_CanExecuteDdl_應為false()
    {
        var vm = new SqlQueryDocumentViewModel();

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "非正式環境且服務可用_CanExecuteDdl_應為true")]
    public void 非正式環境且服務可用_CanExecuteDdl_應為true()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Staging, Substitute.For<IDdlExecutionService>());

        vm.CanExecuteDdl.Should().BeTrue();
    }

    [Fact(DisplayName = "正式環境_CanExecuteDdl_應為false")]
    public void 正式環境_CanExecuteDdl_應為false()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Production, Substitute.For<IDdlExecutionService>());

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "未注入DDL服務_CanExecuteDdl_應為false")]
    public void 未注入DDL服務_CanExecuteDdl_應為false()
    {
        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService: null);

        vm.CanExecuteDdl.Should().BeFalse();
    }

    [Fact(DisplayName = "執行DDL_使用者確認_應以confirm true執行")]
    public async Task 執行DDL_使用者確認_應以confirm執行()
    {
        var ddlService = Substitute.For<IDdlExecutionService>();
        var summary = new DdlStatementSummary
        {
            Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1
        };
        ddlService.ExecuteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new DdlExecutionResult
            {
                IsValid = true,
                Statements = [summary],
                Committed = ci.ArgAt<bool>(1)
            });

        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService);
        vm.ConfirmExecuteCallback = _ => Task.FromResult(true);
        vm.SqlText = "CREATE TABLE dbo.T1 (Id INT)";

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        await ddlService.Received(1).ExecuteAsync(
            "CREATE TABLE dbo.T1 (Id INT)", false, null, Arg.Any<CancellationToken>());
        await ddlService.Received(1).ExecuteAsync(
            "CREATE TABLE dbo.T1 (Id INT)", true, null, Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已寫入資料庫");
    }

    [Fact(DisplayName = "執行DDL_使用者取消_不應以confirm true執行")]
    public async Task 執行DDL_使用者取消_不應實際執行()
    {
        var ddlService = Substitute.For<IDdlExecutionService>();
        ddlService.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true,
                Statements = [new DdlStatementSummary
                    { Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1 }]
            });

        var vm = CreateViewModel(DatabaseEnvironment.Staging, ddlService);
        vm.ConfirmExecuteCallback = _ => Task.FromResult(false);
        vm.SqlText = "CREATE TABLE dbo.T1 (Id INT)";

        await vm.ExecuteDdlCommand.ExecuteAsync(null);

        await ddlService.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已取消");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~SqlQueryDocumentViewModelDdlTests"`
Expected: 編譯失敗（`CanExecuteDdl`／`ExecuteDdlCommand`／`ddlExecutionService` 參數不存在）

- [ ] **Step 3: 修改 `SqlQueryDocumentViewModel.cs`**

3a. 欄位（:27 `_dmlExecutionService` 之後）：

```csharp
    private readonly IDdlExecutionService? _ddlExecutionService;
```

3b. DI 建構函式（:112-131）尾端加參數並指派：

```csharp
    public SqlQueryDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IConnectionManager connectionManager,
        ISqlDryRunRepository? sqlDryRunRepository = null,
        IUpdateSqlGenerator? updateSqlGenerator = null,
        IDmlExecutionService? dmlExecutionService = null,
        IDdlExecutionService? ddlExecutionService = null)
```

建構函式本體 `_dmlExecutionService = dmlExecutionService;` 之後加：

```csharp
        _ddlExecutionService = ddlExecutionService;
```

3c. `CanExecuteDml` 屬性（:78-83）之後加計算屬性：

```csharp
    /// <summary>是否可執行 DDL：非正式環境連線且服務可用（Production 一律停用）</summary>
    public bool CanExecuteDdl =>
        _ddlExecutionService != null
        && SelectedProfile != null
        && SelectedProfile.Environment != DatabaseEnvironment.Production
        && !_selectedConnectionDisabled;
```

3d. `OnSelectedProfileChanged`（:167-207）兩處通知點各補 DDL 版本——
連線停用早退前（:193-194 之後）：

```csharp
                    OnPropertyChanged(nameof(CanExecuteDdl));
                    ExecuteDdlCommand.NotifyCanExecuteChanged();
```

方法尾端（:205-206 之後）：

```csharp
        OnPropertyChanged(nameof(CanExecuteDdl));
        ExecuteDdlCommand.NotifyCanExecuteChanged();
```

3e. `ExecuteDmlAsync` 方法（:547）之後新增命令：

```csharp
    /// <summary>
    /// 執行 DDL：先預演取得逐句摘要，經使用者確認後才 COMMIT 變更 schema。
    /// 環境閘門在 IDdlExecutionService（Production 拒絕），此處僅控制 UI 可用性與確認流程。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDdl))]
    private async Task ExecuteDdlAsync()
    {
        if (_ddlExecutionService == null || string.IsNullOrWhiteSpace(SqlText))
            return;

        if (_selectedConnectionDisabled)
        {
            StatusMessage = "此連線已停用，請改選其他連線。";
            return;
        }

        var (sql, isSelection) = GetEffectiveSql();
        var selectionNote = isSelection ? "（選取範圍）" : "";
        // 目前連線（_localConnectionString == null）傳 null 跟隨資料庫覆寫；
        // 明確選擇的其他連線傳其 Id
        var profileId = _localConnectionString == null ? (Guid?)null : SelectedProfile?.Id;

        try
        {
            IsExecuting = true;
            StatusMessage = "DDL 預演中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

            var preview = await _ddlExecutionService.ExecuteAsync(sql, confirm: false, profileId);

            if (!preview.IsValid)
            {
                StatusMessage = preview.SyntaxErrors.Count > 0
                    ? $"語法錯誤（第 {preview.SyntaxErrors[0].Line} 行第 {preview.SyntaxErrors[0].Column} 列）：{preview.SyntaxErrors[0].Message}"
                    : preview.RejectReason ?? "驗證未通過";
                return;
            }

            if (preview.ExecutionError != null)
            {
                StatusMessage = preview.ExecutionError;
                return;
            }

            var summary = string.Join("\n", preview.Statements
                .Select(s => $"{s.Index}. {s.Type} {s.ObjectName}".TrimEnd()));

            // 跟隨目前連線時，SelectedProfile 可能是開分頁當下的快照，確認訊息一律以執行當下的真實目標為準
            var targetName = _localConnectionString == null
                ? _connectionManager?.GetCurrentProfile()?.Name ?? SelectedProfile?.Name
                : SelectedProfile?.Name;
            var targetDatabase = _localConnectionString == null
                ? _connectionManager?.GetCurrentDatabase()
                : SelectedProfile?.Database;
            var targetNote = string.IsNullOrEmpty(targetDatabase) ? "" : $"（資料庫：{targetDatabase}）";

            var confirmed = ConfirmExecuteCallback != null
                && await ConfirmExecuteCallback(
                    $"將對「{targetName}」{targetNote}執行 {preview.Statements.Count} 句 DDL：\n{summary}\n" +
                    "此操作會 COMMIT 變更 schema，確定執行？");

            if (!confirmed)
            {
                StatusMessage = "已取消，資料庫未變更。";
                return;
            }

            StatusMessage = "DDL 執行中...";
            var result = await _ddlExecutionService.ExecuteAsync(sql, confirm: true, profileId);

            if (!result.IsValid)
            {
                StatusMessage = result.RejectReason ?? "驗證未通過";
                return;
            }

            if (result.ExecutionError != null)
            {
                StatusMessage = result.ExecutionError;
                return;
            }

            DryRunWarnings = summary;
            var committedNote = result.Committed ? "已寫入資料庫" : "未確認已寫入，請檢查";
            StatusMessage = $"DDL 執行完成{selectionNote}：{result.Statements.Count} 句｜{committedNote}";
            AddToHistory(sql);
        }
        catch (Exception ex)
        {
            StatusMessage = $"DDL 執行失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }
```

- [ ] **Step 4: 修改 `MainWindowViewModel.cs`**

4a. 欄位（:31 之後）：

```csharp
    private readonly IDdlExecutionService? _ddlExecutionService;
```

4b. 建構函式（:125 `IDmlExecutionService? dmlExecutionService = null` 之後）加參數：

```csharp
        IDdlExecutionService? ddlExecutionService = null)
```

（原 :125 行尾的 `)` 移到新參數；本體 :134 之後加 `_ddlExecutionService = ddlExecutionService;`）

4c. 兩處 `new SqlQueryDocumentViewModel(...)`（:422、:438）尾端加 `_ddlExecutionService`：

```csharp
        var doc = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository, _updateSqlGenerator, _dmlExecutionService, _ddlExecutionService);
```

- [ ] **Step 5: 修改 `SqlQueryDocumentView.axaml`（:60 執行 DML 按鈕之後）**

```xml
                    <Button Command="{Binding ExecuteDdlCommand}"
                            IsEnabled="{Binding CanExecuteDdl}"
                            ToolTip.Tip="執行 DDL 批次並變更 schema（白名單物件級 DDL；先預演確認；正式環境停用）">
                        <StackPanel Orientation="Horizontal" Spacing="5">
                            <TextBlock Text="🛠️" FontSize="14"/>
                            <TextBlock Text="執行 DDL"/>
                        </StackPanel>
                    </Button>
```

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~SqlQueryDocumentViewModelDdlTests"`
Expected: 6 PASS

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelDdlTests.cs
git commit -m "feat: Desktop SQL 查詢頁新增執行 DDL（預演確認後 COMMIT，正式環境停用）"
```

---

### Task 8: 全套驗證、手動資料庫驗證與文件

**Files:**
- Modify: `docs/McpServerREADME.md`（`execute_sql` 工具說明段落之後）

**Interfaces:**
- Consumes: 前七個任務的全部產出。
- Produces: 全綠測試套件、更新後的 MCP 工具文件。

- [ ] **Step 1: 全套測試**

Run: `dotnet test`
Expected: 全數 PASS，無既有測試被打破

- [ ] **Step 2: 手動資料庫驗證（非正式環境連線）**

依序在 CLI 對測試庫執行，驗證交易行為（自動化測試不涵蓋的部分）：

1. 預演不落地：`dotnet run --project src/Specurai.Cli -- sql ddl "CREATE TABLE dbo.DdlSmokeTest (Id INT)"` → 回報預演成功後，用 `sql query "SELECT OBJECT_ID('dbo.DdlSmokeTest')"` 確認為 NULL（未建立）。
2. confirm 落地：同句加 `--confirm` → OBJECT_ID 非 NULL。
3. 多批次原子性：`sql ddl --confirm --file <含三批的 script，第三批故意 DROP 不存在的表>` → 回報第 3 批失敗，且前兩批的物件不存在（整批回滾）。
4. 正式環境拒絕：切到 Production 連線執行任一 DDL → 回報「正式環境，不允許執行 DDL」。
5. 清理：`sql ddl --confirm "DROP TABLE dbo.DdlSmokeTest"`。

Expected: 五項全數符合；任何不符即為 bug，回頭修正對應任務。

- [ ] **Step 3: 更新 `docs/McpServerREADME.md`**

在 `execute_sql` 說明段落之後，比照其格式新增 `execute_ddl` 段落，內容涵蓋：

- 用途：執行白名單物件級 DDL 批次（CREATE/ALTER/DROP 的 TABLE、INDEX、VIEW、PROCEDURE、FUNCTION、TRIGGER、SCHEMA；可含多句與 GO）。
- 安全機制：Production 連線一律拒絕；庫級操作、TRUNCATE、權限語句、EXEC、DML 拒絕（fail-closed）；預設 confirm=false 僅預演（交易內執行後回滾），confirm:true 才 COMMIT；整批單一交易，任一批失敗全部回滾。
- 參數：`script`（DDL script）、`confirm`（預設 false）。
- 輸出欄位：`Valid`、`Statements`（逐句摘要）、`ExecutionError`、`FailedBatchIndex`、`Committed`、`DatabaseChanged`、`CommitUncertain`（三態規則：結果不確定時 `Committed`／`DatabaseChanged` 為 null）。

（實際行文比照該檔既有工具段落的格式與詳略。）

- [ ] **Step 4: Commit**

```bash
git add docs/McpServerREADME.md
git commit -m "docs: 補充 execute_ddl MCP 工具說明"
```

- [ ] **Step 5: 程式碼審查**

依專案規範，以 `superpowers:requesting-code-review` 對本次功能全部 commit 進行審查後再回報完成。
