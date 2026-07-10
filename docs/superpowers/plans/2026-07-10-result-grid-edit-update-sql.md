# 查詢結果編輯與產生異動 SQL 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SQL 查詢分頁的結果格可直接編輯（僅單表查詢），按「產生異動SQL」比對前後差異產出 UPDATE 語句並彈窗顯示，供複製後配合 Dry Run 預演。

**Architecture:** Domain 新增 `QueryColumnMetadata`/`QueryResultWithSchema` 與 `ISqlQueryRepository.ExecuteQueryWithSchemaAsync`（`CommandBehavior.KeyInfo` 取得來源表/主鍵中繼資料）；Application 新增純邏輯 `UpdateSqlGenerator`；Desktop 開放結果格編輯、快照原值、產生按鈕與兩個對話框（定位欄挑選、SQL 預覽重用 `SqlPreviewWindow`）。設計文件：`docs/superpowers/specs/2026-07-10-result-grid-edit-update-sql-design.md`。

**Tech Stack:** .NET 8、Microsoft.Data.SqlClient（KeyInfo）、Avalonia + CommunityToolkit.Mvvm、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- 一律以繁體中文撰寫 UI 文字、註解、Commit 訊息
- Clean Architecture：Domain 無相依；Application 只依 Domain；Infrastructure 依 Domain+Application；Desktop 依上層
- 實體 `required` + `init`，集合預設 `[]`；ViewModel 用 `[ObservableProperty]`/`[RelayCommand]`，設計時無參數建構子
- 測試 xUnit + NSubstitute + FluentAssertions，TDD 先測試後實作
- **系統維持唯讀**：本功能只「產生」UPDATE 文字，絕不執行寫入
- WHERE 定位：主鍵優先 → 無主鍵由使用者挑選定位欄 → 略過則全欄位原值＋警告註解
- 僅支援單一資料表的查詢結果；timestamp/byte[] 欄位不進 SET 也不進 WHERE；識別字一律 `[方括號]`（`]`→`]]`）

---

### Task 1: Domain 實體與介面擴充

**Files:**
- Create: `src/Specurai.Domain/Entities/QueryColumnMetadata.cs`
- Modify: `src/Specurai.Domain/Interfaces/ISqlQueryRepository.cs`（介面加兩個方法）
- Test: `tests/Specurai.Domain.Tests/Entities/QueryResultWithSchemaTests.cs`

