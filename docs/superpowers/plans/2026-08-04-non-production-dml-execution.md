# 非正式環境 DML 執行通道 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 Development/Testing/Staging 環境的連線可以真正 commit 單一 DML（INSERT/UPDATE/DELETE），Production 一律拒絕；同時把查詢路徑改成 ScriptDom AST 驗證的 SELECT-only，堵掉 CTE 與多句批次繞過漏洞。

**Architecture:** 環境閘門集中在 Application 層新服務 `IDmlExecutionService`（Production 拒絕、confirm 分流）；Infrastructure 沿用 `SqlDryRunRepository` 既有管線，只在交易收尾分支 ROLLBACK / COMMIT；三入口（MCP / CLI / Desktop）都呼叫中央服務。查詢路徑新增 `SqlReadOnlyValidator`（ScriptDom）在 `SqlQueryRepository` 執行前把關。

**Tech Stack:** .NET 8、Microsoft.SqlServer.TransactSql.ScriptDom（Infrastructure 已引用）、Dapper、xUnit + NSubstitute + FluentAssertions、CommunityToolkit.Mvvm、System.CommandLine、MCP SDK。

**Spec:** `docs/superpowers/specs/2026-08-04-non-production-dml-execution-design.md`

## Global Constraints

- 一律以繁體中文撰寫 UI 文字、註解、commit 訊息。
- Clean Architecture 相依方向：Domain ← Application ← Infrastructure ← (Desktop/McpServer/Cli)。
- Repository 介面放 `Specurai.Domain/Interfaces/`，實作放 `Specurai.Infrastructure/Repositories/`。
- ViewModel 用 CommunityToolkit.Mvvm（`[ObservableProperty]`、`[RelayCommand]`），需保留無參數設計時建構函式。
- TDD：先寫失敗測試再實作。測試命名 `[Method]_[Condition]_[Expected]`（DisplayName 繁中）。
- git add 一律逐檔指名，禁止 `git add -A` / `git add .`。
- **不動範圍**：`dry_run_sql`（MCP）/ `sql dry-run`（CLI）/ Desktop「Dry Run」按鈕的介面、行為、輸出格式；`SqlDryRunAnalyzer` 內容；`migration_*` 等其他工具。

---

### Task 1: Domain — `ISqlDmlExecuteRepository` 介面與 `DryRunResult.Committed`

**Files:**
- Create: `src/Specurai.Domain/Interfaces/ISqlDmlExecuteRepository.cs`
- Modify: `src/Specurai.Domain/Entities/DryRunResult.cs`（class `DryRunResult` 末尾加一個屬性）

**Interfaces:**
- Consumes: 既有 `DryRunResult`（`src/Specurai.Domain/Entities/DryRunResult.cs`）。
- Produces: `ISqlDmlExecuteRepository.ExecuteAsync(string sql, CancellationToken)` 與 `ExecuteAsync(string sql, string connectionString, CancellationToken)`，皆回傳 `Task<DryRunResult>`；`DryRunResult.Committed`（bool，init，預設 false）。後續 Task 4、5、7、8、9 都依賴這兩個簽章。

- [ ] **Step 1: 建立介面檔**

```csharp
// src/Specurai.Domain/Interfaces/ISqlDmlExecuteRepository.cs
using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// SQL DML 執行 Repository 介面：實際執行單一 DML（INSERT/UPDATE/DELETE），
/// 在交易中執行並 COMMIT，回傳影響筆數與前後資料對照。
/// 環境限制（Production 拒絕）由 Application 層的 IDmlExecutionService 把關，
/// 呼叫端不應繞過該服務直接使用本介面。
/// </summary>
public interface ISqlDmlExecuteRepository
{
    /// <summary>
    /// 使用預設連線實際執行單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> ExecuteAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 使用指定連線字串實際執行單一 DML 陳述式
    /// </summary>
    Task<DryRunResult> ExecuteAsync(string sql, string connectionString, CancellationToken ct = default);
}
```

- [ ] **Step 2: `DryRunResult` 加 `Committed` 屬性**

在 `src/Specurai.Domain/Entities/DryRunResult.cs` 的 `DryRunResult` 類別中，`ExecutionError` 屬性之後加：

```csharp
    /// <summary>是否已 COMMIT 寫入資料庫（dry run 一律 false）</summary>
    public bool Committed { get; init; }
```

- [ ] **Step 3: 建置驗證**

Run: `dotnet build`
Expected: 成功，0 錯誤。

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Domain/Interfaces/ISqlDmlExecuteRepository.cs src/Specurai.Domain/Entities/DryRunResult.cs
git commit -m "feat: 新增 DML 執行 Repository 介面與 Committed 結果欄位"
```

---

### Task 2: Infrastructure — `SqlReadOnlyValidator`（ScriptDom SELECT-only 驗證）

**Files:**
- Create: `src/Specurai.Infrastructure/Services/SqlReadOnlyValidator.cs`
- Test: `tests/Specurai.Infrastructure.Tests/SqlReadOnlyValidatorTests.cs`

**Interfaces:**
- Consumes: `Microsoft.SqlServer.TransactSql.ScriptDom`（Infrastructure 專案既有套件，參考 `SqlDryRunAnalyzer.cs` 用法）。
- Produces: `SqlReadOnlyValidator.Validate(string sql)` 回傳 `SqlReadOnlyValidationResult { bool IsValid, string? RejectReason }`。Task 3 依賴此簽章。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Specurai.Infrastructure.Tests/SqlReadOnlyValidatorTests.cs
using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests;

public class SqlReadOnlyValidatorTests
{
    private readonly SqlReadOnlyValidator _validator = new();

    [Theory(DisplayName = "Validate_唯讀語句_應放行")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) SELECT * FROM cte")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("DECLARE @x INT; SET @x = 1; SELECT @x")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT * FROM sys.tables")]
    [InlineData("SET NOCOUNT ON; SELECT 1")]
    public void Validate_ReadOnlyStatements_ShouldPass(string sql)
    {
        var result = _validator.Validate(sql);
        result.IsValid.Should().BeTrue();
    }

    [Theory(DisplayName = "Validate_寫入或不允許語句_應拒絕")]
    [InlineData("INSERT INTO T VALUES (1)")]
    [InlineData("UPDATE T SET A = 1")]
    [InlineData("DELETE FROM T")]
    [InlineData("MERGE INTO T USING S ON T.Id = S.Id WHEN MATCHED THEN UPDATE SET A = 1;")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) DELETE FROM cte")]
    [InlineData("SELECT 1; DELETE FROM T")]
    [InlineData("SELECT * INTO T2 FROM T")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE dbo.MyProc")]
    [InlineData("DROP TABLE T")]
    [InlineData("TRUNCATE TABLE T")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("ALTER TABLE T ADD B INT")]
    public void Validate_WriteOrDisallowedStatements_ShouldReject(string sql)
    {
        var result = _validator.Validate(sql);
        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "Validate_語法錯誤_應拒絕並含行列資訊")]
    public void Validate_SyntaxError_ShouldRejectWithLocation()
    {
        var result = _validator.Validate("SELEC * FROM T");
        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("語法錯誤");
    }

    [Fact(DisplayName = "Validate_空字串_應拒絕")]
    public void Validate_Empty_ShouldReject()
    {
        var result = _validator.Validate("   ");
        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlReadOnlyValidatorTests"`
