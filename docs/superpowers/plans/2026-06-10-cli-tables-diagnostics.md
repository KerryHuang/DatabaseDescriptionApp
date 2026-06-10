# B2 · CLI 物件與診斷對齊（tables parameters / create-sql / row-count / stats / column-stats）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 `Specurai.Cli` 補上 `tables parameters`、`tables row-count`、`tables stats`、`tables column-stats` 四個讀取命令（純接線），並以「抽成共用服務」方式新增 `tables create-sql`，同時把 MCP 內嵌的 CREATE TABLE 邏輯重構到 Application 服務。

**Architecture:** 前四項為純展示層接線，鏡像既有 MCP 工具對 `ITableQueryService` / `ITableStatisticsService` / `IColumnUsageService` 的呼叫，沿用 `TablesCommand` 既有輸出樣式（Spectre 表格 + JsonMode）。`create-sql` 將 MCP `SqlTools.GetCreateTableSql` 內嵌的 SQL 上移到 `ITableQueryService.GetCreateTableSqlAsync`（Application），由注入的 `ISqlQueryRepository` 執行；MCP 與 CLI 皆改呼叫此服務。服務皆已由 `AddSpecuraiCore()` 註冊。

**Tech Stack:** .NET 8、System.CommandLine、Spectre.Console、Dapper/SqlClient（既有 repository）、xUnit + NSubstitute + FluentAssertions。

---

## File Structure

- Modify: `src/Specurai.Cli/Commands/TablesCommand.cs` — 新增 4 個讀取子命令 + `create-sql`，並於 `Create()` 註冊。
- Modify: `src/Specurai.Application/Services/ITableQueryService.cs` — 新增 `GetCreateTableSqlAsync`。
- Modify: `src/Specurai.Application/Services/TableQueryService.cs` — 注入 `ISqlQueryRepository`，實作 `GetCreateTableSqlAsync`。
- Modify: `src/Specurai.McpServer/Tools/SqlTools.cs` — `GetCreateTableSql` 改呼叫服務。
- Modify: `tests/Specurai.Application.Tests/Services/TableQueryServiceTests.cs` — 補 `ISqlQueryRepository` 替身與 `GetCreateTableSqlAsync` 測試。

鏡像來源：`src/Specurai.McpServer/Tools/TableTools.cs`（GetParameters）、`StatisticsTools.cs`（GetTableStatistics、GetExactRowCount、GetColumnUsageStatistics）、`SqlTools.cs`（GetCreateTableSql）。

關鍵既有型別（已驗證）：
- `ITableQueryService.GetParametersAsync(string schema, string objectName, CancellationToken)` → `IReadOnlyList<ParameterInfo>`；`ParameterInfo { Name, DataType, Length(int?), IsOutput(bool), DefaultValue, Ordinal }`。
- `ITableStatisticsService.GetAllTableStatisticsAsync()` → `IReadOnlyList<TableStatisticsInfo>`；`GetExactRowCountAsync(string schemaName, string tableName)` → `long`。`TableStatisticsInfo { SchemaName, TableName, ObjectType, ApproximateRowCount(long), ExactRowCount(long?), ColumnCount, IndexCount, ForeignKeyCount, DataSizeMB, IndexSizeMB, TotalSizeMB, DisplayRowCount }`。
- `IColumnUsageService.GetStatisticsAsync()` / `GetFilteredStatisticsAsync(string searchText)` → `IReadOnlyList<ColumnUsageStatistics>`；`ColumnUsageStatistics { ColumnName, UsageCount(int), IsTypeConsistent, IsLengthConsistent, IsNullabilityConsistent, IsFullyConsistent, PrimaryDataType, PrimaryBaseType, PrimaryMaxLength(int), PrimaryIsNullable }`。
- `ISqlQueryRepository`（`src/Specurai.Domain/Interfaces/`）`Task<DataTable> ExecuteQueryAsync(string sql, CancellationToken ct = default)`。
- `TablesCommand.ParseObjectName(string)` → `(schema, name)`（既有 internal 方法，沿用）。

