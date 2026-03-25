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
