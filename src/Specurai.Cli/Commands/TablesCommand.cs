using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 物件瀏覽命令群組
/// </summary>
public static class TablesCommand
{
    public static Command Create()
    {
        var command = new Command("tables", "物件瀏覽（資料表、檢視、預存程序、函式）");
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateColumnsCommand());
        command.AddCommand(CreateIndexesCommand());
        command.AddCommand(CreateRelationsCommand());
        command.AddCommand(CreateDefinitionCommand());
        command.AddCommand(CreateParametersCommand());
        command.AddCommand(CreateRowCountCommand());
        command.AddCommand(CreateStatsCommand());
        command.AddCommand(CreateColumnStatsCommand());
        command.AddCommand(CreateCreateSqlCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var typeOption = new Option<string?>("--type", "物件類型：TABLE, VIEW, PROC, FUNC");
        var schemaOption = new Option<string?>("--schema", "篩選 Schema");
        var command = new Command("list", "列出所有物件") { typeOption, schemaOption };

        command.SetHandler(async (type, schema) =>
        {
            var service = Program.Services.GetRequiredService<ITableQueryService>();

            // 將簡寫轉為完整類型名稱
            var fullType = type?.ToUpperInvariant() switch
            {
                "TABLE" => "BASE TABLE",
                "VIEW" => "VIEW",
                "PROC" or "PROCEDURE" => "PROCEDURE",
                "FUNC" or "FUNCTION" => "FUNCTION",
                null => null,
                _ => type
            };

            var tables = fullType != null
                ? await service.GetTablesByTypeAsync(fullType)
                : await service.GetAllTablesAsync();

            if (!string.IsNullOrEmpty(schema))
                tables = tables.Where(t => t.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase)).ToList();

            if (CliOutput.JsonMode)
            {
                var data = tables.Select(t => new
                {
                    t.Schema,
                    t.Name,
                    t.Type,
                    t.Description
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (tables.Count == 0)
                {
                    CliOutput.Info("沒有找到任何物件。");
                    return;
                }

                var table = new Table();
                table.AddColumn("Schema");
                table.AddColumn("名稱");
                table.AddColumn("類型");
                table.AddColumn("說明");

                foreach (var t in tables)
                {
                    var typeDisplay = t.Type switch
                    {
                        "BASE TABLE" => "[blue]TABLE[/]",
                        "VIEW" => "[cyan]VIEW[/]",
                        "PROCEDURE" => "[yellow]PROC[/]",
                        "FUNCTION" => "[magenta]FUNC[/]",
                        _ => t.Type
                    };
                    table.AddRow(
                        t.Schema.EscapeMarkup(),
                        t.Name.EscapeMarkup(),
                        typeDisplay,
                        (t.Description ?? "").EscapeMarkup());
                }

                AnsiConsole.Write(table);
                CliOutput.Info($"共 {tables.Count} 個物件");
            }
        }, typeOption, schemaOption);

        return command;
    }

    private static Command CreateColumnsCommand()
    {
        var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
        var typeOption = new Option<string>("--type", () => "BASE TABLE", "物件類型");
        var command = new Command("columns", "顯示欄位資訊") { objectArg, typeOption };

        command.SetHandler(async (objectName, type) =>
        {
            var (schema, name) = ParseObjectName(objectName);
            var service = Program.Services.GetRequiredService<ITableQueryService>();
            var columns = await service.GetColumnsAsync(type, schema, name);

            if (CliOutput.JsonMode)
            {
                var data = columns.Select(c => new
                {
                    c.ColumnName,
                    c.DataType,
                    c.Length,
                    c.IsNullable,
                    c.IsPrimaryKey,
                    c.IsUniqueKey,
                    c.IsIndexed,
                    c.DefaultValue,
                    c.Description
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (columns.Count == 0)
                {
                    CliOutput.Info($"物件 {schema}.{name} 沒有欄位，或物件不存在。");
                    return;
                }

                var table = new Table().Title($"[bold]{schema}.{name}[/] 欄位");
                table.AddColumn("欄位");
                table.AddColumn("型別");
                table.AddColumn("長度");
                table.AddColumn("可 Null");
                table.AddColumn("PK");
                table.AddColumn("索引");
                table.AddColumn("預設值");
                table.AddColumn("說明");

                foreach (var c in columns)
                {
                    table.AddRow(
                        c.ColumnName.EscapeMarkup(),
                        c.DataType.EscapeMarkup(),
                        c.Length?.ToString() ?? "",
                        c.IsNullable ? "✓" : "",
                        c.IsPrimaryKey ? "[yellow]PK[/]" : "",
                        c.IsIndexed ? "✓" : "",
                        (c.DefaultValue ?? "").EscapeMarkup(),
                        (c.Description ?? "").EscapeMarkup());
                }

                AnsiConsole.Write(table);
            }
        }, objectArg, typeOption);

        return command;
    }

    private static Command CreateIndexesCommand()
    {
        var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
        var command = new Command("indexes", "顯示索引資訊") { objectArg };

        command.SetHandler(async (objectName) =>
        {
            var (schema, name) = ParseObjectName(objectName);
            var service = Program.Services.GetRequiredService<ITableQueryService>();
            var indexes = await service.GetIndexesAsync(schema, name);

            if (CliOutput.JsonMode)
            {
                var data = indexes.Select(i => new
                {
                    i.Name,
                    i.Type,
                    i.Columns,
                    i.IsUnique,
                    i.IsPrimaryKey,
                    i.CreateDate
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (indexes.Count == 0)
                {
                    CliOutput.Info($"物件 {schema}.{name} 沒有索引。");
                    return;
                }

                var table = new Table().Title($"[bold]{schema}.{name}[/] 索引");
                table.AddColumn("名稱");
                table.AddColumn("類型");
                table.AddColumn("欄位");
                table.AddColumn("唯一");
                table.AddColumn("PK");

                foreach (var i in indexes)
                {
                    table.AddRow(
                        i.Name.EscapeMarkup(),
                        i.Type.EscapeMarkup(),
                        i.ColumnsDisplay.EscapeMarkup(),
                        i.IsUnique ? "✓" : "",
                        i.IsPrimaryKey ? "[yellow]PK[/]" : "");
                }

                AnsiConsole.Write(table);
            }
        }, objectArg);

        return command;
    }

    private static Command CreateRelationsCommand()
    {
        var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
        var command = new Command("relations", "顯示外鍵關聯") { objectArg };

        command.SetHandler(async (objectName) =>
        {
            var (schema, name) = ParseObjectName(objectName);
            var service = Program.Services.GetRequiredService<ITableQueryService>();
            var relations = await service.GetRelationsAsync(schema, name);

            if (CliOutput.JsonMode)
            {
                var data = relations.Select(r => new
                {
                    r.ConstraintName,
                    r.FromTable,
                    r.FromColumn,
                    r.ToTable,
                    r.ToColumn,
                    Type = r.Type.ToString()
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (relations.Count == 0)
                {
                    CliOutput.Info($"物件 {schema}.{name} 沒有外鍵關聯。");
                    return;
                }

                var table = new Table().Title($"[bold]{schema}.{name}[/] 關聯");
                table.AddColumn("約束名稱");
                table.AddColumn("來源表.欄位");
                table.AddColumn("方向");
                table.AddColumn("目標表.欄位");

                foreach (var r in relations)
                {
                    var direction = r.Type == Domain.Entities.RelationType.Outgoing ? "→" : "←";
                    table.AddRow(
                        r.ConstraintName.EscapeMarkup(),
                        $"{r.FromTable}.{r.FromColumn}".EscapeMarkup(),
                        direction,
                        $"{r.ToTable}.{r.ToColumn}".EscapeMarkup());
                }

                AnsiConsole.Write(table);
            }
        }, objectArg);

        return command;
    }

    private static Command CreateDefinitionCommand()
    {
        var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
        var command = new Command("definition", "顯示 SP/Function 的原始碼") { objectArg };

        command.SetHandler(async (objectName) =>
        {
            var (schema, name) = ParseObjectName(objectName);
            var service = Program.Services.GetRequiredService<ITableQueryService>();
            var definition = await service.GetDefinitionAsync(schema, name);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new { Schema = schema, Name = name, Definition = definition });
            }
            else
            {
                if (string.IsNullOrEmpty(definition))
                {
                    CliOutput.Info($"物件 {schema}.{name} 沒有定義，或物件不存在。");
                    return;
                }

                AnsiConsole.MarkupLine($"[bold]-- {schema}.{name}[/]");
                Console.WriteLine(definition);
            }
        }, objectArg);

        return command;
    }

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
                table.AddColumn(new TableColumn("約略列數").RightAligned());
                table.AddColumn(new TableColumn("欄位").RightAligned());
                table.AddColumn(new TableColumn("索引").RightAligned());
                table.AddColumn(new TableColumn("總大小(MB)").RightAligned());

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
                table.AddColumn(new TableColumn("使用次數").RightAligned());
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

    /// <summary>
    /// 解析 schema.name 格式，預設 schema 為 dbo
    /// </summary>
    internal static (string schema, string name) ParseObjectName(string objectName)
    {
        var parts = objectName.Split('.', 2);
        return parts.Length == 2
            ? (parts[0], parts[1])
            : ("dbo", parts[0]);
    }
}