> **註：** `tables parameters/row-count/stats/column-stats` 為純展示層接線，與既有 `tables columns/indexes/relations` 一致——這些既有子命令在 `Specurai.Cli.Tests` 中並無單元測試（CLI 測試僅涵蓋 `ParseImportJson` / Resolver 等純邏輯）。本計畫對這四項沿用相同慣例，以建置 + 手動煙霧測試驗證；唯一具可測邏輯的 `create-sql`（服務層）走完整 TDD。

---

## Task 1: `tables parameters`（純接線）

**Files:** Modify `src/Specurai.Cli/Commands/TablesCommand.cs`

- [ ] **Step 1: 新增子命令方法**

在 `TablesCommand` class 內新增：

```csharp
private static Command CreateParametersCommand()
{
    var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
    var command = new Command("parameters", "顯示預存程序/函數的參數") { objectArg };

    command.SetHandler(async (objectName) =>
    {
        var (schema, name) = ParseObjectName(objectName);
        var service = Program.Services.GetRequiredService<ITableQueryService>();
        var parameters = await service.GetParametersAsync(schema, name);

        if (CliOutput.JsonMode)
        {
            var data = parameters.Select(p => new
            {
                p.Name,
                p.DataType,
                p.Length,
                p.IsOutput,
                p.DefaultValue,
                p.Ordinal
            }).ToList();
            CliOutput.Success(data, data.Count);
        }
        else
        {
            if (parameters.Count == 0)
            {
                CliOutput.Info($"物件 {schema}.{name} 沒有參數，或物件不存在。");
                return;
            }

            var table = new Table().Title($"[bold]{schema}.{name}[/] 參數");
            table.AddColumn("參數");
            table.AddColumn("型別");
            table.AddColumn("長度");
            table.AddColumn("輸出");
            table.AddColumn("預設值");

            foreach (var p in parameters)
            {
                table.AddRow(
                    p.Name.EscapeMarkup(),
                    p.DataType.EscapeMarkup(),
                    p.Length?.ToString() ?? "",
                    p.IsOutput ? "✓" : "",
                    (p.DefaultValue ?? "").EscapeMarkup());
            }

            AnsiConsole.Write(table);
        }
    }, objectArg);

    return command;
}
```

在 `Create()` 內加入註冊（與既有 `AddCommand(...)` 並列）：

```csharp
command.AddCommand(CreateParametersCommand());
```

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/TablesCommand.cs
git commit -m "feat(cli): tables parameters 子命令對齊 MCP get_parameters"
```

---

## Task 2: `tables row-count`（純接線）

**Files:** Modify `src/Specurai.Cli/Commands/TablesCommand.cs`

- [ ] **Step 1: 新增子命令方法**

```csharp
private static Command CreateRowCountCommand()
{
    var objectArg = new Argument<string>("object", "資料表名稱（格式：schema.name）");
    var command = new Command("row-count", "取得資料表精確列數（COUNT(*)）") { objectArg };

    command.SetHandler(async (objectName) =>
    {
        var (schema, name) = ParseObjectName(objectName);
        var service = Program.Services.GetRequiredService<ITableStatisticsService>();
        var count = await service.GetExactRowCountAsync(schema, name);

        if (CliOutput.JsonMode)
            CliOutput.Success(new { Schema = schema, Table = name, RowCount = count });
        else
            CliOutput.SuccessMessage($"{schema}.{name} 精確列數：{count:N0}");
    }, objectArg);

    return command;
}
```

註冊：`command.AddCommand(CreateRowCountCommand());`
並於檔案頂部確認已有 `using Specurai.Application.Services;`（既有，因 `ITableQueryService` 已使用）。

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/TablesCommand.cs
git commit -m "feat(cli): tables row-count 子命令對齊 MCP get_exact_row_count"
```