Expected: 編譯失敗（`SqlReadOnlyValidator` 不存在）。

- [ ] **Step 3: 實作 validator**

```csharp
// src/Specurai.Infrastructure/Services/SqlReadOnlyValidator.cs
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 唯讀 SQL 驗證結果
/// </summary>
public class SqlReadOnlyValidationResult
{
    /// <summary>批次是否僅含允許的唯讀語句</summary>
    public required bool IsValid { get; init; }

    /// <summary>拒絕原因（通過時為 null）</summary>
    public string? RejectReason { get; init; }
}

/// <summary>
/// 唯讀 SQL 驗證器：以 ScriptDom 解析整個批次，逐句白名單檢查（純離線，不碰資料庫）。
/// 允許：SELECT（不含 INTO）、DECLARE、變數 SET、工作階段 SET 選項、SET ISOLATION LEVEL。
/// 其餘（DML/DDL/EXEC/MERGE/TRUNCATE 等）一律拒絕；EXEC 因無法靜態判斷 SP 內容是否唯讀，一律拒絕。
/// </summary>
public class SqlReadOnlyValidator
{
    public SqlReadOnlyValidationResult Validate(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            var e = parseErrors[0];
            return new SqlReadOnlyValidationResult
            {
                IsValid = false,
                RejectReason = $"SQL 語法錯誤（第 {e.Line} 行第 {e.Column} 列）：{e.Message}"
            };
        }

        var statements = ((TSqlScript)fragment).Batches
            .SelectMany(b => b.Statements)
            .ToList();

        if (statements.Count == 0)
        {
            return new SqlReadOnlyValidationResult
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        foreach (var statement in statements)
        {
            var reason = CheckStatement(statement);
            if (reason != null)
                return new SqlReadOnlyValidationResult { IsValid = false, RejectReason = reason };
        }

        return new SqlReadOnlyValidationResult { IsValid = true };
    }

    private static string? CheckStatement(TSqlStatement statement) => statement switch
    {
        SelectStatement { Into: not null } =>
            "SELECT ... INTO 會建立資料表，查詢僅支援唯讀操作。",
        SelectStatement => null,
        DeclareVariableStatement => null,
        SetVariableStatement => null,
        PredicateSetStatement => null,
        SetTransactionIsolationLevelStatement => null,
        _ =>
            $"查詢僅支援 SELECT 等唯讀操作（偵測到 {DescribeStatement(statement)}）；" +
            "資料異動請改用 DML 執行通道（dry run 預演／execute 執行）。"
    };

    private static string DescribeStatement(TSqlStatement statement)
        => statement.GetType().Name.Replace("Statement", "");
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlReadOnlyValidatorTests"`
Expected: 全部 PASS。若 `MERGE`、`SET NOCOUNT ON` 等個案因 ScriptDom AST 型別對應不符而失敗，以測試回饋修正 `CheckStatement` 的 pattern（例如 `PredicateSetStatement` 涵蓋範圍），白名單原則不變：只放行可證明唯讀的型別。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Services/SqlReadOnlyValidator.cs tests/Specurai.Infrastructure.Tests/SqlReadOnlyValidatorTests.cs
git commit -m "feat: 新增 ScriptDom 唯讀 SQL 驗證器"
```

---

### Task 3: Infrastructure — `SqlQueryRepository` 掛上唯讀驗證

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs`
- Test: `tests/Specurai.Infrastructure.Tests/SqlQueryRepositoryReadOnlyTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 2 的 `SqlReadOnlyValidator.Validate(string)`。
- Produces: `ExecuteQueryAsync` / `ExecuteQueryWithSchemaAsync` 對非唯讀 SQL 丟 `InvalidOperationException`，訊息即 validator 的 `RejectReason`。此行為 Task 7（MCP）沿用。

- [ ] **Step 1: 寫失敗測試**

驗證重點：非唯讀 SQL 要**在連線前**被擋下——用一個必然連不上的連線字串，若丟出的是 `InvalidOperationException`（而非 SqlException/逾時），證明驗證先於連線。

```csharp
// tests/Specurai.Infrastructure.Tests/SqlQueryRepositoryReadOnlyTests.cs
using FluentAssertions;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests;

public class SqlQueryRepositoryReadOnlyTests
{
    // 連不上的假連線字串：若 SQL 在連線前就被擋，測試不需要真資料庫
    private const string FakeConnectionString =
        "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=True";

