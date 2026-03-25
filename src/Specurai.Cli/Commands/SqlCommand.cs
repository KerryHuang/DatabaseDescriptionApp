using System.CommandLine;
using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Interfaces;

namespace Specurai.Cli.Commands;

/// <summary>
/// SQL 查詢命令群組
/// </summary>
public static class SqlCommand
{
    public static Command Create()
    {
        var command = new Command("sql", "SQL 查詢");
        command.AddCommand(CreateQueryCommand());
        command.AddCommand(CreateSearchColumnsCommand());
        return command;
    }

    private static Command CreateQueryCommand()
    {
        var sqlArg = new Argument<string>("sql", "SQL 查詢語句（僅 SELECT）");
        var command = new Command("query", "執行唯讀 SQL 查詢") { sqlArg };

        command.SetHandler(async (sql) =>
        {
            var repo = Program.Services.GetRequiredService<ISqlQueryRepository>();

            try
            {
                var result = await repo.ExecuteQueryAsync(sql);

                if (CliOutput.JsonMode)
                {
                    var rows = new List<Dictionary<string, object?>>();
                    foreach (DataRow row in result.Rows)
                    {
                        var dict = new Dictionary<string, object?>();
                        foreach (DataColumn col in result.Columns)
                        {
                            dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                        }
                        rows.Add(dict);
                    }
                    CliOutput.Success(rows, rows.Count);
                }
                else
                {
                    if (result.Rows.Count == 0)
                    {
                        CliOutput.Info("查詢未回傳任何結果。");
                        return;
                    }

                    var table = new Table();
                    foreach (DataColumn col in result.Columns)
                    {
                        table.AddColumn(col.ColumnName.EscapeMarkup());
                    }

                    foreach (DataRow row in result.Rows)
                    {
                        var cells = new string[result.Columns.Count];
                        for (var i = 0; i < result.Columns.Count; i++)
                        {
                            cells[i] = (row[i] == DBNull.Value ? "" : row[i]?.ToString() ?? "").EscapeMarkup();
                        }
                        table.AddRow(cells);
                    }

                    AnsiConsole.Write(table);
                    CliOutput.Info($"共 {result.Rows.Count} 筆");
                }
            }
            catch (Exception ex)
            {
                CliOutput.Error($"查詢失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, sqlArg);

        return command;
    }

    private static Command CreateSearchColumnsCommand()
    {
        var nameArg = new Argument<string>("name", "欄位名稱關鍵字");
        var exactOption = new Option<bool>("--exact", "精確比對");
        var tableOption = new Option<string?>("--table", "篩選資料表名稱");
        var profilesOption = new Option<string?>("--profiles", "指定連線名稱（逗號分隔，跨庫搜尋）");
        var allProfilesOption = new Option<bool>("--all-profiles", "搜尋所有已儲存的連線");

        var command = new Command("search-columns", "搜尋欄位名稱") { nameArg, exactOption, tableOption, profilesOption, allProfilesOption };

        command.SetHandler(async (name, exact, tableName, profiles, allProfiles) =>
        {
            // 判斷是否為跨庫搜尋
            if (!string.IsNullOrEmpty(profiles) || allProfiles)
            {
                await SearchColumnsMultiAsync(name, exact, tableName, profiles, allProfiles);
            }
            else
            {
                await SearchColumnsSingleAsync(name, exact, tableName);
            }
        }, nameArg, exactOption, tableOption, profilesOption, allProfilesOption);

        return command;
    }

    /// <summary>
    /// 單一資料庫欄位搜尋
    /// </summary>
    private static async Task SearchColumnsSingleAsync(string name, bool exact, string? tableName)
    {
        var repo = Program.Services.GetRequiredService<ISqlQueryRepository>();
        var results = await repo.SearchColumnsAsync(name, exact, tableName);

        if (CliOutput.JsonMode)
        {
            var data = results.Select(r => new
            {
                r.ColumnName,
                r.SchemaName,
                r.ObjectName,
                r.ObjectType,
                r.DataType,
                r.Description
            }).ToList();
            CliOutput.Success(data, data.Count);
        }
        else
        {
            if (results.Count == 0)
            {
                CliOutput.Info($"找不到符合「{name}」的欄位。");
                return;
            }

            var table = new Table().Title($"搜尋結果：[bold]{name}[/]");
            table.AddColumn("Schema");
            table.AddColumn("物件");
            table.AddColumn("欄位");
            table.AddColumn("型別");
            table.AddColumn("說明");

            foreach (var r in results)
            {
                table.AddRow(
                    r.SchemaName.EscapeMarkup(),
                    r.ObjectName.EscapeMarkup(),
                    r.ColumnName.EscapeMarkup(),
                    r.DataType.EscapeMarkup(),
                    (r.Description ?? "").EscapeMarkup());
            }

            AnsiConsole.Write(table);
            CliOutput.Info($"共 {results.Count} 筆");
        }
    }

    /// <summary>
    /// 跨資料庫欄位搜尋
    /// </summary>
    private static async Task SearchColumnsMultiAsync(string name, bool exact, string? tableName, string? profiles, bool allProfiles)
    {
        var cm = Program.Services.GetRequiredService<IConnectionManager>();
        var searchService = Program.Services.GetRequiredService<IColumnSearchService>();

        List<Guid> profileIds;

        if (allProfiles)
        {
            profileIds = cm.GetAllProfiles().Select(p => p.Id).ToList();
        }
        else
        {
            var profileNames = profiles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            profileIds = [];
            foreach (var pn in profileNames)
            {
                var profile = cm.GetAllProfiles()
                    .FirstOrDefault(p => p.Name.Equals(pn, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                    profileIds.Add(profile.Id);
                else
                    CliOutput.Warning($"找不到連線「{pn}」，已跳過");
            }
        }

        if (profileIds.Count == 0)
        {
            CliOutput.Error("沒有可搜尋的連線。");
            Environment.ExitCode = 1;
            return;
        }

        if (!CliOutput.JsonMode)
            CliOutput.Info($"正在搜尋 {profileIds.Count} 個資料庫...");

        var results = await searchService.SearchColumnsMultiAsync(name, profileIds, exact, tableName);

        if (CliOutput.JsonMode)
        {
            var data = results.Select(r => new
            {
                r.DatabaseName,
                r.ColumnName,
                r.SchemaName,
                r.ObjectName,
                r.ObjectType,
                r.DataType,
                r.PrimaryDataType,
                r.MatchesPrimaryDataType,
                r.Description
            }).ToList();
            CliOutput.Success(data, data.Count);
        }
        else
        {
            if (results.Count == 0)
            {
                CliOutput.Info($"在 {profileIds.Count} 個資料庫中找不到符合「{name}」的欄位。");
                return;
            }

            var table = new Table().Title($"跨庫搜尋：[bold]{name}[/]");
            table.AddColumn("資料庫");
            table.AddColumn("Schema");
            table.AddColumn("物件");
            table.AddColumn("欄位");
            table.AddColumn("型別");
            table.AddColumn("一致");

            foreach (var r in results)
            {
                var consistent = r.MatchesPrimaryDataType ? "[green]✓[/]" : "[red]✗[/]";
                table.AddRow(
                    r.DatabaseName.EscapeMarkup(),
                    r.SchemaName.EscapeMarkup(),
                    r.ObjectName.EscapeMarkup(),
                    r.ColumnName.EscapeMarkup(),
                    r.DataType.EscapeMarkup(),
                    consistent);
            }

            AnsiConsole.Write(table);
            CliOutput.Info($"共 {results.Count} 筆，來自 {results.Select(r => r.DatabaseName).Distinct().Count()} 個資料庫");
        }
    }
}