---

## Task 3: `tables stats`（純接線，全資料表統計）

**Files:** Modify `src/Specurai.Cli/Commands/TablesCommand.cs`

- [ ] **Step 1: 新增子命令方法**

```csharp
private static Command CreateStatsCommand()
{
    var command = new Command("stats", "顯示所有資料表的統計資訊（列數、大小等）");

    command.SetHandler(async () =>
    {
        var service = Program.Services.GetRequiredService<ITableStatisticsService>();
        var stats = await service.GetAllTableStatisticsAsync();

        if (CliOutput.JsonMode)
        {
            var data = stats.Select(s => new
            {
                s.SchemaName,
                s.TableName,
                s.ObjectType,
                s.ApproximateRowCount,
                s.ColumnCount,
                s.IndexCount,
                s.DataSizeMB,
                s.IndexSizeMB,
                s.TotalSizeMB
            }).ToList();
            CliOutput.Success(data, data.Count);
        }
        else
        {
            if (stats.Count == 0)
            {
                CliOutput.Info("沒有資料表統計資訊。");
                return;
            }

            var table = new Table().Title("[bold]資料表統計[/]");
            table.AddColumn("Schema");
            table.AddColumn("資料表");
            table.AddColumn("類型");
            table.AddColumn("約略列數", c => c.RightAligned());
            table.AddColumn("欄位", c => c.RightAligned());
            table.AddColumn("索引", c => c.RightAligned());
            table.AddColumn("總大小(MB)", c => c.RightAligned());

            foreach (var s in stats)
            {
                table.AddRow(
                    s.SchemaName.EscapeMarkup(),
                    s.TableName.EscapeMarkup(),
                    s.ObjectType.EscapeMarkup(),
                    s.ApproximateRowCount.ToString("N0"),
                    s.ColumnCount.ToString(),
                    s.IndexCount.ToString(),
                    s.TotalSizeMB.ToString("N2"));
            }

            AnsiConsole.Write(table);
        }
    });

    return command;
}
```

註冊：`command.AddCommand(CreateStatsCommand());`

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/TablesCommand.cs
git commit -m "feat(cli): tables stats 子命令對齊 MCP get_table_statistics"
```

---

## Task 4: `tables column-stats`（純接線，欄位使用統計）

**Files:** Modify `src/Specurai.Cli/Commands/TablesCommand.cs`

- [ ] **Step 1: 新增子命令方法**

```csharp
private static Command CreateColumnStatsCommand()
{
    var searchOption = new Option<string?>("--search", "篩選文字（可選）");
    var command = new Command("column-stats", "顯示欄位使用狀態統計（型別一致性分析）") { searchOption };

    command.SetHandler(async (search) =>
    {
        var service = Program.Services.GetRequiredService<IColumnUsageService>();
        var stats = string.IsNullOrWhiteSpace(search)
            ? await service.GetStatisticsAsync()
            : await service.GetFilteredStatisticsAsync(search);

        if (CliOutput.JsonMode)
        {
            var data = stats.Select(s => new
            {
                s.ColumnName,
                s.UsageCount,
                s.IsFullyConsistent,
                s.PrimaryDataType,
                s.PrimaryMaxLength,
                s.PrimaryIsNullable
            }).ToList();
            CliOutput.Success(data, data.Count);
        }
        else
        {
            if (stats.Count == 0)
            {
                CliOutput.Info("沒有欄位使用統計資訊。");
                return;
            }

            var table = new Table().Title("[bold]欄位使用統計[/]");
            table.AddColumn("欄位");
            table.AddColumn("使用次數", c => c.RightAligned());
            table.AddColumn("型別一致");
            table.AddColumn("主要型別");

            foreach (var s in stats)
            {
                table.AddRow(
                    s.ColumnName.EscapeMarkup(),
                    s.UsageCount.ToString("N0"),
                    s.IsFullyConsistent ? "[green]✓[/]" : "[yellow]✗[/]",
                    s.PrimaryDataType.EscapeMarkup());
            }

            AnsiConsole.Write(table);
        }
    }, searchOption);

    return command;
}
```

註冊：`command.AddCommand(CreateColumnStatsCommand());`
於檔案頂部確認 `using Specurai.Application.Services;` 已涵蓋 `IColumnUsageService`（同命名空間）。

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/TablesCommand.cs
git commit -m "feat(cli): tables column-stats 子命令對齊 MCP get_column_usage_statistics"
```

