using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 匯出命令群組
/// </summary>
public static class ExportCommand
{
    public static Command Create()
    {
        var command = new Command("export", "匯出資料");
        command.AddCommand(CreateExcelCommand());
        return command;
    }

    private static Command CreateExcelCommand()
    {
        var outputOption = new Option<string?>(["--output", "-o"], "輸出檔案路徑（預設：specurai-export.xlsx）");
        var tableOption = new Option<string?>("--table", "指定表格（格式：schema.name），不指定則匯出全部");
        var command = new Command("excel", "匯出到 Excel") { outputOption, tableOption };

        command.SetHandler(async (output, tableName) =>
        {
            var service = Program.Services.GetRequiredService<IExportService>();

            try
            {
                byte[] data;
                string defaultName;

                if (!string.IsNullOrEmpty(tableName))
                {
                    var (schema, name) = TablesCommand.ParseObjectName(tableName);
                    if (!CliOutput.JsonMode)
                        CliOutput.Info($"正在匯出 {schema}.{name}...");
                    data = await service.ExportTableToExcelAsync(schema, name);
                    defaultName = $"{schema}.{name}.xlsx";
                }
                else
                {
                    if (!CliOutput.JsonMode)
                        CliOutput.Info("正在匯出所有資料表...");
                    data = await service.ExportToExcelAsync();
                    defaultName = "specurai-export.xlsx";
                }

                var filePath = output ?? defaultName;
                await File.WriteAllBytesAsync(filePath, data);

                if (CliOutput.JsonMode)
                {
                    CliOutput.Success(new
                    {
                        FilePath = Path.GetFullPath(filePath),
                        SizeBytes = data.Length,
                        Message = "匯出完成"
                    });
                }
                else
                {
                    CliOutput.SuccessMessage($"已匯出至 {Path.GetFullPath(filePath)} ({data.Length / 1024} KB)");
                }
            }
            catch (Exception ex)
            {
                CliOutput.Error($"匯出失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, outputOption, tableOption);

        return command;
    }
}
