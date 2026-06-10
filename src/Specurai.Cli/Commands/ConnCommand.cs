using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Specurai.Application.Services;
using Specurai.Cli.Output;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Commands;

/// <summary>
/// 連線管理命令群組
/// </summary>
public static class ConnCommand
{
    public static Command Create()
    {
        var command = new Command("conn", "連線管理");
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateAddCommand());
        command.AddCommand(CreateSwitchCommand());
        command.AddCommand(CreateTestCommand());
        command.AddCommand(CreateRemoveCommand());
        command.AddCommand(CreateImportCommand());
        command.AddCommand(CreateUpdateCommand());
        command.AddCommand(CreateExportCommand());
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var nameArg = new Argument<string>("name", "要更新的連線名稱");
        var newNameOpt = new Option<string?>("--new-name", "新名稱");
        var serverOpt = new Option<string?>("--server", "新伺服器位址");
        var databaseOpt = new Option<string?>("--database", "新資料庫名稱");
        var authOpt = new Option<string?>("--auth", "新認證方式（Windows 或 SqlServer）");
        var userOpt = new Option<string?>("--user", "新 SQL 帳號");
        var passwordOpt = new Option<string?>("--password", "新 SQL 密碼");

        var command = new Command("update", "更新既有連線") { nameArg };
        command.AddOption(newNameOpt);
        command.AddOption(serverOpt);
        command.AddOption(databaseOpt);
        command.AddOption(authOpt);
        command.AddOption(userOpt);
        command.AddOption(passwordOpt);

        command.SetHandler((name, newName, server, database, auth, user, password) =>
        {
            if (!HasProfileUpdate(newName, server, database, auth, user, password))
            {
                CliOutput.Error("未提供任何要更新的欄位（--new-name / --server / --database / --auth / --user / --password）");
                Environment.ExitCode = 2;
                return;
            }

            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = cm.GetAllProfiles()
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                CliOutput.Error($"找不到連線「{name}」");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                ApplyProfileUpdates(profile, newName, server, database, auth, user, password);
                cm.UpdateProfile(profile);
            }
            catch (Exception ex)
            {
                CliOutput.Error($"更新連線失敗：{ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (CliOutput.JsonMode)
                CliOutput.Success(new { profile.Id, profile.Name, Message = "連線已更新" });
            else
                CliOutput.SuccessMessage($"已更新連線「{profile.Name}」");
        }, nameArg, newNameOpt, serverOpt, databaseOpt, authOpt, userOpt, passwordOpt);

        return command;
    }