---

## Task 5: `create-sql` 抽成共用服務（Application）+ 重構 MCP + CLI 命令

**Files:**
- Modify: `src/Specurai.Application/Services/ITableQueryService.cs`
- Modify: `src/Specurai.Application/Services/TableQueryService.cs`
- Modify: `tests/Specurai.Application.Tests/Services/TableQueryServiceTests.cs`
- Modify: `src/Specurai.McpServer/Tools/SqlTools.cs`
- Modify: `src/Specurai.Cli/Commands/TablesCommand.cs`

- [ ] **Step 1: 寫服務層失敗測試**

在 `tests/Specurai.Application.Tests/Services/TableQueryServiceTests.cs` 的建構設定中新增 `ISqlQueryRepository` 替身（與既有 5 個 `Substitute.For<...>()` 並列），並把 `new TableQueryService(...)` 補上第 6 個引數 `_sqlQueryRepository`。然後新增測試：

```csharp
[Fact(DisplayName = "GetCreateTableSqlAsync: 有結果時應回傳建表語句")]
public async Task GetCreateTableSqlAsync_WhenRowExists_ShouldReturnScript()
{
    var dt = new System.Data.DataTable();
    dt.Columns.Add("CreateTableScript");
    dt.Rows.Add("CREATE TABLE [dbo].[T] (...);");
    _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>()).Returns(dt);

    var result = await _service.GetCreateTableSqlAsync("dbo", "T");

    result.Should().Be("CREATE TABLE [dbo].[T] (...);");
}

[Fact(DisplayName = "GetCreateTableSqlAsync: 無結果時應回傳 null")]
public async Task GetCreateTableSqlAsync_WhenNoRows_ShouldReturnNull()
{
    var dt = new System.Data.DataTable();
    dt.Columns.Add("CreateTableScript");
    _sqlQueryRepository.ExecuteQueryAsync(Arg.Any<string>()).Returns(dt);

    var result = await _service.GetCreateTableSqlAsync("dbo", "Missing");

    result.Should().BeNull();
}
```

> 若測試類別頂部尚未 `using NSubstitute;` / `using Specurai.Domain.Interfaces;`，請補上。`_sqlQueryRepository` 欄位宣告為 `private readonly ISqlQueryRepository _sqlQueryRepository;`。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~GetCreateTableSqlAsync"`
Expected: 編譯失敗（`GetCreateTableSqlAsync` 與第 6 個建構子引數不存在）。

- [ ] **Step 3: 介面新增方法**

在 `src/Specurai.Application/Services/ITableQueryService.cs` 新增（置於 `GetDefinitionAsync` 之後）：

```csharp
/// <summary>
/// 產生指定資料表的 CREATE TABLE SQL 語句；找不到資料表時回傳 null。
/// </summary>
Task<string?> GetCreateTableSqlAsync(
    string schema,
    string tableName,
    CancellationToken ct = default);
```

- [ ] **Step 4: 服務實作（注入 ISqlQueryRepository 並上移 SQL）**

在 `src/Specurai.Application/Services/TableQueryService.cs`：

1. 新增欄位 `private readonly ISqlQueryRepository _sqlQueryRepository;`，並於建構子參數尾端加入 `ISqlQueryRepository sqlQueryRepository`，於建構子主體加上 `_sqlQueryRepository = sqlQueryRepository;`。需 `using Specurai.Domain.Interfaces;`（若未引入）。
2. 新增方法（SQL 由 `src/Specurai.McpServer/Tools/SqlTools.cs` 既有 `GetCreateTableSql` 上移，行為不變）：

