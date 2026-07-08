using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;

namespace Specurai.Cli.Commands;

/// <summary>
/// 資料庫清單命令（SSMS 式：列出連線伺服器上的所有使用者資料庫）
/// </summary>
public static class DatabasesCommand
{
    public static Command Create()
    {
        var command = new Command("databases", "列出伺服器上的所有使用者資料庫");

        command.SetHandler(async () =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = new ConnectionResolver(cm).Resolve(Program.CurrentOptions);
            if (profile == null)
            {
                CliOutput.Error("找不到連線設定。請使用 --server 或 --profile 指定連線。");
                Environment.ExitCode = 1;
                return;
            }

            IReadOnlyList<string> databases;
            try
            {
                databases = await cm.GetDatabasesAsync(profile);
            }
            catch (Exception ex)
            {
                CliOutput.Error($"無法列舉資料庫（{profile.Server}）：{ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (CliOutput.JsonMode)
            {
                var data = databases.Select(name => new
                {
                    Name = name,
                    IsProfileDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase)
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (databases.Count == 0)
                {
                    CliOutput.Info("伺服器上沒有使用者資料庫。");
                    return;
                }

                var table = new Table().Title($"[bold]{profile.Server}[/] 使用者資料庫");
                table.AddColumn("資料庫");
                table.AddColumn("預設");

                foreach (var name in databases)
                {
                    var isDefault = string.Equals(name, profile.Database, StringComparison.OrdinalIgnoreCase);
                    table.AddRow(name.EscapeMarkup(), isDefault ? "[green]✓[/]" : "");
                }

                AnsiConsole.Write(table);
                CliOutput.Info($"共 {databases.Count} 個資料庫");
            }
        });

        return command;
    }
}
