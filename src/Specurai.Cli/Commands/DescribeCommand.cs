using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 描述編輯命令群組
/// </summary>
public static class DescribeCommand
{
    public static Command Create()
    {
        var command = new Command("describe", "編輯物件/欄位說明");
        command.AddCommand(CreateTableCommand());
        command.AddCommand(CreateColumnCommand());
        return command;
    }

    private static Command CreateTableCommand()
    {
        var objectArg = new Argument<string>("object", "物件名稱（格式：schema.name）");
        var descArg = new Argument<string>("description", "說明文字");
        var typeOption = new Option<string>("--type", () => "BASE TABLE", "物件類型（BASE TABLE, VIEW, PROCEDURE, FUNCTION）");
        var dryRunOption = new Option<bool>("--dry-run", "僅預覽，不實際執行");
        var command = new Command("table", "更新物件說明") { objectArg, descArg, typeOption, dryRunOption };

        command.SetHandler(async (objectName, description, type, dryRun) =>
        {
            var (schema, name) = TablesCommand.ParseObjectName(objectName);

            if (dryRun)
            {
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Action = "update_table_description", Schema = schema, Name = name, Description = description, Type = type, DryRun = true });
                else
                    CliOutput.Info($"[預覽] 將更新 {schema}.{name} 的說明為「{description}」");
                return;
            }

            var service = Program.Services.GetRequiredService<ITableQueryService>();
            await service.UpdateTableDescriptionAsync(type, schema, name, description);

            if (CliOutput.JsonMode)
                CliOutput.Success(new { Schema = schema, Name = name, Description = description, Message = "說明已更新" });
            else
                CliOutput.SuccessMessage($"已更新 {schema}.{name} 的說明");
        }, objectArg, descArg, typeOption, dryRunOption);

        return command;
    }

    private static Command CreateColumnCommand()
    {
        var objectArg = new Argument<string>("object", "欄位名稱（格式：schema.table.column）");
        var descArg = new Argument<string>("description", "說明文字");
        var typeOption = new Option<string>("--type", () => "TABLE", "物件類型（TABLE, VIEW）");
        var dryRunOption = new Option<bool>("--dry-run", "僅預覽，不實際執行");
        var command = new Command("column", "更新欄位說明") { objectArg, descArg, typeOption, dryRunOption };

        command.SetHandler(async (objectName, description, type, dryRun) =>
        {
            var parts = objectName.Split('.', 3);
            string schema, tableName, columnName;

            if (parts.Length == 3)
            {
                schema = parts[0];
                tableName = parts[1];
                columnName = parts[2];
            }
            else if (parts.Length == 2)
            {
                schema = "dbo";
                tableName = parts[0];
                columnName = parts[1];
            }
            else
            {
                CliOutput.Error("欄位名稱格式錯誤，需為 schema.table.column 或 table.column");
                Environment.ExitCode = 2;
                return;
            }

            if (dryRun)
            {
                if (CliOutput.JsonMode)
                    CliOutput.Success(new { Action = "update_column_description", Schema = schema, Table = tableName, Column = columnName, Description = description, DryRun = true });
                else
                    CliOutput.Info($"[預覽] 將更新 {schema}.{tableName}.{columnName} 的說明為「{description}」");
                return;
            }

            var service = Program.Services.GetRequiredService<ITableQueryService>();
            await service.UpdateColumnDescriptionAsync(schema, tableName, columnName, description, type);

            if (CliOutput.JsonMode)
                CliOutput.Success(new { Schema = schema, Table = tableName, Column = columnName, Description = description, Message = "說明已更新" });
            else
                CliOutput.SuccessMessage($"已更新 {schema}.{tableName}.{columnName} 的說明");
        }, objectArg, descArg, typeOption, dryRunOption);

        return command;
    }
}
