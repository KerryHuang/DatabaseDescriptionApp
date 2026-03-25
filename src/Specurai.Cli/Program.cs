using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Cli.Commands;
using Specurai.Cli.Output;
using Specurai.Infrastructure;

namespace Specurai.Cli;

public static class Program
{
    /// <summary>
    /// DI 服務提供者
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// 目前的全域選項
    /// </summary>
    public static GlobalOptions CurrentOptions { get; private set; } = new();

    public static async Task<int> Main(string[] args)
    {
        // 全域選項
        var serverOption = new Option<string?>(["--server", "-s"], "伺服器位址");
        var portOption = new Option<int?>("--port", "連接埠（預設 1433）");
        var databaseOption = new Option<string?>(["--database", "-d"], "資料庫名稱");
        var userOption = new Option<string?>(["--user", "-u"], "SQL 帳號");
        var passwordOption = new Option<string?>("--password", "SQL 密碼");
        var connectionStringOption = new Option<string?>("--connection-string", "完整連線字串");
        var connStdinOption = new Option<bool>("--conn-stdin", "從 stdin 讀取 JSON 連線");
        var profileOption = new Option<string?>(["--profile", "-p"], "使用已儲存的連線名稱");
        var jsonOption = new Option<bool>("--json", "JSON 輸出");

        var rootCommand = new RootCommand("Specurai CLI - SQL Server 資料庫結構查詢、效能診斷與跨環境比對工具")
        {
            serverOption,
            portOption,
            databaseOption,
            userOption,
            passwordOption,
            connectionStringOption,
            connStdinOption,
            profileOption,
            jsonOption
        };

        // 註冊命令群組
        rootCommand.AddCommand(ConnCommand.Create());
        rootCommand.AddCommand(TablesCommand.Create());
        rootCommand.AddCommand(DescribeCommand.Create());
        rootCommand.AddCommand(SqlCommand.Create());
        rootCommand.AddCommand(ExportCommand.Create());
        rootCommand.AddCommand(PerfCommand.Create());
        rootCommand.AddCommand(HealthCommand.Create());
        rootCommand.AddCommand(SchemaCommand.Create());
        rootCommand.AddCommand(UsageCommand.Create());
        rootCommand.AddCommand(JobsCommand.Create());
        rootCommand.AddCommand(MaintenanceCommand.Create());

        // 使用中介軟體捕獲全域選項
        var builder = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .AddMiddleware(async (context, next) =>
            {
                // 解析全域選項
                var parseResult = context.ParseResult;
                CurrentOptions = new GlobalOptions
                {
                    Server = parseResult.GetValueForOption(serverOption),
                    Port = parseResult.GetValueForOption(portOption),
                    Database = parseResult.GetValueForOption(databaseOption),
                    User = parseResult.GetValueForOption(userOption),
                    Password = parseResult.GetValueForOption(passwordOption),
                    ConnectionString = parseResult.GetValueForOption(connectionStringOption),
                    ConnStdin = parseResult.GetValueForOption(connStdinOption),
                    Profile = parseResult.GetValueForOption(profileOption),
                    Json = parseResult.GetValueForOption(jsonOption)
                };

                // --json 旗標控制輸出模式
                CliOutput.JsonMode = CurrentOptions.Json;

                // 初始化 DI
                var services = new ServiceCollection();
                services.AddSpecuraiCore();
                Services = services.BuildServiceProvider();

                await next(context);
            });

        return await builder.Build().InvokeAsync(args);
    }
}