    private static Command CreateExportCommand()
    {
        var outputOpt = new Option<string>(["--output", "-o"], "輸出檔案路徑") { IsRequired = true };
        var includePwdOpt = new Option<bool>("--include-passwords", "匯出時包含密碼（預設 false）");

        var command = new Command("export", "匯出連線設定為 JSON") { outputOpt, includePwdOpt };

        command.SetHandler((output, includePasswords) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var exportService = Program.Services.GetRequiredService<IConnectionExportService>();

            var profiles = cm.GetAllProfiles();
            if (profiles.Count == 0)
            {
                CliOutput.Error("沒有連線設定可匯出。");
                Environment.ExitCode = 1;
                return;
            }

            if (includePasswords)
                CliOutput.Warning("匯出檔案將包含明文密碼，請妥善保管。");

            int count;
            try
            {
                count = ExportProfilesToFile(exportService, profiles, output, includePasswords);
            }
            catch (Exception ex)
            {
                CliOutput.Error($"匯出連線失敗：{ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (CliOutput.JsonMode)
                CliOutput.Success(new { Output = output, Count = count });
            else
                CliOutput.SuccessMessage($"已匯出 {count} 個連線設定至 {output}");
        }, outputOpt, includePwdOpt);

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出所有連線");
        command.SetHandler(() =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profiles = cm.GetAllProfiles();
            var current = cm.GetCurrentProfile();

            if (CliOutput.JsonMode)
            {
                var data = profiles.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Server,
                    p.Database,
                    AuthType = p.AuthType.ToString(),
                    IsCurrent = current?.Id == p.Id,
                    p.IsDefault
                }).ToList();
                CliOutput.Success(data, data.Count);
            }
            else
            {
                if (profiles.Count == 0)
                {
                    CliOutput.Info("尚未設定任何連線。使用 specurai conn add 新增連線。");
                    return;
                }

                var table = new Table();
                table.AddColumn("名稱");
                table.AddColumn("伺服器");
                table.AddColumn("資料庫");
                table.AddColumn("驗證");
                table.AddColumn("狀態");

                foreach (var p in profiles)
                {
                    var isCurrent = current?.Id == p.Id;
                    var status = isCurrent ? "[green]← 目前[/]" : (p.IsDefault ? "[grey]預設[/]" : "");
                    var auth = p.AuthType == AuthenticationType.WindowsAuthentication ? "Windows" : p.Username ?? "SQL";

                    table.AddRow(
                        isCurrent ? $"[green]{p.Name.EscapeMarkup()}[/]" : p.Name.EscapeMarkup(),
                        p.Server.EscapeMarkup(),
                        p.Database.EscapeMarkup(),
                        auth,
                        status);
                }

                AnsiConsole.Write(table);
            }
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var nameArg = new Option<string?>("--name", "連線名稱");
        var serverOpt = new Option<string?>("--server", "伺服器位址");
        var databaseOpt = new Option<string?>("--database", "資料庫名稱");
        var userOpt = new Option<string?>("--user", "SQL 帳號");
        var passwordOpt = new Option<string?>("--password", "SQL 密碼");
        var defaultOpt = new Option<bool>("--default", "設為預設連線");

        var command = new Command("add", "新增連線");
        command.AddOption(nameArg);
        command.AddOption(serverOpt);
        command.AddOption(databaseOpt);
        command.AddOption(userOpt);
        command.AddOption(passwordOpt);
        command.AddOption(defaultOpt);

        command.SetHandler((name, server, database, user, password, isDefault) =>
        {
            // 互動模式：如果沒有提供 server，啟動互動精靈
            if (string.IsNullOrEmpty(server) && !CliOutput.JsonMode)
            {
                name = AnsiConsole.Ask<string>("連線名稱:");
                server = AnsiConsole.Ask<string>("伺服器位址:");
                database = AnsiConsole.Ask<string>("資料庫名稱:");
                var authType = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("驗證方式:")
                        .AddChoices("Windows 驗證", "SQL Server 驗證"));

                if (authType == "SQL Server 驗證")
                {
                    user = AnsiConsole.Ask<string>("帳號:");
                    password = AnsiConsole.Prompt(new TextPrompt<string>("密碼:").Secret());
                }

                isDefault = AnsiConsole.Confirm("設為預設連線?", false);
            }

            if (string.IsNullOrEmpty(server))
            {
                CliOutput.Error("必須提供伺服器位址（--server）");
                Environment.ExitCode = 2;
                return;
            }

            var profile = new ConnectionProfile
            {
                Name = name ?? $"{server}/{database ?? "master"}",
                Server = server,
                Database = database ?? "master",
                AuthType = string.IsNullOrEmpty(user)
                    ? AuthenticationType.WindowsAuthentication
                    : AuthenticationType.SqlServerAuthentication,
                Username = user,
                Password = password,
                IsDefault = isDefault
            };

            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            cm.AddProfile(profile);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new { profile.Id, profile.Name, Message = "連線已新增" });
            }
            else
            {
                CliOutput.SuccessMessage($"已新增連線「{profile.Name}」");
            }
        }, nameArg, serverOpt, databaseOpt, userOpt, passwordOpt, defaultOpt);

        return command;
    }

    private static Command CreateSwitchCommand()
    {
        var nameArg = new Argument<string?>("name", () => null, "連線名稱");
        var command = new Command("switch", "切換目前連線") { nameArg };

        command.SetHandler((name) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profiles = cm.GetAllProfiles();

            if (string.IsNullOrEmpty(name) && !CliOutput.JsonMode)
            {
                // 互動模式：選單
                if (profiles.Count == 0)
                {
                    CliOutput.Info("尚未設定任何連線。");
                    return;
                }

                var current = cm.GetCurrentProfile();
                name = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("選擇連線:")
                        .AddChoices(profiles.Select(p =>
                            current?.Id == p.Id ? $"{p.Name} ← 目前" : p.Name)));

                // 移除 " ← 目前" 後綴
                name = name.Replace(" ← 目前", "");
            }

            if (string.IsNullOrEmpty(name))
            {
                CliOutput.Error("必須提供連線名稱");
                Environment.ExitCode = 2;
                return;
            }

            var profile = profiles.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                CliOutput.Error($"找不到連線「{name}」");
                Environment.ExitCode = 1;
                return;
            }

            cm.SetCurrentProfile(profile.Id);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new { profile.Id, profile.Name, Message = "已切換連線" });
            }
            else
            {
                CliOutput.SuccessMessage($"已切換至「{profile.Name}」({profile.Server}/{profile.Database})");
            }
        }, nameArg);

        return command;
    }

    private static Command CreateTestCommand()
    {
        var nameArg = new Argument<string?>("name", () => null, "連線名稱（不指定則測試目前連線）");
        var command = new Command("test", "測試連線") { nameArg };

        command.SetHandler(async (name) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            ConnectionProfile? profile;

            if (!string.IsNullOrEmpty(name))
            {
                profile = cm.GetAllProfiles()
                    .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    CliOutput.Error($"找不到連線「{name}」");
                    Environment.ExitCode = 1;
                    return;
                }
            }
            else
            {
                // 使用 ConnectionResolver 解析（支援 CLI 參數、環境變數等）
                var resolver = new ConnectionResolver(cm);
                profile = resolver.Resolve(Program.CurrentOptions);
                if (profile == null)
                {
                    CliOutput.Error("未設定連線。使用 specurai conn add 新增連線，或用 --server 指定。");
                    Environment.ExitCode = 3;
                    return;
                }
            }

            if (!CliOutput.JsonMode)
            {
                CliOutput.Info($"正在測試連線 {profile.Server}/{profile.Database}...");
            }

            var success = await cm.TestConnectionAsync(profile);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new
                {
                    profile.Name,
                    profile.Server,
                    profile.Database,
                    Connected = success
                });
            }
            else
            {
                if (success)
                    CliOutput.SuccessMessage($"連線成功：{profile.Server}/{profile.Database}");
                else
                    CliOutput.Error($"連線失敗：{profile.Server}/{profile.Database}");
            }

            if (!success) Environment.ExitCode = 1;
        }, nameArg);

        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var nameArg = new Argument<string>("name", "連線名稱");
        var command = new Command("remove", "刪除連線") { nameArg };

        command.SetHandler((name) =>
        {
            var cm = Program.Services.GetRequiredService<IConnectionManager>();
            var profile = cm.GetAllProfiles()
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                CliOutput.Error($"找不到連線「{name}」");
                Environment.ExitCode = 1;
                return;
            }

            // 非 JSON 模式下確認
            if (!CliOutput.JsonMode)
            {
                if (!AnsiConsole.Confirm($"確定要刪除連線「{profile.Name}」?", false))
                    return;
            }

            cm.DeleteProfile(profile.Id);

            if (CliOutput.JsonMode)
            {
                CliOutput.Success(new { profile.Id, profile.Name, Message = "連線已刪除" });
            }
            else
            {
                CliOutput.SuccessMessage($"已刪除連線「{profile.Name}」");
            }
        }, nameArg);

        return command;
    }

    private static Command CreateImportCommand()
    {
        var stdinOpt = new Option<bool>("--stdin", "從 stdin 讀取 JSON 連線（相容 mpe show --json 格式）");
        var pathArg = new Argument<string?>("path", () => null, "JSON 檔案路徑");
        var command = new Command("import", "匯入連線") { pathArg };
        command.AddOption(stdinOpt);

        command.SetHandler((useStdin, path) =>
        {
            string? json = null;

            if (useStdin && Console.IsInputRedirected)
            {
                // 若 --conn-stdin 已在 middleware 消費 stdin，從已解析的結果取用
                if (Program.CurrentOptions.ConnStdin && Program.StdinProfiles.Count > 0)
                {
                    json = null; // stdin 已被消費，改用已解析的 profiles
                    var cm2 = Program.Services.GetRequiredService<IConnectionManager>();
                    var existingProfiles2 = cm2.GetAllProfiles();
                    var imported2 = 0;
                    var updated2 = 0;
                    foreach (var profile in Program.StdinProfiles)
                    {
                        var existing = existingProfiles2
                            .FirstOrDefault(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.Server = profile.Server;
                            existing.Database = profile.Database;
                            existing.AuthType = profile.AuthType;
                            existing.Username = profile.Username;
                            existing.Password = profile.Password;
                            cm2.UpdateProfile(existing);
                            updated2++;
                        }
                        else
                        {
                            cm2.AddProfile(profile);
                            imported2++;
                        }
                    }
                    if (CliOutput.JsonMode)
                        CliOutput.Success(new { Imported = imported2, Updated = updated2, Total = Program.StdinProfiles.Count });
                    else
                    {
                        if (imported2 > 0) CliOutput.SuccessMessage($"已匯入 {imported2} 個連線");
                        if (updated2 > 0) CliOutput.SuccessMessage($"已更新 {updated2} 個連線");
                    }
                    return;
                }
                json = Console.In.ReadToEnd();
            }
            else if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                json = File.ReadAllText(path);
            }
            else
            {
                CliOutput.Error("必須提供 --stdin 或檔案路徑");
                Environment.ExitCode = 2;
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                CliOutput.Error("輸入為空");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var profiles = ParseImportJson(json);
                if (profiles.Count == 0)
                {
                    CliOutput.Error("無法解析任何連線設定");
                    Environment.ExitCode = 1;
                    return;
                }

                var cm = Program.Services.GetRequiredService<IConnectionManager>();
                var existingProfiles = cm.GetAllProfiles();

                var imported = 0;
                var updated = 0;
                foreach (var profile in profiles)
                {
                    // 用名稱比對是否已存在
                    var existing = existingProfiles
                        .FirstOrDefault(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        // 更新現有連線
                        existing.Server = profile.Server;
                        existing.Database = profile.Database;
                        existing.AuthType = profile.AuthType;
                        existing.Username = profile.Username;
                        existing.Password = profile.Password;
                        cm.UpdateProfile(existing);
                        updated++;
                    }
                    else
                    {
                        cm.AddProfile(profile);
                        imported++;
                    }
                }

                if (CliOutput.JsonMode)
                {
                    CliOutput.Success(new { Imported = imported, Updated = updated, Total = profiles.Count });
                }
                else
                {
                    if (imported > 0) CliOutput.SuccessMessage($"已匯入 {imported} 個連線");
                    if (updated > 0) CliOutput.SuccessMessage($"已更新 {updated} 個連線");
                }
            }
            catch (JsonException ex)
            {
                CliOutput.Error($"JSON 解析失敗：{ex.Message}");
                Environment.ExitCode = 1;
            }
        }, stdinOpt, pathArg);

        return command;
    }

    /// <summary>
    /// 將非 null 的新值套用到既有連線設定（鏡像 MCP UpdateConnection 行為）。
    /// </summary>
    internal static ConnectionProfile ApplyProfileUpdates(
        ConnectionProfile profile,
        string? newName = null,
        string? newServer = null,
        string? newDatabase = null,
        string? newAuthType = null,
        string? newUsername = null,
        string? newPassword = null)
    {
        if (newName != null) profile.Name = newName;
        if (newServer != null) profile.Server = newServer;
        if (newDatabase != null) profile.Database = newDatabase;
        if (newAuthType != null)
            profile.AuthType = newAuthType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? AuthenticationType.SqlServerAuthentication
                : AuthenticationType.WindowsAuthentication;
        if (newUsername != null) profile.Username = newUsername;
        if (newPassword != null) profile.Password = newPassword;
        return profile;
    }

    /// <summary>
    /// 判斷是否至少提供一個要更新的欄位（皆為 null 代表未指定任何變更）。
    /// </summary>
    internal static bool HasProfileUpdate(
        string? newName,
        string? newServer,
        string? newDatabase,
        string? newAuthType,
        string? newUsername,
        string? newPassword)
        => newName != null || newServer != null || newDatabase != null
            || newAuthType != null || newUsername != null || newPassword != null;

    /// <summary>
    /// 將連線設定透過匯出服務序列化並寫入檔案，回傳匯出筆數（鏡像 MCP ExportConnections 行為）。
    /// </summary>
    internal static int ExportProfilesToFile(
        IConnectionExportService exportService,
        IReadOnlyList<ConnectionProfile> profiles,
        string outputPath,
        bool includePasswords)
    {
        var data = exportService.ExportToJson(profiles, includePasswords);
        File.WriteAllBytes(outputPath, data);
        return profiles.Count;
    }

    /// <summary>
    /// 解析匯入的 JSON，支援 mpe show --json 格式和簡易格式
    /// </summary>
    internal static List<ConnectionProfile> ParseImportJson(string json)
        => ConnectionProfileParser.ParseMultiple(json);

    internal static ConnectionProfile? ParseSingleProfile(JsonElement element)
        => ConnectionProfileParser.ParseSingle(element);
}