```csharp
public async Task<string?> GetCreateTableSqlAsync(
    string schema,
    string tableName,
    CancellationToken ct = default)
{
    var sql = $@"
        DECLARE @sql NVARCHAR(MAX) = '';
        DECLARE @tableName NVARCHAR(256) = '[{schema}].[{tableName}]';

        SELECT @sql = @sql +
            CASE WHEN @sql = '' THEN '' ELSE ',' + CHAR(13) + CHAR(10) END +
            '    [' + c.COLUMN_NAME + '] ' +
            c.DATA_TYPE +
            CASE
                WHEN c.DATA_TYPE IN ('varchar','nvarchar','char','nchar')
                    THEN '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) END + ')'
                WHEN c.DATA_TYPE IN ('decimal','numeric')
                    THEN '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR) + ')'
                ELSE ''
            END +
            CASE WHEN c.IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE ' NULL' END +
            CASE WHEN c.COLUMN_DEFAULT IS NOT NULL THEN ' DEFAULT ' + c.COLUMN_DEFAULT ELSE '' END
        FROM INFORMATION_SCHEMA.COLUMNS c
        WHERE c.TABLE_SCHEMA = '{schema}' AND c.TABLE_NAME = '{tableName}'
        ORDER BY c.ORDINAL_POSITION;

        SELECT 'CREATE TABLE ' + @tableName + ' (' + CHAR(13) + CHAR(10) + @sql + CHAR(13) + CHAR(10) + ');' AS CreateTableScript;
    ";

    var dataTable = await _sqlQueryRepository.ExecuteQueryAsync(sql, ct);
    if (dataTable.Rows.Count == 0)
        return null;

    return dataTable.Rows[0][0]?.ToString();
}
```

- [ ] **Step 5: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~GetCreateTableSqlAsync"`
Expected: 2 個測試 PASS。

- [ ] **Step 6: 重構 MCP `SqlTools.GetCreateTableSql` 改呼叫服務**

把 `src/Specurai.McpServer/Tools/SqlTools.cs` 的 `GetCreateTableSql` 整段（簽章使用 `ISqlQueryRepository` 與內嵌 SQL 的版本）替換為：

```csharp
[McpServerTool, Description("產生指定資料表的 CREATE TABLE SQL 語句")]
public static async Task<string> GetCreateTableSql(
    ITableQueryService tableQueryService,
    [Description("Schema 名稱，例如 dbo")] string schema,
    [Description("資料表名稱")] string tableName)
{
    try
    {
        var script = await tableQueryService.GetCreateTableSqlAsync(schema, tableName);
        return script ?? $"找不到資料表 [{schema}].[{tableName}]。";
    }
    catch (Exception ex)
    {
        return $"產生建表語句失敗：{ex.Message}";
    }
}
```

於檔案頂部確認 `using Specurai.Application.Services;`（若無則補）。

- [ ] **Step 7: 新增 CLI `tables create-sql` 子命令**

在 `src/Specurai.Cli/Commands/TablesCommand.cs` 新增：

```csharp
private static Command CreateCreateSqlCommand()
{
    var objectArg = new Argument<string>("object", "資料表名稱（格式：schema.name）");
    var command = new Command("create-sql", "產生資料表的 CREATE TABLE 語句") { objectArg };

    command.SetHandler(async (objectName) =>
    {
        var (schema, name) = ParseObjectName(objectName);
        var service = Program.Services.GetRequiredService<ITableQueryService>();
        var script = await service.GetCreateTableSqlAsync(schema, name);

        if (script == null)
        {
            CliOutput.Error($"找不到資料表 {schema}.{name}。");
            Environment.ExitCode = 1;
            return;
        }

        if (CliOutput.JsonMode)
            CliOutput.Success(new { Schema = schema, Table = name, Script = script });
        else
            AnsiConsole.WriteLine(script);
    }, objectArg);

    return command;
}
```