**Interfaces:**
- Consumes: 無
- Produces:
  - `class QueryColumnMetadata { required string ColumnName; string? BaseSchema; string? BaseTable; string? BaseColumn; bool IsKey; bool IsReadOnly; required Type ClrType }`
  - `class QueryResultWithSchema { required DataTable Table; IReadOnlyList<QueryColumnMetadata> Columns; string? TargetSchema; string? TargetTable; bool IsSingleTable }`
  - `ISqlQueryRepository.ExecuteQueryWithSchemaAsync(string sql, CancellationToken ct = default)` 與 `(string sql, string connectionString, CancellationToken ct = default)` 兩多載，回傳 `Task<QueryResultWithSchema>`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Domain.Tests/Entities/QueryResultWithSchemaTests.cs`：

```csharp
using System.Data;
using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class QueryResultWithSchemaTests
{
    private static QueryColumnMetadata Col(string name, string? table = "Users", string? schema = "dbo",
        string? baseColumn = null, bool isKey = false, bool isReadOnly = false, Type? clrType = null) => new()
    {
        ColumnName = name,
        BaseSchema = schema,
        BaseTable = table,
        BaseColumn = baseColumn ?? name,
        IsKey = isKey,
        IsReadOnly = isReadOnly,
        ClrType = clrType ?? typeof(string)
    };

    [Fact(DisplayName = "單一來源表：IsSingleTable 為 true 且 TargetTable/TargetSchema 正確")]
    public void 單一來源表_應判定為單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", isKey: true), Col("Name")]
        };

        result.IsSingleTable.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "多來源表（JOIN）：IsSingleTable 為 false 且 TargetTable 為 null")]
    public void 多來源表_應判定為非單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", table: "Users"), Col("OrderNo", table: "Orders")]
        };

        result.IsSingleTable.Should().BeFalse();
        result.TargetTable.Should().BeNull();
    }

    [Fact(DisplayName = "含運算式欄位（BaseTable 為 null）不影響單表判定")]
    public void 運算式欄位_不影響單表判定()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id"), Col("Total", table: null, baseColumn: null)]
        };

        result.IsSingleTable.Should().BeTrue();
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "全部都是運算式欄位：非單表")]
    public void 全運算式欄位_應判定為非單表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("A", table: null, baseColumn: null)]
        };

        result.IsSingleTable.Should().BeFalse();
    }

    [Fact(DisplayName = "同表不同大小寫視為同一來源表")]
    public void 同表不同大小寫_應視為同一來源表()
    {
        var result = new QueryResultWithSchema
        {
            Table = new DataTable(),
            Columns = [Col("Id", table: "Users"), Col("Name", table: "USERS")]
        };

        result.IsSingleTable.Should().BeTrue();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~QueryResultWithSchemaTests"`
Expected: 編譯失敗（型別不存在）

- [ ] **Step 3: 實作 Domain**

建立 `src/Specurai.Domain/Entities/QueryColumnMetadata.cs`：

```csharp
using System.Data;

namespace Specurai.Domain.Entities;

/// <summary>
/// 查詢結果欄位的來源中繼資料（由 CommandBehavior.KeyInfo 取得）
/// </summary>
public class QueryColumnMetadata
{
    /// <summary>結果欄位名稱</summary>
    public required string ColumnName { get; init; }

    /// <summary>來源 Schema 名稱（無來源時為 null）</summary>
    public string? BaseSchema { get; init; }

    /// <summary>來源資料表名稱（運算式欄位為 null）</summary>
    public string? BaseTable { get; init; }

    /// <summary>來源欄位名稱（運算式欄位為 null）</summary>
    public string? BaseColumn { get; init; }

    /// <summary>是否為主鍵欄位</summary>
    public bool IsKey { get; init; }

    /// <summary>是否唯讀（identity、timestamp/rowversion、運算式欄位）</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>欄位 CLR 型別（產生 SQL 字面值與編輯值轉型用）</summary>
    public required Type ClrType { get; init; }
}

/// <summary>
/// 含 Schema 中繼資料的查詢結果
/// </summary>
public class QueryResultWithSchema
{
    /// <summary>查詢結果資料</summary>
    public required DataTable Table { get; init; }

    /// <summary>各欄位的來源中繼資料（與結果欄位順序一致）</summary>
    public IReadOnlyList<QueryColumnMetadata> Columns { get; init; } = [];

    /// <summary>唯一來源表的 Schema（非單表時為 null）</summary>
    public string? TargetSchema => DistinctBaseTables() is [var only] ? only.Schema : null;

    /// <summary>唯一來源表名稱（非單表時為 null）</summary>
    public string? TargetTable => DistinctBaseTables() is [var only] ? only.Table : null;

    /// <summary>結果是否來自單一資料表（可編輯的前提）</summary>
    public bool IsSingleTable => TargetTable != null;

    private List<(string? Schema, string Table)> DistinctBaseTables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string?, string)>();
        foreach (var column in Columns)
        {
            if (string.IsNullOrEmpty(column.BaseTable)) continue;
            if (seen.Add($"{column.BaseSchema}::{column.BaseTable}"))
                result.Add((column.BaseSchema, column.BaseTable!));
        }
        return result;
    }
}
```

在 `src/Specurai.Domain/Interfaces/ISqlQueryRepository.cs` 的 `ExecuteQueryAsync` 兩多載之後加入：

```csharp
    /// <summary>
    /// 執行 SQL 查詢並同時取得欄位來源中繼資料（CommandBehavior.KeyInfo，使用預設連線）
    /// </summary>
    Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 執行 SQL 查詢並同時取得欄位來源中繼資料（CommandBehavior.KeyInfo，使用指定連線字串）
    /// </summary>
    Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, string connectionString, CancellationToken ct = default);
```

檔頭補 `using Specurai.Domain.Entities;`。

注意：`SqlQueryRepository`（Infrastructure）此時尚未實作新方法會編譯失敗——本 Task 先在該類別加最小佔位實作（Task 2 會換成真實作）：

```csharp
    public Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, string connectionString, CancellationToken ct = default)
        => throw new NotImplementedException();
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Domain.Tests/Specurai.Domain.Tests.csproj --filter "FullyQualifiedName~QueryResultWithSchemaTests"`
Expected: PASS（5 個測試）。另跑 `dotnet build` 確認全方案可編譯。

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Domain/Entities/QueryColumnMetadata.cs src/Specurai.Domain/Interfaces/ISqlQueryRepository.cs src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs tests/Specurai.Domain.Tests/Entities/QueryResultWithSchemaTests.cs
git commit -m "feat: 新增查詢結果欄位中繼資料實體與介面

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Infrastructure — KeyInfo 查詢與中繼資料對映

**Files:**
- Modify: `src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs`（換掉 Task 1 佔位）
- Test: `tests/Specurai.Infrastructure.Tests/Repositories/SqlQueryRepositorySchemaTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `QueryColumnMetadata`/`QueryResultWithSchema`
- Produces: `SqlQueryRepository.ExecuteQueryWithSchemaAsync` 真實作；`internal static List<QueryColumnMetadata> MapColumnMetadata(DataTable? schemaTable)`（離線可測，csproj 已有 InternalsVisibleTo）

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Infrastructure.Tests/Repositories/SqlQueryRepositorySchemaTests.cs`：

```csharp
using System.Data;
using FluentAssertions;
using Specurai.Infrastructure.Repositories;

namespace Specurai.Infrastructure.Tests.Repositories;

public class SqlQueryRepositorySchemaTests
{
    /// <summary>建立模擬 GetSchemaTable() 回傳形狀的 schema 資料表</summary>
    private static DataTable BuildSchemaTable(params object?[][] rows)
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("BaseSchemaName", typeof(string));
        table.Columns.Add("BaseTableName", typeof(string));
        table.Columns.Add("BaseColumnName", typeof(string));
        table.Columns.Add("IsKey", typeof(bool));
        table.Columns.Add("IsAutoIncrement", typeof(bool));
        table.Columns.Add("IsReadOnly", typeof(bool));
        table.Columns.Add("IsExpression", typeof(bool));
        table.Columns.Add("DataType", typeof(Type));
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }

    [Fact(DisplayName = "一般欄位：來源表/欄與主鍵旗標正確對映")]
    public void MapColumnMetadata_一般欄位_應正確對映()
    {
        var schema = BuildSchemaTable(
            ["EMP_ID", "dbo", "SYS010", "EMP_ID", true, false, false, false, typeof(string)],
            ["EMP_NAME", "dbo", "SYS010", "EMP_NAME", false, false, false, false, typeof(string)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result.Should().HaveCount(2);
        result[0].ColumnName.Should().Be("EMP_ID");
        result[0].BaseSchema.Should().Be("dbo");
        result[0].BaseTable.Should().Be("SYS010");
        result[0].BaseColumn.Should().Be("EMP_ID");
        result[0].IsKey.Should().BeTrue();
        result[0].IsReadOnly.Should().BeFalse();
        result[0].ClrType.Should().Be(typeof(string));
        result[1].IsKey.Should().BeFalse();
    }

    [Fact(DisplayName = "identity 欄位應標記唯讀")]
    public void MapColumnMetadata_Identity欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["Id", "dbo", "T", "Id", true, true, false, false, typeof(int)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "運算式欄位（無 BaseColumn）應標記唯讀且來源欄為 null")]
    public void MapColumnMetadata_運算式欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["Total", null, null, null, false, false, false, true, typeof(decimal)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].BaseColumn.Should().BeNull();
        result[0].BaseTable.Should().BeNull();
        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "byte[]（timestamp/rowversion）欄位應標記唯讀")]
    public void MapColumnMetadata_ByteArray欄位_應唯讀()
    {
        var schema = BuildSchemaTable(
            ["TIMESTAMP", "dbo", "SYS010", "TIMESTAMP", false, false, false, false, typeof(byte[])]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "IsKey 為 DBNull 應視為 false")]
    public void MapColumnMetadata_IsKey為DBNull_應視為False()
    {
        var schema = BuildSchemaTable(
            ["A", "dbo", "T", "A", null, false, false, false, typeof(string)]);

        var result = SqlQueryRepository.MapColumnMetadata(schema);

        result[0].IsKey.Should().BeFalse();
    }

    [Fact(DisplayName = "schema 表為 null 應回傳空清單")]
    public void MapColumnMetadata_Null_應回傳空清單()
    {
        SqlQueryRepository.MapColumnMetadata(null).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SqlQueryRepositorySchemaTests"`
Expected: 編譯失敗（`MapColumnMetadata` 不存在）

- [ ] **Step 3: 實作**

在 `src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs` 移除 Task 1 佔位，加入（檔頭補 `using Specurai.Domain.Entities;`）：

```csharp
    public async Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await ExecuteQueryWithSchemaAsync(sql, connectionString, ct);
    }

    public async Task<QueryResultWithSchema> ExecuteQueryWithSchemaAsync(string sql, string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = 30;

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.KeyInfo, ct);
        var columns = MapColumnMetadata(reader.GetSchemaTable());

        // 手動填列：避免 DataTable.Load 因 KeyInfo 自動加上主鍵/唯一約束，
        // 導致 OUTER JOIN 含 NULL 鍵值或重複鍵值的結果載入失敗
        var table = new DataTable();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (table.Columns.Contains(name))
                name = $"{name}_{i}";
            table.Columns.Add(name, reader.GetFieldType(i));
        }

        while (await reader.ReadAsync(ct))
        {
            var row = table.NewRow();
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            table.Rows.Add(row);
        }

        return new QueryResultWithSchema { Table = table, Columns = columns };
    }

    /// <summary>
    /// 將 GetSchemaTable() 的結果對映為欄位中繼資料。
    /// 唯讀判定：identity、驅動回報唯讀、運算式欄位、無來源欄、byte[]（timestamp/rowversion）
    /// </summary>
    internal static List<QueryColumnMetadata> MapColumnMetadata(DataTable? schemaTable)
    {
        if (schemaTable == null)
            return [];

        var result = new List<QueryColumnMetadata>();
        foreach (DataRow row in schemaTable.Rows)
        {
            var clrType = row.Table.Columns.Contains("DataType") ? row["DataType"] as Type ?? typeof(object) : typeof(object);
            var baseColumn = AsString(row, "BaseColumnName");
            var isReadOnly = AsBool(row, "IsAutoIncrement")
                || AsBool(row, "IsReadOnly")
                || AsBool(row, "IsExpression")
                || string.IsNullOrEmpty(baseColumn)
                || clrType == typeof(byte[]);

            result.Add(new QueryColumnMetadata
            {
                ColumnName = AsString(row, "ColumnName") ?? string.Empty,
                BaseSchema = AsString(row, "BaseSchemaName"),
                BaseTable = AsString(row, "BaseTableName"),
                BaseColumn = baseColumn,
                IsKey = AsBool(row, "IsKey"),
                IsReadOnly = isReadOnly,
                ClrType = clrType
            });
        }
        return result;

        static string? AsString(DataRow row, string column) =>
            row.Table.Columns.Contains(column) && row[column] is string { Length: > 0 } s ? s : null;

        static bool AsBool(DataRow row, string column) =>
            row.Table.Columns.Contains(column) && row[column] is true;
    }
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 全部 PASS（新增 6 個 + 既有不受影響）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Infrastructure/Repositories/SqlQueryRepository.cs tests/Specurai.Infrastructure.Tests/Repositories/SqlQueryRepositorySchemaTests.cs
git commit -m "feat: SqlQueryRepository 以 KeyInfo 取得查詢欄位中繼資料

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Application — UpdateSqlGenerator

**Files:**
- Create: `src/Specurai.Application/Models/UpdateSqlModels.cs`
- Create: `src/Specurai.Application/Services/UpdateSqlGenerator.cs`（含介面 `IUpdateSqlGenerator`）
- Modify: `src/Specurai.Infrastructure/ServiceRegistration.cs`（註冊，加在「Application - 核心查詢服務」區塊之後）
- Test: `tests/Specurai.Application.Tests/Services/UpdateSqlGeneratorTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `QueryColumnMetadata`
- Produces:
  - `class UpdateSqlRequest { string? TargetSchema; required string TargetTable; required IReadOnlyList<QueryColumnMetadata> Columns; required IReadOnlyList<string> KeyColumns; bool IsFallbackKeys; required IReadOnlyList<UpdateSqlRow> Rows }`
  - `class UpdateSqlRow { required IReadOnlyDictionary<string, object?> Original; required IReadOnlyDictionary<string, object?> Current }`
  - `class UpdateSqlResult { string Sql; int StatementCount; IReadOnlyList<string> Warnings }`
  - `interface IUpdateSqlGenerator { UpdateSqlResult Generate(UpdateSqlRequest request); }`＋實作 `UpdateSqlGenerator`
  - DI：`services.AddSingleton<IUpdateSqlGenerator, UpdateSqlGenerator>();`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/Specurai.Application.Tests/Services/UpdateSqlGeneratorTests.cs`：

```csharp
using FluentAssertions;
using Specurai.Application.Models;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Application.Tests.Services;

public class UpdateSqlGeneratorTests
{
    private readonly UpdateSqlGenerator _generator = new();

    private static QueryColumnMetadata Col(string name, bool isKey = false, bool isReadOnly = false, Type? clrType = null) => new()
    {
        ColumnName = name,
        BaseSchema = "dbo",
        BaseTable = "SYS010",
        BaseColumn = name,
        IsKey = isKey,
        IsReadOnly = isReadOnly,
        ClrType = clrType ?? typeof(string)
    };

    private static UpdateSqlRequest Request(
        IReadOnlyList<QueryColumnMetadata> columns,
        IReadOnlyList<string> keys,
        params UpdateSqlRow[] rows) => new()
    {
        TargetSchema = "dbo",
        TargetTable = "SYS010",
        Columns = columns,
        KeyColumns = keys,
        Rows = rows
    };

    private static UpdateSqlRow Row(Dictionary<string, object?> original, Dictionary<string, object?> current) =>
        new() { Original = original, Current = current };

    [Fact(DisplayName = "單欄異動：SET 只含改過的欄位，WHERE 用主鍵原值")]
    public void Generate_單欄異動_應產生正確UPDATE()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "100719", ["EMP_NAME"] = "洪玉如" },
                new() { ["EMP_ID"] = "100719", ["EMP_NAME"] = "洪小玉" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().Contain("UPDATE [dbo].[SYS010]");
        result.Sql.Should().Contain("SET [EMP_NAME] = N'洪小玉'");
        result.Sql.Should().Contain("WHERE [EMP_ID] = N'100719'");
        result.Sql.Should().NotContain("[EMP_ID] = N'100719',"); // EMP_ID 未改，不進 SET
        result.Sql.TrimEnd().Should().EndWith(";");
    }

    [Fact(DisplayName = "無異動：StatementCount 為 0")]
    public void Generate_無異動_應回傳零句()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(0);
        result.Sql.Should().BeEmpty();
    }

    [Fact(DisplayName = "多列多欄異動：每列一句 UPDATE")]
    public void Generate_多列異動_應每列一句()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME"), Col("PWD") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲", ["PWD"] = "a" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "乙", ["PWD"] = "b" }),
            Row(new() { ["EMP_ID"] = "2", ["EMP_NAME"] = "丙", ["PWD"] = "c" },
                new() { ["EMP_ID"] = "2", ["EMP_NAME"] = "丙", ["PWD"] = "c" }),
            Row(new() { ["EMP_ID"] = "3", ["EMP_NAME"] = "丁", ["PWD"] = "d" },
                new() { ["EMP_ID"] = "3", ["EMP_NAME"] = "戊", ["PWD"] = "d" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(2);
        result.Sql.Should().Contain("SET [EMP_NAME] = N'乙', [PWD] = N'b'");
        result.Sql.Should().Contain("SET [EMP_NAME] = N'戊'");
    }

    [Fact(DisplayName = "NULL 處理：SET 用 NULL、WHERE 原值 NULL 用 IS NULL")]
    public void Generate_NULL處理_應正確()
    {
        var columns = new[] { Col("EMP_ID"), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID", "EMP_NAME"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = null },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "新值" }));
        var result = _generator.Generate(request);
        result.Sql.Should().Contain("WHERE [EMP_ID] = N'1' AND [EMP_NAME] IS NULL");

        var request2 = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "舊值" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = null }));
        var result2 = _generator.Generate(request2);
        result2.Sql.Should().Contain("SET [EMP_NAME] = NULL");
    }

    [Fact(DisplayName = "型別字面值：數字/日期/bit/Guid 格式正確")]
    public void Generate_型別字面值_應正確()
    {
        var columns = new[]
        {
            Col("Id", isKey: true, clrType: typeof(int)),
            Col("Amount", clrType: typeof(decimal)),
            Col("Birthday", clrType: typeof(DateTime)),
            Col("IsActive", clrType: typeof(bool)),
            Col("Token", clrType: typeof(Guid))
        };
        var guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 5, ["Amount"] = 1.5m, ["Birthday"] = new DateTime(2026, 7, 10, 8, 30, 0), ["IsActive"] = false, ["Token"] = Guid.Empty },
                new() { ["Id"] = 5, ["Amount"] = 99.25m, ["Birthday"] = new DateTime(2026, 12, 31), ["IsActive"] = true, ["Token"] = guid }));

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("[Amount] = 99.25");
        result.Sql.Should().Contain("[Birthday] = '2026-12-31 00:00:00.000'");
        result.Sql.Should().Contain("[IsActive] = 1");
        result.Sql.Should().Contain($"[Token] = '{guid}'");
        result.Sql.Should().Contain("WHERE [Id] = 5");
    }

    [Fact(DisplayName = "跳脫：字串單引號與識別字方括號")]
    public void Generate_跳脫_應正確()
    {
        var columns = new QueryColumnMetadata[]
        {
            new() { ColumnName = "Weird]Col", BaseSchema = "dbo", BaseTable = "T]1", BaseColumn = "Weird]Col", IsKey = true, ClrType = typeof(string) },
            new() { ColumnName = "Name", BaseSchema = "dbo", BaseTable = "T]1", BaseColumn = "Name", ClrType = typeof(string) }
        };
        var request = new UpdateSqlRequest
        {
            TargetSchema = "dbo",
            TargetTable = "T]1",
            Columns = columns,
            KeyColumns = ["Weird]Col"],
            Rows = [Row(new() { ["Weird]Col"] = "a", ["Name"] = "O'Brien" },
                        new() { ["Weird]Col"] = "a", ["Name"] = "O'Neil" })]
        };

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("UPDATE [dbo].[T]]1]");
        result.Sql.Should().Contain("[Weird]]Col]");
        result.Sql.Should().Contain("N'O''Neil'");
    }

    [Fact(DisplayName = "編輯後為字串的數字欄位：依 ClrType 轉型後輸出數字字面值")]
    public void Generate_字串編輯值轉型_應輸出正確字面值()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Qty", clrType: typeof(int)) };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 1, ["Qty"] = 10 },
                new() { ["Id"] = 1, ["Qty"] = "25" }));   // DataGrid 編輯後常是字串

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().Contain("[Qty] = 25");
    }

    [Fact(DisplayName = "編輯值無法轉型：跳過該列並回報警告")]
    public void Generate_轉型失敗_應跳過並警告()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Qty", clrType: typeof(int)) };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 1, ["Qty"] = 10 },
                new() { ["Id"] = 1, ["Qty"] = "abc" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Qty");
    }

    [Fact(DisplayName = "唯讀欄位（timestamp/identity）不進 SET 也不進 WHERE")]
    public void Generate_唯讀欄位_應排除()
    {
        var columns = new[]
        {
            Col("Id", isKey: true, clrType: typeof(int)),
            Col("Name"),
            Col("Ver", isReadOnly: true, clrType: typeof(byte[]))
        };
        var request = Request(columns, ["Id", "Ver"],
            Row(new() { ["Id"] = 1, ["Name"] = "甲", ["Ver"] = new byte[] { 1 } },
                new() { ["Id"] = 1, ["Name"] = "乙", ["Ver"] = new byte[] { 1 } }));

        var result = _generator.Generate(request);

        result.Sql.Should().NotContain("[Ver]");
        result.Sql.Should().Contain("WHERE [Id] = 1");
    }

    [Fact(DisplayName = "全欄位 fallback：加警告註解")]
    public void Generate_Fallback定位_應加警告註解()
    {
        var columns = new[] { Col("A"), Col("B") };
        var request = new UpdateSqlRequest
        {
            TargetSchema = "dbo",
            TargetTable = "SYS010",
            Columns = columns,
            KeyColumns = ["A", "B"],
            IsFallbackKeys = true,
            Rows = [Row(new() { ["A"] = "1", ["B"] = "x" }, new() { ["A"] = "1", ["B"] = "y" })]
        };

        var result = _generator.Generate(request);

        result.Sql.Should().StartWith("-- 警告：無主鍵定位，執行前請先 Dry Run 確認影響筆數");
        result.Sql.Should().Contain("WHERE [A] = N'1' AND [B] = N'x'");
    }

    [Fact(DisplayName = "複合主鍵：WHERE 帶入全部主鍵欄")]
    public void Generate_複合主鍵_應全數帶入WHERE()
    {
        var columns = new[] { Col("K1", isKey: true), Col("K2", isKey: true), Col("V") };
        var request = Request(columns, ["K1", "K2"],
            Row(new() { ["K1"] = "a", ["K2"] = "b", ["V"] = "1" },
                new() { ["K1"] = "a", ["K2"] = "b", ["V"] = "2" }));

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("WHERE [K1] = N'a' AND [K2] = N'b'");
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~UpdateSqlGeneratorTests"`
Expected: 編譯失敗（型別不存在）

- [ ] **Step 3: 實作**

建立 `src/Specurai.Application/Models/UpdateSqlModels.cs`：

```csharp
using Specurai.Domain.Entities;

namespace Specurai.Application.Models;

/// <summary>
/// 產生異動 SQL 的請求
/// </summary>
public class UpdateSqlRequest
{
    /// <summary>目標資料表 Schema（可為 null）</summary>
    public string? TargetSchema { get; init; }

    /// <summary>目標資料表名稱</summary>
    public required string TargetTable { get; init; }

    /// <summary>結果欄位中繼資料</summary>
    public required IReadOnlyList<QueryColumnMetadata> Columns { get; init; }

    /// <summary>WHERE 定位欄位（結果欄位名稱）</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>定位欄位是否為「全欄位 fallback」（無主鍵且使用者未挑選）</summary>
    public bool IsFallbackKeys { get; init; }

    /// <summary>原值/現值列（順序對應）</summary>
    public required IReadOnlyList<UpdateSqlRow> Rows { get; init; }
}

/// <summary>
/// 一列資料的原值與現值
/// </summary>
public class UpdateSqlRow
{
    public required IReadOnlyDictionary<string, object?> Original { get; init; }
    public required IReadOnlyDictionary<string, object?> Current { get; init; }
}

/// <summary>
/// 產生異動 SQL 的結果
/// </summary>
public class UpdateSqlResult
{
    /// <summary>產生的 UPDATE 語句全文（無異動時為空字串）</summary>
    public string Sql { get; init; } = string.Empty;

    /// <summary>UPDATE 語句數量</summary>
    public int StatementCount { get; init; }

    /// <summary>警告（轉型失敗跳過的列等）</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

建立 `src/Specurai.Application/Services/UpdateSqlGenerator.cs`：

```csharp
using System.Globalization;
using System.Text;
using Specurai.Application.Models;
using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 異動 SQL 產生器介面
/// </summary>
public interface IUpdateSqlGenerator
{
    /// <summary>比對原值與現值，產生 UPDATE 語句（純邏輯，不碰資料庫）</summary>
    UpdateSqlResult Generate(UpdateSqlRequest request);
}

/// <summary>
/// 異動 SQL 產生器：比對每列原值/現值差異，產出 UPDATE 語句。
/// SET 只含實際改過的欄位；WHERE 一律使用原值（NULL 用 IS NULL）。
/// </summary>
public class UpdateSqlGenerator : IUpdateSqlGenerator
{
    public UpdateSqlResult Generate(UpdateSqlRequest request)
    {
        // GroupBy 容錯：重複欄名（理論上單表 SELECT 不會有）取第一個，不擲例外
        var metaByName = request.Columns
            .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var statements = new List<string>();

        for (var rowIndex = 0; rowIndex < request.Rows.Count; rowIndex++)
        {
            var row = request.Rows[rowIndex];
            var setClauses = new List<string>();
            var conversionFailed = false;

            foreach (var column in request.Columns)
            {
                // 唯讀欄位（identity/timestamp/運算式）與無來源欄者不可異動
                if (column.IsReadOnly || string.IsNullOrEmpty(column.BaseColumn))
                    continue;

                var original = Normalize(row.Original.GetValueOrDefault(column.ColumnName));
                if (!TryConvert(row.Current.GetValueOrDefault(column.ColumnName), column.ClrType, out var current))
                {
                    warnings.Add($"第 {rowIndex + 1} 列：欄位「{column.ColumnName}」的值無法轉換為 {column.ClrType.Name}，已跳過該列。");
                    conversionFailed = true;
                    break;
                }

                if (!ValuesEqual(original, current))
                    setClauses.Add($"{Quote(column.BaseColumn!)} = {FormatLiteral(current)}");
            }

            if (conversionFailed || setClauses.Count == 0)
                continue;

            var whereClauses = new List<string>();
            foreach (var keyName in request.KeyColumns)
            {
                if (!metaByName.TryGetValue(keyName, out var meta) || string.IsNullOrEmpty(meta.BaseColumn))
                    continue;
                // timestamp/byte[] 不進 WHERE
                if (meta.ClrType == typeof(byte[]))
                    continue;

                var value = Normalize(row.Original.GetValueOrDefault(keyName));
                whereClauses.Add(value == null
                    ? $"{Quote(meta.BaseColumn!)} IS NULL"
                    : $"{Quote(meta.BaseColumn!)} = {FormatLiteral(value)}");
            }

            var tableName = string.IsNullOrEmpty(request.TargetSchema)
                ? Quote(request.TargetTable)
                : $"{Quote(request.TargetSchema)}.{Quote(request.TargetTable)}";

            statements.Add($"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)};");
        }

        if (statements.Count == 0)
            return new UpdateSqlResult { Warnings = warnings };

        var sb = new StringBuilder();
        if (request.IsFallbackKeys)
            sb.AppendLine("-- 警告：無主鍵定位，執行前請先 Dry Run 確認影響筆數");
        sb.AppendJoin(Environment.NewLine, statements);

        return new UpdateSqlResult
        {
            Sql = sb.ToString(),
            StatementCount = statements.Count,
            Warnings = warnings
        };
    }

    /// <summary>DBNull 正規化為 null</summary>
    private static object? Normalize(object? value) => value is DBNull ? null : value;

    /// <summary>
    /// 將現值轉為欄位 CLR 型別（DataGrid 編輯後的值常是字串）。
    /// 字串輸入依使用者目前文化解析（與格子顯示格式一致）。
    /// </summary>
    private static bool TryConvert(object? value, Type clrType, out object? converted)
    {
        converted = value is DBNull ? null : value;
        if (converted == null || clrType.IsInstanceOfType(converted))
            return true;

        try
        {
            if (converted is string s)
            {
                converted = clrType == typeof(Guid) ? Guid.Parse(s)
                    : clrType == typeof(DateTime) ? DateTime.Parse(s, CultureInfo.CurrentCulture)
                    : Convert.ChangeType(s, clrType, CultureInfo.CurrentCulture);
            }
            else
            {
                converted = Convert.ChangeType(converted, clrType, CultureInfo.InvariantCulture);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b);
    }

    /// <summary>識別字加方括號（] 跳脫為 ]]）</summary>
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    /// <summary>依型別產生 SQL 字面值</summary>
    private static string FormatLiteral(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
        DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'",
        TimeSpan ts => $"'{ts}'",
        Guid g => $"'{g}'",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        string s => $"N'{s.Replace("'", "''")}'",
        char c => $"N'{(c == '\'' ? "''" : c.ToString())}'",
        _ => $"N'{value.ToString()?.Replace("'", "''")}'"
    };
}
```

在 `src/Specurai.Infrastructure/ServiceRegistration.cs` 的「Application - 核心查詢服務」區塊（`services.AddSingleton<ITableQueryService, TableQueryService>();` 之後）加入：

```csharp
        services.AddSingleton<IUpdateSqlGenerator, UpdateSqlGenerator>();
```

- [ ] **Step 4: 執行測試確認通過**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj`
Expected: 全部 PASS（新增 11 個 + 既有不受影響）

- [ ] **Step 5: Commit**

```bash
git add src/Specurai.Application/Models/UpdateSqlModels.cs src/Specurai.Application/Services/UpdateSqlGenerator.cs src/Specurai.Infrastructure/ServiceRegistration.cs tests/Specurai.Application.Tests/Services/UpdateSqlGeneratorTests.cs
git commit -m "feat: 新增 UpdateSqlGenerator 比對異動產生 UPDATE 語句

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Desktop — 可編輯結果格、產生按鈕與對話框

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`（建構子加 `IUpdateSqlGenerator`，兩處 `new SqlQueryDocumentViewModel(...)` 補參數——以內容定位）
- Modify: `src/Specurai.Desktop/Program.cs`（MainWindowViewModel 註冊補 `sp.GetRequiredService<IUpdateSqlGenerator>()`）
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`（按鈕、DataGrid IsReadOnly 綁定）
- Modify: `src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs`（DataContextChanged 掛回呼）
- Create: `src/Specurai.Desktop/Views/KeyColumnPickerWindow.axaml` ＋ `.axaml.cs`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelTests.cs`（追加）

**Interfaces:**
- Consumes: Task 1 `ExecuteQueryWithSchemaAsync`/`QueryResultWithSchema`；Task 3 `IUpdateSqlGenerator`/`UpdateSqlRequest`/`UpdateSqlRow`/`UpdateSqlResult`
- Produces:
  - `SqlQueryDocumentViewModel` DI 建構子改為 `(ISqlQueryRepository, IConnectionManager, ISqlDryRunRepository? = null, IUpdateSqlGenerator? = null)`（第 4 個選擇性參數）
  - 新增 `[ObservableProperty] bool _isResultEditable`、`[RelayCommand] GenerateUpdateSqlAsync`（產生 `GenerateUpdateSqlCommand`）
  - 回呼屬性：`Func<IReadOnlyList<string>, Task<IReadOnlyList<string>?>>? PickKeyColumnsAsync`、`Func<string, Task>? ShowGeneratedSqlAsync`（View 掛真對話框，測試掛假回呼）
  - `MainWindowViewModel` DI 建構子在 `ISqlDryRunRepository sqlDryRunRepository,` 之後加必要參數 `IUpdateSqlGenerator updateSqlGenerator,`
  - `KeyColumnPickerWindow(IReadOnlyList<string> columns)`：`ShowDialog<IReadOnlyList<string>?>`，確定回傳勾選清單、略過/關閉回傳 null

- [ ] **Step 1: 寫失敗測試**

在 `tests/Specurai.Desktop.Tests/ViewModels/SqlQueryDocumentViewModelTests.cs` 檔尾（最後一個 `#endregion` 前的位置比照既有 region 風格）追加。共用輔助與測試（`using Specurai.Application.Models; using Specurai.Application.Services;` 需補在檔頭）：

```csharp
    #region 結果編輯與產生異動SQL測試

    private static QueryResultWithSchema SingleTableResult(bool withKey = true)
    {
        var table = new DataTable();
        table.Columns.Add("EMP_ID", typeof(string));
        table.Columns.Add("EMP_NAME", typeof(string));
        table.Rows.Add("100719", "洪玉如");

        return new QueryResultWithSchema
        {
            Table = table,
            Columns =
            [
                new QueryColumnMetadata { ColumnName = "EMP_ID", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_ID", IsKey = withKey, ClrType = typeof(string) },
                new QueryColumnMetadata { ColumnName = "EMP_NAME", BaseSchema = "dbo", BaseTable = "SYS010", BaseColumn = "EMP_NAME", ClrType = typeof(string) }
            ]
        };
    }

    private static QueryResultWithSchema MultiTableResult()
    {
        var table = new DataTable();
        table.Columns.Add("A", typeof(string));
        table.Rows.Add("x");

        return new QueryResultWithSchema
        {
            Table = table,
            Columns =
            [
                new QueryColumnMetadata { ColumnName = "A", BaseSchema = "dbo", BaseTable = "T1", BaseColumn = "A", ClrType = typeof(string) },
                new QueryColumnMetadata { ColumnName = "B", BaseSchema = "dbo", BaseTable = "T2", BaseColumn = "B", ClrType = typeof(string) }
            ]
        };
    }

    [Fact]
    public async Task 執行查詢_單表結果_應可編輯()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT * FROM SYS010"
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeTrue();
        vm.QueryResults.Should().HaveCount(1);
    }

    [Fact]
    public async Task 執行查詢_多表結果_應唯讀()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MultiTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT ..."
        };

        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    [Fact]
    public async Task 產生異動SQL_無異動_應顯示無異動()
    {
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Any<UpdateSqlRequest>()).Returns(new UpdateSqlResult());
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("無異動");
    }

    [Fact]
    public async Task 產生異動SQL_有異動_應以回呼顯示SQL並用主鍵定位()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE [dbo].[SYS010] ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        string? shownSql = null;
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            ShowGeneratedSqlAsync = sql => { shownSql = sql; return Task.CompletedTask; }
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "洪小玉";   // 模擬編輯

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        shownSql.Should().Contain("UPDATE");
        captured.Should().NotBeNull();
        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID"]);
        captured.IsFallbackKeys.Should().BeFalse();
        captured.Rows[0].Original["EMP_NAME"].Should().Be("洪玉如");   // 快照保留原值
        captured.Rows[0].Current["EMP_NAME"].Should().Be("洪小玉");
        vm.StatusMessage.Should().Contain("1 句");
    }

    [Fact]
    public async Task 產生異動SQL_無主鍵_應呼叫欄位挑選回呼()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult(withKey: false));

        IReadOnlyList<string>? offered = null;
        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            PickKeyColumnsAsync = cols => { offered = cols; return Task.FromResult<IReadOnlyList<string>?>(["EMP_ID"]); },
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "改";

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        offered.Should().Contain(["EMP_ID", "EMP_NAME"]);
        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID"]);
        captured.IsFallbackKeys.Should().BeFalse();
    }

    [Fact]
    public async Task 產生異動SQL_無主鍵且略過挑選_應用全欄位Fallback()
    {
        UpdateSqlRequest? captured = null;
        var generator = Substitute.For<IUpdateSqlGenerator>();
        generator.Generate(Arg.Do<UpdateSqlRequest>(r => captured = r))
            .Returns(new UpdateSqlResult { Sql = "UPDATE ...;", StatementCount = 1 });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult(withKey: false));

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT * FROM SYS010",
            PickKeyColumnsAsync = _ => Task.FromResult<IReadOnlyList<string>?>(null),
            ShowGeneratedSqlAsync = _ => Task.CompletedTask
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.QueryResults[0]["EMP_NAME"] = "改";

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        captured!.KeyColumns.Should().BeEquivalentTo(["EMP_ID", "EMP_NAME"]);
        captured.IsFallbackKeys.Should().BeTrue();
    }

    [Fact]
    public async Task 產生異動SQL_不可編輯結果_應提示僅支援單表()
    {
        var generator = Substitute.For<IUpdateSqlGenerator>();
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MultiTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, null, generator)
        {
            SqlText = "SELECT ..."
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        await vm.GenerateUpdateSqlCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("僅支援單一資料表");
        generator.DidNotReceive().Generate(Arg.Any<UpdateSqlRequest>());
    }

    [Fact]
    public async Task DryRun後_結果應不可編輯()
    {
        var dryRunRepo = Substitute.For<ISqlDryRunRepository>();
        dryRunRepo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = new DataTable()
            });
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, dryRunRepo)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);
        vm.IsResultEditable.Should().BeTrue();

        vm.SqlText = "UPDATE SYS010 SET EMP_NAME = N'x' WHERE EMP_ID = '1'";
        await vm.DryRunCommand.ExecuteAsync(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    [Fact]
    public async Task 清除_應重置可編輯狀態()
    {
        _sqlQueryRepository.ExecuteQueryWithSchemaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SingleTableResult());

        var vm = new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager)
        {
            SqlText = "SELECT * FROM SYS010"
        };
        await vm.ExecuteQueryCommand.ExecuteAsync(null);

        vm.ClearQueryCommand.Execute(null);

        vm.IsResultEditable.Should().BeFalse();
    }

    #endregion
```

**注意**：既有測試中所有走 `ExecuteQueryCommand` 的測試（原本 mock `ExecuteQueryAsync`）需改 mock `ExecuteQueryWithSchemaAsync`——本 Task 把 `ExecuteQueryAsync` 改呼叫新方法（見 Step 3）。逐一檢視既有測試：把 `_sqlQueryRepository.ExecuteQueryAsync(...)` 的 mock 與 `Received` 斷言改為 `ExecuteQueryWithSchemaAsync` 對應形式（回傳 `new QueryResultWithSchema { Table = 原本的DataTable }`）。選取範圍測試的 `Received(1).ExecuteQueryAsync("SELECT 2;", ...)` 改為 `Received(1).ExecuteQueryWithSchemaAsync("SELECT 2;", ...)`。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~SqlQueryDocumentViewModelTests"`
Expected: 編譯失敗（`IsResultEditable`、`GenerateUpdateSqlCommand` 等不存在）

- [ ] **Step 3: 實作 ViewModel**

修改 `src/Specurai.Desktop/ViewModels/SqlQueryDocumentViewModel.cs`（檔頭補 `using Specurai.Application.Models;`；`Specurai.Application.Services` 已有）：

（a）欄位與屬性（`_sqlDryRunRepository` 欄位之後）：

```csharp
    private readonly IUpdateSqlGenerator? _updateSqlGenerator;
    private QueryResultWithSchema? _lastQueryResult;
    private List<Dictionary<string, object?>> _originalRows = [];
```

（`_selectionEnd` 可觀察屬性之後）：

```csharp
    /// <summary>查詢結果是否可編輯（單一資料表來源才開放）</summary>
    [ObservableProperty]
    private bool _isResultEditable;

    /// <summary>無主鍵時的定位欄挑選回呼（View 掛真對話框，測試掛假回呼）；回傳 null 表示略過</summary>
    public Func<IReadOnlyList<string>, Task<IReadOnlyList<string>?>>? PickKeyColumnsAsync { get; set; }

    /// <summary>顯示產生 SQL 的回呼（View 掛 SqlPreviewWindow）</summary>
    public Func<string, Task>? ShowGeneratedSqlAsync { get; set; }
```

（b）DI 建構子加第 4 個選擇性參數：

```csharp
    public SqlQueryDocumentViewModel(
        ISqlQueryRepository sqlQueryRepository,
        IConnectionManager connectionManager,
        ISqlDryRunRepository? sqlDryRunRepository = null,
        IUpdateSqlGenerator? updateSqlGenerator = null)
    {
        _sqlQueryRepository = sqlQueryRepository;
        _connectionManager = connectionManager;
        _sqlDryRunRepository = sqlDryRunRepository;
        _updateSqlGenerator = updateSqlGenerator;
        // …其餘既有內容不變
```

（c）`ExecuteQueryAsync` 改用新方法並建立快照。整段 try 區塊開頭到欄位建立處改為：

```csharp
            IsExecuting = true;
            StatusMessage = "執行中...";
            QueryResults.Clear();
            ResultColumns.Clear();
            DryRunWarnings = string.Empty;
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalRows = [];

            var stopwatch = Stopwatch.StartNew();
            var result = !string.IsNullOrEmpty(_localConnectionString)
                ? await _sqlQueryRepository.ExecuteQueryWithSchemaAsync(sql, _localConnectionString)
                : await _sqlQueryRepository.ExecuteQueryWithSchemaAsync(sql);
            stopwatch.Stop();

            var dataTable = result.Table;
            _lastQueryResult = result;
            IsResultEditable = result.IsSingleTable;

            var metaByName = result.Columns
                .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 建立欄位（包含描述；可編輯結果依中繼資料設定唯讀欄與雙向綁定）
            foreach (DataColumn col in dataTable.Columns)
            {
                var headerText = col.ColumnName;
                if (_columnDescriptions.TryGetValue(col.ColumnName, out var description)
                    && !string.IsNullOrWhiteSpace(description))
                {
                    headerText = $"{col.ColumnName}\n({description})";
                }

                var meta = metaByName.GetValueOrDefault(col.ColumnName);
                var editable = IsResultEditable && meta is { IsReadOnly: false };

                ResultColumns.Add(new DataGridTextColumn
                {
                    Header = headerText,
                    Binding = new Avalonia.Data.Binding($"[{col.ColumnName}]")
                    {
                        Mode = editable ? Avalonia.Data.BindingMode.TwoWay : Avalonia.Data.BindingMode.OneWay
                    },
                    IsReadOnly = !editable,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                });
            }
```

資料轉換迴圈之後（`RowCount = ...` 之前）加快照：

```csharp
            // 快照原值：產生異動 SQL 時以此比對
            _originalRows = QueryResults.Select(r => new Dictionary<string, object?>(r)).ToList();
```

（d）`DryRunAsync` 的 try 區塊開頭（`RowCount = 0;` 之後）與 `ClearQuery` 各加：

```csharp
            IsResultEditable = false;
            _lastQueryResult = null;
            _originalRows = [];
```

（e）新增命令（`DryRunAsync` 之後）：

```csharp
    /// <summary>
    /// 比對結果格的編輯差異，產生 UPDATE 語句（僅產生文字，不執行任何寫入）
    /// </summary>
    [RelayCommand]
    private async Task GenerateUpdateSqlAsync()
    {
        if (_updateSqlGenerator == null)
            return;

        if (_lastQueryResult is not { IsSingleTable: true } schema || !IsResultEditable)
        {
            StatusMessage = "僅支援單一資料表的查詢結果。";
            return;
        }

        if (_originalRows.Count != QueryResults.Count)
        {
            StatusMessage = "結果列數與快照不一致，請重新執行查詢。";
            return;
        }

        // 主鍵優先；無主鍵讓使用者挑選定位欄；略過則全欄位原值 fallback
        var keyColumns = schema.Columns.Where(c => c.IsKey).Select(c => c.ColumnName).ToList();
        var isFallback = false;
        if (keyColumns.Count == 0)
        {
            var candidates = schema.Columns
                .Where(c => !string.IsNullOrEmpty(c.BaseColumn) && c.ClrType != typeof(byte[]))
                .Select(c => c.ColumnName)
                .ToList();

            var picked = PickKeyColumnsAsync != null ? await PickKeyColumnsAsync(candidates) : null;
            if (picked is { Count: > 0 })
            {
                keyColumns = picked.ToList();
            }
            else
            {
                keyColumns = candidates;
                isFallback = true;
            }
        }

        var rows = QueryResults
            .Select((current, i) => new UpdateSqlRow { Original = _originalRows[i], Current = current })
            .ToList();

        var result = _updateSqlGenerator.Generate(new UpdateSqlRequest
        {
            TargetSchema = schema.TargetSchema,
            TargetTable = schema.TargetTable!,
            Columns = schema.Columns,
            KeyColumns = keyColumns,
            IsFallbackKeys = isFallback,
            Rows = rows
        });

        if (result.StatementCount == 0)
        {
            StatusMessage = result.Warnings.Count > 0 ? string.Join("；", result.Warnings) : "無異動。";
            return;
        }

        var warningNote = result.Warnings.Count > 0 ? $"（{string.Join("；", result.Warnings)}）" : "";
        StatusMessage = $"已產生 {result.StatementCount} 句 UPDATE{warningNote}";

        if (ShowGeneratedSqlAsync != null)
            await ShowGeneratedSqlAsync(result.Sql);
    }
```

- [ ] **Step 4: 串接 MainWindowViewModel 與 DI**

`src/Specurai.Desktop/ViewModels/MainWindowViewModel.cs`：
- 欄位：`private readonly IUpdateSqlGenerator? _updateSqlGenerator;`（`_sqlDryRunRepository` 之後；檔頭確認有 `using Specurai.Application.Services;`）
- DI 建構子 `ISqlDryRunRepository sqlDryRunRepository,` 之後加 `IUpdateSqlGenerator updateSqlGenerator,`，指派 `_updateSqlGenerator = updateSqlGenerator;`
- 兩處 `new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository)` 改為 `new SqlQueryDocumentViewModel(_sqlQueryRepository, _connectionManager, _sqlDryRunRepository, _updateSqlGenerator)`
- 既有 `MainWindowViewModelTests` 中 DI 建構呼叫需補一個 `Substitute.For<IUpdateSqlGenerator>()` 參數（機械修正）

`src/Specurai.Desktop/Program.cs` 的 MainWindowViewModel 註冊，在 `sp.GetRequiredService<ISqlDryRunRepository>(),` 之後加：

```csharp
                sp.GetRequiredService<IUpdateSqlGenerator>(),
```

- [ ] **Step 5: AXAML 與對話框**

（a）`src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml`：

- Dry Run 按鈕之後、清除按鈕之前加：

```xml
                    <Button Command="{Binding GenerateUpdateSqlCommand}"
                            IsEnabled="{Binding IsResultEditable}"
                            ToolTip.Tip="比對結果格的編輯差異，產生 UPDATE 語句（僅產生文字，不會執行）">
                        <StackPanel Orientation="Horizontal" Spacing="5">
                            <TextBlock Text="🧾" FontSize="14"/>
                            <TextBlock Text="產生異動SQL"/>
                        </StackPanel>
                    </Button>
```

- DataGrid 的 `IsReadOnly="True"` 改為 `IsReadOnly="{Binding !IsResultEditable}"`

（b）建立 `src/Specurai.Desktop/Views/KeyColumnPickerWindow.axaml`：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Specurai.Desktop.Views.KeyColumnPickerWindow"
        Title="選擇定位欄位"
        Width="360" Height="440"
        WindowStartupLocation="CenterOwner"
        CanResize="True">
    <Grid RowDefinitions="Auto,*,Auto" Margin="12">
        <TextBlock Grid.Row="0" TextWrapping="Wrap" Margin="0,0,0,8"
                   Text="查詢結果沒有主鍵欄位。請勾選用來定位資料列的欄位（WHERE 條件）；略過則以全部欄位的原值定位。"/>
        <ScrollViewer Grid.Row="1">
            <ItemsControl x:Name="ColumnList"/>
        </ScrollViewer>
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right"
                    Margin="0,10,0,0" Spacing="8">
            <Button Content="確定" Click="OnOkClick" Width="80"/>
            <Button Content="略過" Click="OnSkipClick" Width="80"/>
        </StackPanel>
    </Grid>
</Window>
```

建立 `src/Specurai.Desktop/Views/KeyColumnPickerWindow.axaml.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Views;

/// <summary>
/// 無主鍵時的定位欄位挑選視窗：確定回傳勾選欄位清單，略過/關閉回傳 null
/// </summary>
public partial class KeyColumnPickerWindow : Window
{
    private readonly List<CheckBox> _checkBoxes = [];

    public KeyColumnPickerWindow()
    {
        // 設計時建構子
        InitializeComponent();
    }

    public KeyColumnPickerWindow(IReadOnlyList<string> columns) : this()
    {
        foreach (var column in columns)
        {
            var checkBox = new CheckBox { Content = column, Margin = new Avalonia.Thickness(4, 2) };
            _checkBoxes.Add(checkBox);
            ColumnList.Items.Add(checkBox);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selected = _checkBoxes
            .Where(c => c.IsChecked == true)
            .Select(c => c.Content?.ToString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        Close(selected.Count > 0 ? (IReadOnlyList<string>?)selected : null);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

（c）`src/Specurai.Desktop/Views/SqlQueryDocumentView.axaml.cs`：先閱讀現況，在建構子 `InitializeComponent()` 之後加（若已有 DataContextChanged 訂閱則併入）：

```csharp
        // 掛上對話框回呼：ViewModel 保持可單元測試（測試以假回呼替代）
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.SqlQueryDocumentViewModel vm)
                return;

            vm.PickKeyColumnsAsync = async columns =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                    return null;
                var picker = new KeyColumnPickerWindow(columns);
                return await picker.ShowDialog<IReadOnlyList<string>?>(owner);
            };

            vm.ShowGeneratedSqlAsync = async sql =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                    return;
                await new SqlPreviewWindow(sql).ShowDialog(owner);
            };
        };
```

（需要的 using：`System.Collections.Generic`、`Avalonia.Controls`——依現有檔案補齊。）

- [ ] **Step 6: 執行測試確認通過**

Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj && dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 建置成功（XAML 編譯通過）、全部 PASS（新增 9 個 + 既有測試改 mock 後全過）

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Desktop tests/Specurai.Desktop.Tests
git commit -m "feat: 查詢結果格可編輯並產生異動 UPDATE 語句

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 全方案驗證與收尾

**Files:**
- Modify: `README.md`（桌面功能一覽若有 SQL 查詢說明處，補「結果格編輯與產生異動SQL」一句；以搜尋 `Dry Run` 或 `SQL 查詢` 定位）

**Interfaces:**
- Consumes: 前述全部
- Produces: 全方案綠燈與活庫驗證

- [ ] **Step 1: 全方案建置與測試**

Run: `dotnet build && dotnet test`
Expected: 建置成功（僅 1 個既有 SqlPreviewWindow 警告）、全部測試 PASS

- [ ] **Step 2: 活庫手動驗證**（控制者執行，測試庫 WayDoSoft01-Test）

1. 桌面 App：`SELECT * FROM SYS010 WHERE DEL_MARK = 'N'` → 結果格可編輯（timestamp 欄不可編輯）→ 改一格 EMP_NAME → 產生異動SQL → 彈窗顯示 `UPDATE [dbo].[SYS010] SET [EMP_NAME] = N'...' WHERE [EMP_ID] = ...`（SYS010 若有主鍵）
2. 複製產生的 SQL 到編輯器 → 選取 → F6 Dry Run → 前後對照正確、已回滾
3. JOIN 查詢 → 結果唯讀、按鈕停用/提示
4. 無主鍵表 → 挑選欄位視窗出現；略過 → SQL 開頭有警告註解
5. 未改任何格 → 「無異動」

- [ ] **Step 3: 文件與 Commit**

```bash
git add README.md
git commit -m "docs: 補充查詢結果編輯與產生異動 SQL 說明

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: 程式碼審查**

依專案規範以 `superpowers:requesting-code-review` 進行審查後回報完成。
