using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 欄位跨資料庫搜尋命令群組
/// </summary>
public static class ColumnsCommand
{
    public static Command Create()
    {
        var command = new Command("columns", "欄位搜尋");
        command.AddCommand(CreateSearchCommand());
        return command;
    }

    private static Command CreateSearchCommand()
    {
        var keywordArg = new Argument<string>("keyword", "欄位名稱關鍵字");
        var profilesOption = new Option<string?>("--profiles", "指定連線名稱清單（逗號分隔），預設使用全部已儲存連線");
        var exactOption = new Option<bool>("--exact", "精確比對");
        var tableOption = new Option<string?>("--table", "資料表名稱關鍵字");
        var command = new Command("search", "跨資料庫搜尋欄位") { keywordArg, profilesOption, exactOption, tableOption };

        command.SetHandler(async (keyword, profilesStr, exact, table) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var allProfiles = cm.GetAllProfiles();

            IReadOnlyList<Guid> profileIds;
            if (!string.IsNullOrWhiteSpace(profilesStr))
            {
                var names = profilesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                profileIds = allProfiles
                    .Where(p => names.Any(n => n.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.Id).ToList();
            }
            else
            {
                profileIds = allProfiles.Select(p => p.Id).ToList();
            }

            if (profileIds.Count == 0)
            {
                CliOutput.Error("沒有可搜尋的連線。");
                Environment.ExitCode = 2;
                return;
            }

            var service = Program.Services.GetRequiredService<IColumnSearchService>();
            var results = await service.SearchColumnsMultiAsync(keyword, profileIds, exact, table);

            if (CliOutput.JsonMode) { CliOutput.Success(results, results.Count); return; }
            if (results.Count == 0) { CliOutput.Info("未找到符合的欄位。"); return; }

            var resultTable = new Table().Title($"欄位搜尋：{keyword.EscapeMarkup()}");
            resultTable.AddColumn("資料庫"); resultTable.AddColumn("物件"); resultTable.AddColumn("類型"); resultTable.AddColumn("欄位"); resultTable.AddColumn("資料型別"); resultTable.AddColumn("說明");
            foreach (var r in results)
            {
                resultTable.AddRow(
                    r.DatabaseName.EscapeMarkup(),
                    r.FullObjectName.EscapeMarkup(),
                    r.ObjectType.EscapeMarkup(),
                    r.ColumnName.EscapeMarkup(),
                    r.DataType.EscapeMarkup(),
                    (r.Description ?? "").EscapeMarkup());
            }
            AnsiConsole.Write(resultTable);
            CliOutput.Info($"共 {results.Count} 筆。");
        }, keywordArg, profilesOption, exactOption, tableOption);

        return command;
    }
}