註冊：`command.AddCommand(CreateCreateSqlCommand());`

- [ ] **Step 8: 全方案建置 + 測試**

Run: `dotnet build`
Expected: Build succeeded，0 error。
Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj`
Expected: 全部 PASS（含新增 2 個）。

- [ ] **Step 9: Commit**

```bash
git add src/Specurai.Application/Services/ITableQueryService.cs src/Specurai.Application/Services/TableQueryService.cs tests/Specurai.Application.Tests/Services/TableQueryServiceTests.cs src/Specurai.McpServer/Tools/SqlTools.cs src/Specurai.Cli/Commands/TablesCommand.cs
git commit -m "feat(cli): tables create-sql 並將 CREATE TABLE 邏輯抽至 ITableQueryService"
```

---

## Task 6: 整批驗證與審查

- [ ] **Step 1: 全方案測試綠燈**

Run: `dotnet test`
Expected: 全部 PASS。

- [ ] **Step 2: 命令樹確認**

Run: `dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- tables --help`
Expected: 出現 `parameters`、`row-count`、`stats`、`column-stats`、`create-sql`。

- [ ] **Step 3: 煙霧測試（需資料庫連線）**

Run（擇一已知資料表）：
```
dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- tables stats
dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- tables create-sql dbo.SerialNoConfig
```
Expected：`stats` 顯示統計表；`create-sql` 輸出 CREATE TABLE 語句。

- [ ] **Step 4: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本批變更，通過後回報，再進入 B3。

---

## Self-Review 紀錄

- **Spec 覆蓋**：B2 範圍（parameters、create-sql、row-count、表統計、欄位使用統計）皆有對應 Task。✅
- **Placeholder 掃描**：無 TBD/TODO；每個程式碼步驟均含完整程式碼。✅
- **型別一致性**：`GetCreateTableSqlAsync(string,string,CancellationToken)→Task<string?>` 在介面、實作、MCP、CLI、測試一致；`ITableStatisticsService` / `IColumnUsageService` 方法與實體屬性與既有定義相符。✅
- **架構**：`create-sql` 的原始 SQL 由 MCP 展示層上移至 Application 服務（改善分層），MCP 改為呼叫服務；`ISqlQueryRepository`（Domain 介面）回傳 `DataTable`，Application 依賴 Domain 合法。✅
- **刻意取捨**：前四個讀取命令沿用既有 `tables` 子命令「無單元測試」慣例（純接線），僅 `create-sql` 服務層走 TDD。

## 執行中發現並修正的既有 Bug（2026-06-10）

抽取 `create-sql` 時的煙霧測試揭露 **MCP 原 `GetCreateTableSql` 的既有 bug**：其 SQL 使用 `SELECT @sql = @sql + ... FROM ... ORDER BY` 的變數累加反模式，在實測 server（SQL Server 2022）上只會取到**單一欄位**，產生不完整的 `CREATE TABLE`（11 欄表只輸出 1 欄）。

- **決定**：不忠實鏡像此 bug。改用 `STUFF(... FOR XML PATH(''), TYPE ...)` 串接欄位定義——相容 SQL Server 2005+（呼應專案近期「相容舊版 SQL Server」的取向，未用 2017+ 的 `STRING_AGG`）。
- **附帶改善**：找不到資料表時 `@cols` 為 NULL → 服務回傳 `null` → CLI 顯示「找不到資料表」；原版會回一個空殼 `CREATE TABLE ( )`。
- **效益**：修在共用服務，MCP 與 CLI 同時修復。已加 DBNull 測試覆蓋找不到的路徑（create-sql 服務測試共 3 個）。
- 已實測 `Systems.SerialNoConfig` 正確輸出全部 11 欄含型別/長度/預設值。