    [Theory(DisplayName = "ExecuteQueryAsync_非唯讀SQL_應在連線前丟InvalidOperationException")]
    [InlineData("DELETE FROM T")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) DELETE FROM cte")]
    [InlineData("SELECT 1; DELETE FROM T")]
    [InlineData("SELECT * INTO T2 FROM T")]
    [InlineData("EXEC sp_who")]
    public async Task ExecuteQueryAsync_NonReadOnly_ShouldThrowBeforeConnecting(string sql)
    {
        var repo = new SqlQueryRepository(() => FakeConnectionString);

        var act = () => repo.ExecuteQueryAsync(sql, FakeConnectionString);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("唯讀") || e.Message.Contains("SELECT"));
    }

    [Fact(DisplayName = "ExecuteQueryWithSchemaAsync_非唯讀SQL_應在連線前丟InvalidOperationException")]
    public async Task ExecuteQueryWithSchemaAsync_NonReadOnly_ShouldThrowBeforeConnecting()
    {
        var repo = new SqlQueryRepository(() => FakeConnectionString);

        var act = () => repo.ExecuteQueryWithSchemaAsync("UPDATE T SET A = 1", FakeConnectionString);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

注意：`ExecuteQueryWithSchemaAsync` 的參數簽章以 `ISqlQueryRepository` 實際定義為準，撰寫測試前先看介面檔。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlQueryRepositoryReadOnlyTests"`
Expected: FAIL（目前丟的是 SqlException 連線失敗，不是 InvalidOperationException）。

- [ ] **Step 3: 實作驗證攔截**

在 `SqlQueryRepository` 加入：

```csharp
using Specurai.Infrastructure.Services;

// 類別欄位
private static readonly SqlReadOnlyValidator ReadOnlyValidator = new();

/// <summary>
/// 唯讀驗證：非 SELECT 等唯讀語句一律擋下（在開啟連線之前）
/// </summary>
private static void EnsureReadOnly(string sql)
{
    var validation = ReadOnlyValidator.Validate(sql);
    if (!validation.IsValid)
        throw new InvalidOperationException(validation.RejectReason);
}
```

然後在**所有實際開連線執行使用者 SQL 的核心 overload** 的第一行呼叫 `EnsureReadOnly(sql)`：

1. `ExecuteQueryAsync(string sql, string connectionString, CancellationToken ct)` — 開頭加 `EnsureReadOnly(sql);`
2. `ExecuteQueryWithSchemaAsync` 的 connectionString overload — 開頭加 `EnsureReadOnly(sql);`

檢查無 connectionString 的 overload 是否委派到上述核心 overload（`ExecuteQueryAsync(sql, ct)` 已委派）；若 `ExecuteQueryWithSchemaAsync(sql, ct)` 沒有委派而是自己開連線，也在它開頭加 `EnsureReadOnly(sql);`。

**不要**在 `GetColumnDescriptionsAsync`、`SearchColumnsAsync` 等內部固定 SQL 的方法加驗證（SQL 非使用者輸入）。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlQueryRepositoryReadOnlyTests"`
Expected: 全部 PASS。

- [ ] **Step 5: 跑全部 Infrastructure 測試確認無回歸**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 全部 PASS。

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs tests/Specurai.Infrastructure.Tests/SqlQueryRepositoryReadOnlyTests.cs
git commit -m "fix: 查詢路徑改用 ScriptDom 唯讀驗證，堵住 CTE 與多句批次繞過"
```

---

### Task 4: Infrastructure — `SqlDryRunRepository` 泛化出 COMMIT 分支

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/SqlDryRunRepository.cs`
- Test: `tests/Specurai.Infrastructure.Tests/SqlDmlExecuteRepositoryTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 1 的 `ISqlDmlExecuteRepository`、`DryRunResult.Committed`。
- Produces: `SqlDryRunRepository` 同時實作 `ISqlDryRunRepository` 與 `ISqlDmlExecuteRepository`。`ExecuteAsync` 成功時回傳 `Committed = true`；驗證失敗或 `ExecutionError` 時 `Committed = false`。Task 5、6 依賴。

- [ ] **Step 1: 寫失敗測試（離線可驗證的部分）**

```csharp
// tests/Specurai.Infrastructure.Tests/SqlDmlExecuteRepositoryTests.cs
using FluentAssertions;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests;

public class SqlDmlExecuteRepositoryTests
{
    private const string FakeConnectionString =
        "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=True";

    [Fact(DisplayName = "SqlDryRunRepository_應同時實作執行介面")]
    public void SqlDryRunRepository_ShouldImplementExecuteInterface()
    {
        var repo = new SqlDryRunRepository(() => FakeConnectionString);
        repo.Should().BeAssignableTo<ISqlDmlExecuteRepository>();
    }

    [Theory(DisplayName = "ExecuteAsync_非單一DML_應離線拒絕且未Commit")]
    [InlineData("SELECT * FROM T")]
    [InlineData("DELETE FROM A; DELETE FROM B")]
    [InlineData("DROP TABLE T")]
    public async Task ExecuteAsync_NotSingleDml_ShouldRejectOfflineWithoutCommit(string sql)
    {
        ISqlDmlExecuteRepository repo = new SqlDryRunRepository(() => FakeConnectionString);

        // 離線拒絕：不會嘗試連線（假連線字串連不上，若嘗試連線會丟 SqlException）
        var result = await repo.ExecuteAsync(sql, FakeConnectionString);

        result.IsValid.Should().BeFalse();
        result.Committed.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_語法錯誤_應回傳錯誤明細且未Commit")]
    public async Task ExecuteAsync_SyntaxError_ShouldReturnErrorsWithoutCommit()
    {
        ISqlDmlExecuteRepository repo = new SqlDryRunRepository(() => FakeConnectionString);

        var result = await repo.ExecuteAsync("UPDATE T SET WHERE", FakeConnectionString);

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
        result.Committed.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlDmlExecuteRepositoryTests"`
Expected: 編譯失敗（`SqlDryRunRepository` 未實作 `ISqlDmlExecuteRepository`）。

- [ ] **Step 3: 實作 commit 分支**

改寫 `SqlDryRunRepository`，原則：**dry run 的對外行為一個位元組都不變**，只是把「交易收尾」參數化。

1. 類別宣告改為：

```csharp
public class SqlDryRunRepository : ISqlDryRunRepository, ISqlDmlExecuteRepository
```

2. 新增 `ExecuteAsync` 兩個 overload，與 `DryRunAsync` 對稱：

```csharp
    public async Task<DryRunResult> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await ExecuteAsync(sql, connectionString, ct);
    }

    public Task<DryRunResult> ExecuteAsync(string sql, string connectionString, CancellationToken ct = default)
        => RunAsync(sql, connectionString, commit: true, ct);
```

3. 把現有 `DryRunAsync(string sql, string connectionString, ...)` 的方法本體改名為私有核心：

```csharp
    public Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default)
        => RunAsync(sql, connectionString, commit: false, ct);

    private async Task<DryRunResult> RunAsync(string sql, string connectionString, bool commit, CancellationToken ct)
    {
        // …原 DryRunAsync 本體，差異如下…
    }
```

`RunAsync` 相對原本體的差異：
- IDENTITY 警告只在 dry run 加（commit 時序號本來就會被使用）：

```csharp
        var warnings = new List<string>();
        if (!commit && analysis.StatementType == DryRunStatementType.Insert)
            warnings.Add("若目標資料表有 IDENTITY 欄位，序號在回滾後仍會被消耗。");
```

- 呼叫 `ExecutePreviewAsync` / `ExecuteCountOnlyAsync` 時多傳 `commit`。

4. `ExecutePreviewAsync` 改交易收尾邏輯（簽章加 `bool commit`）——原本 `finally` 一律 rollback，改為成功才 commit、其餘 rollback：

```csharp
    private static async Task<DryRunResult> ExecutePreviewAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, bool commit, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };

            var preview = new DataTable();
            var total = 0;
            // reader 必須在 Commit 前關閉，因此用區塊限制其生命週期
            {
                using var reader = await command.ExecuteReaderAsync(ct);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    // 未別名的 deleted.*/inserted.* 會產生重複欄位名稱，加序號避免 DataTable 衝突
                    if (preview.Columns.Contains(name))
                        name = $"{name}_{i}";
                    preview.Columns.Add(name, typeof(object));
                }

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
            }

            if (commit)
            {
                // 交易收尾不使用呼叫端的取消權杖，確保必定送出
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
            }

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = total,
                PreviewTable = preview,
                PreviewTruncated = total > PreviewRowLimit,
                Warnings = warnings,
                Committed = committed
            };
        }
        finally
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
        }
    }
```

5. `ExecuteCountOnlyAsync` 同樣加 `bool commit` 參數、同樣的 committed 旗標模式；其 `catch (SqlException)` 回傳 `ExecutionError` 結果時不 commit（committed 保持 false，finally rollback），錯誤訊息在 commit 模式下改為「執行失敗：{ex.Message}」、dry run 模式維持原文「此語句實際執行將會失敗：{ex.Message}」：

```csharp
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = commit
                    ? $"執行失敗（已回滾）：{ex.Message}"
                    : $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
```

6. `RunAsync` 外層的 `catch (SqlException ex)`（OUTPUT 注入執行失敗那個）同樣依 `commit` 切換訊息文字。

7. 類別頂端 XML 註解更新為描述雙介面職責。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 新測試 PASS，既有測試零回歸。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/SqlDryRunRepository.cs tests/Specurai.Infrastructure.Tests/SqlDmlExecuteRepositoryTests.cs
git commit -m "feat: SqlDryRunRepository 泛化交易收尾，新增 COMMIT 執行分支"
```

---

### Task 5: Application — `IDmlExecutionService` 環境閘門與 confirm 分流

**Files:**
- Create: `src/Specurai.Application/Services/IDmlExecutionService.cs`
- Create: `src/Specurai.Application/Services/DmlExecutionService.cs`
- Test: `tests/Specurai.Application.Tests/DmlExecutionServiceTests.cs`

**Interfaces:**
- Consumes: `IConnectionManager.GetCurrentProfile() / GetEnabledProfiles() / GetCurrentConnectionString() / GetConnectionString(Guid)`；Task 1 的 `ISqlDmlExecuteRepository`；既有 `ISqlDryRunRepository`。
- Produces: `IDmlExecutionService.ExecuteAsync(string sql, bool confirm, Guid? profileId = null, CancellationToken ct = default)` 回傳 `Task<DryRunResult>`。Task 6～9 依賴此簽章。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Specurai.Application.Tests/DmlExecutionServiceTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Tests;

public class DmlExecutionServiceTests
{
    private readonly IConnectionManager _connectionManager = Substitute.For<IConnectionManager>();
    private readonly ISqlDryRunRepository _dryRunRepo = Substitute.For<ISqlDryRunRepository>();
    private readonly ISqlDmlExecuteRepository _executeRepo = Substitute.For<ISqlDmlExecuteRepository>();

    private DmlExecutionService CreateService()
        => new(_connectionManager, _dryRunRepo, _executeRepo);

    private static ConnectionProfile Profile(DatabaseEnvironment env, string name = "測試連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p",
        Environment = env
    };

    [Fact(DisplayName = "ExecuteAsync_正式環境_應拒絕且不呼叫任何Repository")]
    public async Task ExecuteAsync_Production_ShouldRejectWithoutCallingRepositories()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Production, "正式庫"));

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: true);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
        result.Committed.Should().BeFalse();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
        await _executeRepo.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Theory(DisplayName = "ExecuteAsync_非正式環境未confirm_應走DryRun")]
    [InlineData(DatabaseEnvironment.Development)]
    [InlineData(DatabaseEnvironment.Testing)]
    [InlineData(DatabaseEnvironment.Staging)]
    public async Task ExecuteAsync_NonProductionWithoutConfirm_ShouldDryRun(DatabaseEnvironment env)
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(env));
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _dryRunRepo.DryRunAsync("UPDATE T SET A = 1", "conn", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, AffectedRowCount = 3 });

        var result = await CreateService().ExecuteAsync("UPDATE T SET A = 1", confirm: false);

        result.AffectedRowCount.Should().Be(3);
        await _executeRepo.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_非正式環境confirm_應走Execute")]
    public async Task ExecuteAsync_NonProductionWithConfirm_ShouldExecute()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Staging));
        _connectionManager.GetCurrentConnectionString().Returns("conn");
        _executeRepo.ExecuteAsync("DELETE FROM T WHERE Id = 1", "conn", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, AffectedRowCount = 1, Committed = true });

        var result = await CreateService().ExecuteAsync("DELETE FROM T WHERE Id = 1", confirm: true);

        result.Committed.Should().BeTrue();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId_應以該連線環境與連線字串為準")]
    public async Task ExecuteAsync_WithProfileId_ShouldUseThatProfile()
    {
        var profile = Profile(DatabaseEnvironment.Testing, "測試2");
        _connectionManager.GetEnabledProfiles().Returns([profile]);
        _connectionManager.GetConnectionString(profile.Id).Returns("conn2");
        _dryRunRepo.DryRunAsync("DELETE FROM T", "conn2", Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true });

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false, profile.Id);

        result.IsValid.Should().BeTrue();
        await _dryRunRepo.Received(1).DryRunAsync("DELETE FROM T", "conn2", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteAsync_指定profileId為正式環境_應拒絕")]
    public async Task ExecuteAsync_WithProductionProfileId_ShouldReject()
    {
        var profile = Profile(DatabaseEnvironment.Production, "正式庫");
        _connectionManager.GetEnabledProfiles().Returns([profile]);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: true, profile.Id);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("正式環境");
    }

    [Fact(DisplayName = "ExecuteAsync_找不到profile_應拒絕不靜默落回目前連線")]
    public async Task ExecuteAsync_ProfileNotFound_ShouldRejectWithoutFallback()
    {
        _connectionManager.GetEnabledProfiles().Returns([]);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false, Guid.NewGuid());

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrEmpty();
        await _dryRunRepo.DidNotReceiveWithAnyArgs().DryRunAsync(default!, default!, default);
    }

    [Fact(DisplayName = "ExecuteAsync_無目前連線_應拒絕")]
    public async Task ExecuteAsync_NoCurrentProfile_ShouldReject()
    {
        _connectionManager.GetCurrentProfile().Returns((ConnectionProfile?)null);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false);

        result.IsValid.Should().BeFalse();
    }

    [Fact(DisplayName = "ExecuteAsync_連線字串取不到_應拒絕")]
    public async Task ExecuteAsync_NoConnectionString_ShouldReject()
    {
        _connectionManager.GetCurrentProfile().Returns(Profile(DatabaseEnvironment.Development));
        _connectionManager.GetCurrentConnectionString().Returns((string?)null);

        var result = await CreateService().ExecuteAsync("DELETE FROM T", confirm: false);

        result.IsValid.Should().BeFalse();
    }
}
```

注意：`ConnectionProfile` 的必要屬性以實際定義為準（參考 `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs` 的 `SampleProfile`），若 `Id` 非自動產生請補上。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~DmlExecutionServiceTests"`
Expected: 編譯失敗（型別不存在）。

- [ ] **Step 3: 實作介面與服務**

```csharp
// src/Specurai.Application/Services/IDmlExecutionService.cs
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// DML 執行服務：環境閘門與 confirm 分流的唯一所在。
/// Production 連線一律拒絕（不連資料庫）；
/// confirm=false 走 dry run 預演（一律回滾）、confirm=true 走實際執行（COMMIT）。
/// </summary>
public interface IDmlExecutionService
{
    /// <summary>
    /// 執行單一 DML（INSERT/UPDATE/DELETE）
    /// </summary>
    /// <param name="sql">單一 DML 陳述式</param>
    /// <param name="confirm">false 僅預演；true 實際執行並 COMMIT</param>
    /// <param name="profileId">目標連線設定檔（null 表示目前連線，跟隨資料庫覆寫）</param>
    Task<DryRunResult> ExecuteAsync(string sql, bool confirm, Guid? profileId = null, CancellationToken ct = default);
}
```

```csharp
// src/Specurai.Application/Services/DmlExecutionService.cs
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// DML 執行服務實作
/// </summary>
public class DmlExecutionService : IDmlExecutionService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ISqlDryRunRepository _dryRunRepository;
    private readonly ISqlDmlExecuteRepository _executeRepository;

    public DmlExecutionService(
        IConnectionManager connectionManager,
        ISqlDryRunRepository dryRunRepository,
        ISqlDmlExecuteRepository executeRepository)
    {
        _connectionManager = connectionManager;
        _dryRunRepository = dryRunRepository;
        _executeRepository = executeRepository;
    }

    public async Task<DryRunResult> ExecuteAsync(
        string sql, bool confirm, Guid? profileId = null, CancellationToken ct = default)
    {
        // 解析目標連線：指定 profileId 時不得靜默落回目前連線
        var profile = profileId == null
            ? _connectionManager.GetCurrentProfile()
            : _connectionManager.GetEnabledProfiles().FirstOrDefault(p => p.Id == profileId.Value);

        if (profile == null)
            return Reject(profileId == null
                ? "未設定目前連線，無法執行 DML。"
                : "找不到指定的連線設定（可能已停用），請改選其他連線。");

        if (profile.Environment == DatabaseEnvironment.Production)
            return Reject($"連線「{profile.Name}」為正式環境，不允許執行 DML；如需預演請改用 dry run。");

        var connectionString = profileId == null
            ? _connectionManager.GetCurrentConnectionString()
            : _connectionManager.GetConnectionString(profileId.Value);

        if (string.IsNullOrEmpty(connectionString))
            return Reject("無法取得連線字串，請確認連線設定。");

        return confirm
            ? await _executeRepository.ExecuteAsync(sql, connectionString, ct)
            : await _dryRunRepository.DryRunAsync(sql, connectionString, ct);
    }

    private static DryRunResult Reject(string reason)
        => new() { IsValid = false, RejectReason = reason };
}
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj`
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Application/Services/IDmlExecutionService.cs src/Specurai.Application/Services/DmlExecutionService.cs tests/Specurai.Application.Tests/DmlExecutionServiceTests.cs
git commit -m "feat: 新增 DML 執行服務，集中環境閘門與 confirm 分流"
```

---

### Task 6: DI 註冊（三入口共用的 ServiceRegistration）

**Files:**
- Modify: `src/Specurai.Infrastructure/ServiceRegistration.cs:33-36` 附近

**Interfaces:**
- Consumes: Task 4 的雙介面 `SqlDryRunRepository`、Task 5 的 `DmlExecutionService`。
- Produces: DI 容器可解析 `ISqlDmlExecuteRepository` 與 `IDmlExecutionService`（Task 7、8、9 依賴）。

- [ ] **Step 1: 改註冊**

把現有的：

```csharp
        services.AddSingleton<ISqlDryRunRepository>(sp =>
            new SqlDryRunRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
```

改為（同一實例掛兩個介面，並註冊執行服務）：

```csharp
        services.AddSingleton<SqlDryRunRepository>(sp =>
            new SqlDryRunRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<ISqlDryRunRepository>(sp => sp.GetRequiredService<SqlDryRunRepository>());
        services.AddSingleton<ISqlDmlExecuteRepository>(sp => sp.GetRequiredService<SqlDryRunRepository>());
        services.AddSingleton<IDmlExecutionService>(sp => new DmlExecutionService(
            sp.GetRequiredService<IConnectionManager>(),
            sp.GetRequiredService<ISqlDryRunRepository>(),
            sp.GetRequiredService<ISqlDmlExecuteRepository>()));
```

`IDmlExecutionService` 的註冊位置放在該檔 Application 服務註冊區（參考 `ColumnSearchService` 註冊處的分區慣例）；必要時補 `using Specurai.Application.Services;`。

確認 Desktop / McpServer / Cli 三個 host 都是透過這個 `ServiceRegistration` 註冊（搜尋各 host Program.cs 的呼叫）；若某 host 有自己的重複註冊，同步修改。

- [ ] **Step 2: 建置 + 全測試**

Run: `dotnet build && dotnet test`
Expected: 建置成功、全部測試 PASS。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Infrastructure/ServiceRegistration.cs
git commit -m "feat: 註冊 DML 執行 Repository 與服務"
```

---

### Task 7: McpServer — `execute_sql` 工具 + `execute_readonly_sql` 改走 validator

**Files:**
- Modify: `src/Specurai.McpServer/Tools/SqlTools.cs`
- Test: `tests/Specurai.McpServer.Tests/SqlToolsTests.cs`（加測試）
- Test: `tests/Specurai.McpServer.Tests/ConfirmGateTests.cs`（加 InlineData）

**Interfaces:**
- Consumes: Task 5 的 `IDmlExecutionService.ExecuteAsync(sql, confirm, profileId, ct)`；Task 3 的 repository 端唯讀驗證（丟 `InvalidOperationException`）。
- Produces: MCP 工具 `execute_sql`（方法名 `ExecuteSql`，最後參數 `bool confirm = false`）。

- [ ] **Step 1: 寫失敗測試**

`ConfirmGateTests.cs` 的 Theory 加一行 InlineData：

```csharp
    [InlineData(typeof(SqlTools), nameof(SqlTools.ExecuteSql))]
```

`SqlToolsTests.cs` 加測試：

```csharp
    [Fact(DisplayName = "execute_sql: confirm=false 應以預演模式呼叫服務並附確認提示")]
    public async Task ExecuteSql_ConfirmFalse_ShouldPreviewWithHint()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, null, Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                AffectedRowCount = 2
            });

        var result = await SqlTools.ExecuteSql(service, "DELETE FROM T WHERE Id < 3", confirm: false);

        await service.Received(1).ExecuteAsync("DELETE FROM T WHERE Id < 3", false, null, Arg.Any<CancellationToken>());
        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Committed").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
        result.Should().Contain("confirm:true");
    }

    [Fact(DisplayName = "execute_sql: confirm=true 應實際執行並回報已寫入")]
    public async Task ExecuteSql_ConfirmTrue_ShouldExecuteAndReportCommitted()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), true, null, Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                AffectedRowCount = 2,
                Committed = true
            });

        var result = await SqlTools.ExecuteSql(service, "DELETE FROM T WHERE Id < 3", confirm: true);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Committed").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "execute_sql: 正式環境拒絕應回傳原因")]
    public async Task ExecuteSql_ProductionRejected_ShouldReturnReason()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), Arg.Any<bool>(), null, Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = false, RejectReason = "連線「正式庫」為正式環境，不允許執行 DML" });

        var result = await SqlTools.ExecuteSql(service, "DELETE FROM T", confirm: true);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("RejectReason").GetString().Should().Contain("正式環境");
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "execute_readonly_sql: 唯讀驗證擋下的例外應回傳訊息")]
    public async Task ExecuteReadonlySql_ValidatorRejected_ShouldReturnMessage()
    {
        var repo = Substitute.For<ISqlQueryRepository>();
        repo.ExecuteQueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DataTable>>(_ => throw new InvalidOperationException("查詢僅支援 SELECT 等唯讀操作"));

        var result = await SqlTools.ExecuteReadonlySql(repo, "WITH cte AS (SELECT 1 AS A) DELETE FROM cte");

        result.Should().Contain("唯讀");
    }
```

測試檔需補 `using Specurai.Application.Services;`。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 編譯失敗（`ExecuteSql` 不存在）。

- [ ] **Step 3: 實作工具**

`SqlTools.cs`：

1. `ExecuteReadonlySql` 移除 `dangerousKeywords` 迴圈與 `normalizedSql` 變數（Task 3 的 repository 驗證已把關且更嚴格），其餘不動——`catch (Exception ex)` 已會把 `InvalidOperationException` 訊息帶回。Description 保持「僅支援 SELECT 等讀取操作」。

2. 新增工具方法（放在 `DryRunSql` 之後）：

```csharp
    /// <summary>
    /// 實際執行單一 DML（僅限非正式環境；預設預演，confirm:true 才 COMMIT）
    /// </summary>
    [McpServerTool, Description("實際執行單一 DML（INSERT/UPDATE/DELETE）（⚠️ 破壞性操作：confirm:true 時會 COMMIT 寫入資料庫；僅限非正式環境連線，Production 一律拒絕；預設 confirm=false 僅預演，回報影響筆數與前後對照，需 confirm:true 才實際執行）")]
    public static async Task<string> ExecuteSql(
        IDmlExecutionService dmlExecutionService,
        [Description("要執行的單一 DML 陳述式（INSERT/UPDATE/DELETE）")] string sql,
        [Description("是否實際執行（預設 false 僅預演）")] bool confirm = false)
    {
        try
        {
            var result = await dmlExecutionService.ExecuteAsync(sql, confirm);

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

            if (result.ExecutionError != null)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = true,
                    StatementType = result.StatementType.ToString(),
                    result.ExecutionError,
                    result.Warnings,
                    Committed = false,
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
                result.Committed,
                DatabaseChanged = result.Committed,
                Hint = result.Committed ? null : "以上為預演結果（已回滾）。確認無誤後加 confirm:true 實際執行。"
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"DML 執行失敗：{ex.Message}";
        }
    }
```

補 `using Specurai.Application.Services;`。

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.McpServer.Tests/Specurai.McpServer.Tests.csproj`
Expected: 全部 PASS（含既有 ConfirmGateTests、SqlToolsTests 零回歸）。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.McpServer/Tools/SqlTools.cs tests/Specurai.McpServer.Tests/SqlToolsTests.cs tests/Specurai.McpServer.Tests/ConfirmGateTests.cs
git commit -m "feat: MCP 新增 execute_sql 工具並強化 execute_readonly_sql 唯讀驗證"
```

---

### Task 8: CLI — `sql execute` 子命令

**Files:**
- Modify: `src/Specurai.Cli/Commands/SqlCommand.cs`

**Interfaces:**
- Consumes: Task 5 的 `IDmlExecutionService`（經 `Program.Services` 解析）。
- Produces: `specurai sql execute "<sql>" [--confirm]` 命令；沿用 dry-run 的轉置表格呈現。

CLI 專案無測試專案（與既有 `sql dry-run` 慣例一致），本 task 以建置 + 手動驗證把關。

- [ ] **Step 1: 抽出 dry-run 的呈現輔助方法**

`CreateDryRunCommand` 內的區域靜態函式 `BuildPreviewTable`、`FormatPreviewCell` 移到 `SqlCommand` 類別層級成為 `private static` 方法（內容原封不動搬移，註解一併帶走），`OutputJson` 同樣搬到類別層級並改以結果欄位計算兩個旗標（dry run 的輸出值不變：`Committed=false` 時 `RolledBack = result.IsValid`、`DatabaseChanged = false`）：

```csharp
    private static void OutputJson(Specurai.Domain.Entities.DryRunResult result)
    {
        // …原本體不變，僅最後兩個欄位改為…
        //     RolledBack = result.IsValid && !result.Committed,
        //     DatabaseChanged = result.Committed
        // 並在匿名物件中加入 result.Committed
    }
```

搬移後 `CreateDryRunCommand` 直接呼叫類別層級方法，行為與輸出不變。

- [ ] **Step 2: 新增 execute 子命令**

`Create()` 加 `command.AddCommand(CreateExecuteCommand());`，並新增：

```csharp
    private static Command CreateExecuteCommand()
    {
        var sqlArg = new Argument<string>("sql", "單一 DML 陳述式（INSERT/UPDATE/DELETE）");
        var confirmOption = new Option<bool>("--confirm", "實際執行並 COMMIT（未指定時僅預演）");
        var command = new Command("execute",
            "實際執行單一 DML（僅限非正式環境；預設先預演，加 --confirm 才寫入資料庫）") { sqlArg, confirmOption };

        command.SetHandler(async (sql, confirm) =>
        {
            var service = Program.Services.GetRequiredService<IDmlExecutionService>();

            try
            {
                var result = await service.ExecuteAsync(sql, confirm);

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
                    AnsiConsole.Write(BuildPreviewTable(result.PreviewTable));
                    if (result.PreviewTruncated)
                        CliOutput.Info($"預覽僅顯示前 {result.PreviewTable.Rows.Count} 筆。");
                }

                foreach (var warning in result.Warnings)
                    CliOutput.Warning(warning);

                if (result.Committed)
                {
                    CliOutput.Info("已 COMMIT，資料庫已變更。");
                }
                else
                {
                    CliOutput.Info("以上為預演結果（已回滾）。確認無誤後加 --confirm 實際執行。");
                }
            }
            catch (Exception ex)
            {
                CliOutput.Error($"執行失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, sqlArg, confirmOption);

        return command;
    }
```

補 `using Specurai.Application.Services;`（若尚未引用）。

- [ ] **Step 3: 建置 + 冒煙測試**

Run: `dotnet build`
Expected: 成功。

冒煙（不需真 DB，驗證閘門與訊息）：

```bash
dotnet run --project src/Specurai.Cli -- sql execute "SELECT 1"
```
Expected: 拒絕訊息（僅允許單一 DML）、ExitCode 1。

```bash
dotnet run --project src/Specurai.Cli -- sql dry-run "DELETE FROM T"
```
Expected: 行為與改動前一致（無連線時報連線錯誤；有連線時照常預演）。

- [ ] **Step 4: Commit**

```bash
git add src/Specurai.Cli/Commands/SqlCommand.cs
git commit -m "feat: CLI 新增 sql execute 命令（非正式環境 DML 執行）"
```

---

### Task 9: Desktop — SQL 查詢視窗「執行 DML」按鈕

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`（Dry Run 按鈕旁加按鈕）
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`（開啟 SqlQuery 文件處掛 `ConfirmExecuteCallback`）
- Modify: `src/Specurai.Desktop/Program.cs:85-86` 附近（ViewModel 工廠加參數）
- Test: `tests/Specurai.Desktop.Tests/SqlQueryDocumentViewModelDmlTests.cs`（新檔）

**Interfaces:**
- Consumes: Task 5 的 `IDmlExecutionService`；既有 `ConfirmExecuteCallback` 模式（`Func<string, Task<bool>>?`，參考 `SchemaMigrationDocumentViewModel.cs:82` 與 `MainWindowViewModel` 的 `ConfirmSaveCallback` 掛法）。
- Produces: `SqlQueryDocumentViewModel.ExecuteDmlCommand`（`CanExecute` 綁 `CanExecuteDml`）、`ConfirmExecuteCallback` 屬性。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Specurai.Desktop.Tests/SqlQueryDocumentViewModelDmlTests.cs
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Desktop.ViewModels;

namespace Specurai.Desktop.Tests;

public class SqlQueryDocumentViewModelDmlTests
{
    private static ConnectionProfile Profile(DatabaseEnvironment env, string name = "測試連線") => new()
    {
        Name = name,
        Server = "srv",
        Database = "AppDb",
        AuthType = AuthenticationType.SqlServerAuthentication,
        Username = "u",
        Password = "p",
        Environment = env
    };

    private static SqlQueryDocumentViewModel CreateVm(
        DatabaseEnvironment env, IDmlExecutionService? dmlService = null)
    {
        var profile = Profile(env);
        var cm = Substitute.For<IConnectionManager>();
        cm.GetEnabledProfiles().Returns([profile]);
        cm.GetCurrentProfile().Returns(profile);
        var queryRepo = Substitute.For<ISqlQueryRepository>();

        return new SqlQueryDocumentViewModel(
            queryRepo, cm,
            Substitute.For<ISqlDryRunRepository>(),
            updateSqlGenerator: null,
            dmlExecutionService: dmlService ?? Substitute.For<IDmlExecutionService>());
    }

    [Fact(DisplayName = "CanExecuteDml_正式環境連線_應為false")]
    public void CanExecuteDml_Production_ShouldBeFalse()
    {
        var vm = CreateVm(DatabaseEnvironment.Production);
        vm.CanExecuteDml.Should().BeFalse();
    }

    [Fact(DisplayName = "CanExecuteDml_非正式環境連線_應為true")]
    public void CanExecuteDml_NonProduction_ShouldBeTrue()
    {
        var vm = CreateVm(DatabaseEnvironment.Testing);
        vm.CanExecuteDml.Should().BeTrue();
    }

    [Fact(DisplayName = "ExecuteDml_確認回呼拒絕_不應confirm執行")]
    public async Task ExecuteDml_ConfirmDeclined_ShouldNotCommit()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Delete, AffectedRowCount = 5 });
        var vm = CreateVm(DatabaseEnvironment.Testing, service);
        vm.SqlText = "DELETE FROM T";
        vm.ConfirmExecuteCallback = _ => Task.FromResult(false);

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        await service.DidNotReceive().ExecuteAsync(
            Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("取消");
    }

    [Fact(DisplayName = "ExecuteDml_確認後_應confirm執行並回報已寫入")]
    public async Task ExecuteDml_Confirmed_ShouldCommit()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Update, AffectedRowCount = 2 });
        service.ExecuteAsync(Arg.Any<string>(), true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = true, StatementType = DryRunStatementType.Update, AffectedRowCount = 2, Committed = true });
        var vm = CreateVm(DatabaseEnvironment.Staging, service);
        vm.SqlText = "UPDATE T SET A = 1 WHERE Id = 9";
        vm.ConfirmExecuteCallback = _ => Task.FromResult(true);

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        await service.Received(1).ExecuteAsync(
            "UPDATE T SET A = 1 WHERE Id = 9", true, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Contain("已寫入");
    }

    [Fact(DisplayName = "ExecuteDml_預演即失敗_不應詢問確認")]
    public async Task ExecuteDml_PreviewInvalid_ShouldNotAskConfirm()
    {
        var service = Substitute.For<IDmlExecutionService>();
        service.ExecuteAsync(Arg.Any<string>(), false, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult { IsValid = false, RejectReason = "偵測到 2 個陳述式" });
        var vm = CreateVm(DatabaseEnvironment.Testing, service);
        vm.SqlText = "DELETE FROM A; DELETE FROM B";
        var asked = false;
        vm.ConfirmExecuteCallback = _ => { asked = true; return Task.FromResult(true); };

        await vm.ExecuteDmlCommand.ExecuteAsync(null);

        asked.Should().BeFalse();
        vm.StatusMessage.Should().Contain("陳述式");
    }
}
```

`ConnectionProfile` 屬性與既有測試建構方式以現行程式為準。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~SqlQueryDocumentViewModelDmlTests"`
Expected: 編譯失敗（建構函式參數、`CanExecuteDml`、`ExecuteDmlCommand`、`ConfirmExecuteCallback` 不存在）。

- [ ] **Step 3: 實作 ViewModel**

`SqlQueryDocumentViewModel.cs`：

1. 欄位與建構函式：

```csharp
    private readonly IDmlExecutionService? _dmlExecutionService;

    public SqlQueryDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IConnectionManager connectionManager,
        ISqlDryRunRepository? sqlDryRunRepository = null,
        IUpdateSqlGenerator? updateSqlGenerator = null,
        IDmlExecutionService? dmlExecutionService = null)
    {
        // …既有指派…
        _dmlExecutionService = dmlExecutionService;
        // …
    }
```

2. 確認回呼與可執行判斷：

```csharp
    /// <summary>執行 DML 前的確認回呼（View 掛真對話框，測試掛假回呼）；null 或回傳 false 時不執行</summary>
    public Func<string, Task<bool>>? ConfirmExecuteCallback { get; set; }

    /// <summary>是否可執行 DML：非正式環境連線且服務可用（Production 一律停用）</summary>
    public bool CanExecuteDml =>
        _dmlExecutionService != null
        && SelectedProfile != null
        && SelectedProfile.Environment != DatabaseEnvironment.Production
        && !_selectedConnectionDisabled;
```

3. `OnSelectedProfileChanged` 尾端加通知（方法既有，補兩行）：

```csharp
        OnPropertyChanged(nameof(CanExecuteDml));
        ExecuteDmlCommand.NotifyCanExecuteChanged();
```

4. 執行命令（放在 `DryRunAsync` 命令之後）：

```csharp
    /// <summary>
    /// 執行 DML：先預演取得影響筆數，經使用者確認後才 COMMIT 寫入。
    /// 環境閘門在 IDmlExecutionService（Production 拒絕），此處僅控制 UI 可用性與確認流程。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteDml))]
    private async Task ExecuteDmlAsync()
    {
        if (_dmlExecutionService == null || string.IsNullOrWhiteSpace(SqlText))
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
            StatusMessage = "預演中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            RowCount = 0;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalByRow.Clear();

            var preview = await _dmlExecutionService.ExecuteAsync(sql, confirm: false, profileId);

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
                DryRunWarnings = string.Join("\n", preview.Warnings);
                return;
            }

            var confirmed = ConfirmExecuteCallback != null
                && await ConfirmExecuteCallback(
                    $"將對「{SelectedProfile?.Name}」執行 {preview.StatementType}，影響 {preview.AffectedRowCount} 筆。\n" +
                    "此操作會 COMMIT 寫入資料庫，確定執行？");

            if (!confirmed)
            {
                StatusMessage = "已取消，資料庫未變更。";
                return;
            }

            StatusMessage = "執行中...";
            var result = await _dmlExecutionService.ExecuteAsync(sql, confirm: true, profileId);

            if (!result.IsValid)
            {
                StatusMessage = result.RejectReason ?? "驗證未通過";
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
            StatusMessage = $"執行完成{selectionNote}：影響 {result.AffectedRowCount} 筆（{result.StatementType}）｜已寫入資料庫";
            AddToHistory(sql);
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
```

補 `using`（`Specurai.Application.Services` 既有）。

- [ ] **Step 4: 執行 ViewModel 測試確認通過**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 新測試 PASS、既有測試零回歸。

- [ ] **Step 5: View、MainWindow 掛回呼、DI**

1. `SqlQueryDocumentView.axaml`：找到「Dry Run」按鈕，緊鄰其後加：

```xml
<Button Content="執行 DML"
        Command="{Binding ExecuteDmlCommand}"
        IsEnabled="{Binding CanExecuteDml}"
        ToolTip.Tip="實際執行單一 DML 並寫入資料庫（先預演確認；正式環境停用）" />
```

樣式（Classes、Margin）比照相鄰按鈕。

2. `MainWindowViewModel.cs`：開啟 `SqlQueryDocumentViewModel` 文件之處（搜尋 `SqlQueryDocumentViewModel`），比照 `MissingIndexReportDocumentViewModel` 的掛法加：

```csharp
        doc.ConfirmExecuteCallback = ConfirmSaveCallback;
```

3. `Program.cs:85-86` 附近的 `SqlQueryDocumentViewModel` 工廠註冊，加入第五個參數：

```csharp
                sp.GetRequiredService<IDmlExecutionService>(),
```

（依既有參數順序對應建構函式：queryRepo、connectionManager、dryRunRepo、updateSqlGenerator、dmlExecutionService。）

- [ ] **Step 6: 建置 + 全測試 + 手動冒煙**

Run: `dotnet build && dotnet test`
Expected: 全 PASS。

手動冒煙（若環境可跑桌面程式）：`dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj`，開 SQL 查詢視窗確認：(1) Production 連線時按鈕反灰；(2) 非正式連線輸入 DML 按「執行 DML」會先跳確認對話框。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs src/Specurai.Desktop/Program.cs tests/Specurai.Desktop.Tests/SqlQueryDocumentViewModelDmlTests.cs
git commit -m "feat: Desktop SQL 查詢視窗新增執行 DML 按鈕（正式環境停用）"
```

---

### Task 10: 文件更新與收尾驗證

**Files:**
- Modify: `docs/McpServerREADME.md`（工具清單加 `execute_sql`、更新 `execute_readonly_sql` 的防護描述）
- Modify: `README.md`（若有 MCP/CLI 工具清單章節則同步；沒有就不動）

**Interfaces:**
- Consumes: Task 7、8 的最終工具／命令行為。
- Produces: 文件與實作一致。

- [ ] **Step 1: 更新 McpServerREADME**

在 SQL 工具章節：
- 新增 `execute_sql` 條目：說明僅限非正式環境、預設預演、`confirm:true` 才 COMMIT、Production 一律拒絕。
- 更新 `execute_readonly_sql` 描述：防護方式為 ScriptDom AST 白名單驗證（SELECT-only，擋 CTE-DML、多句批次、SELECT INTO、EXEC）。
- `dry_run_sql` 描述不動。

- [ ] **Step 2: 全方案最終驗證**

Run: `dotnet build && dotnet test`
Expected: 建置成功、全部測試 PASS。

- [ ] **Step 3: Commit**

```bash
git add docs/McpServerREADME.md
git commit -m "docs: 更新 MCP 工具文件（execute_sql 與唯讀驗證強化）"
```

- [ ] **Step 4: 程式碼審查**

依專案憲法，使用 `superpowers:requesting-code-review` 對本次全部變更做審查後再回報完成。
