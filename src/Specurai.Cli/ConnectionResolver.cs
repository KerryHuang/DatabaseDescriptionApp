using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Cli;

/// <summary>
/// 連線解析器，支援多種連線來源
/// 優先順序：CLI 參數 > stdin > 環境變數 > profile 名稱 > connections.json 預設
/// </summary>
public class ConnectionResolver
{
    private readonly IConnectionManager _connectionManager;

    public ConnectionResolver(IConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// 解析連線設定
    /// </summary>
    public ConnectionProfile? Resolve(GlobalOptions options)
    {
        // 1. 完整連線字串
        if (!string.IsNullOrEmpty(options.ConnectionString))
            return FromConnectionString(options.ConnectionString);

        // 2. CLI 參數
        if (!string.IsNullOrEmpty(options.Server))
            return FromCliArgs(options);

        // 3. stdin JSON（已在 middleware 讀取並註冊為臨時 profile）
        if (options.ConnStdin && Program.StdinProfiles.Count > 0)
            return Program.StdinProfiles[0];

        // 4. 環境變數
        var envProfile = FromEnvironment();
        if (envProfile != null)
            return envProfile;

        // 5. 指定的 profile 名稱
        if (!string.IsNullOrEmpty(options.Profile))
            return FromProfileName(options.Profile);

        // 6. connections.json 預設
        return _connectionManager.GetCurrentProfile();
    }

    /// <summary>
    /// 從連線字串建立 profile
    /// </summary>
    internal static ConnectionProfile FromConnectionString(string connectionString)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        return new ConnectionProfile
        {
            Name = $"{builder.DataSource}/{builder.InitialCatalog}",
            Server = builder.DataSource,
            Database = builder.InitialCatalog,
            AuthType = builder.IntegratedSecurity
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = builder.IntegratedSecurity ? null : builder.UserID,
            Password = builder.IntegratedSecurity ? null : builder.Password
        };
    }

    /// <summary>
    /// 從 CLI 參數建立 profile
    /// </summary>
    internal static ConnectionProfile FromCliArgs(GlobalOptions options)
    {
        var server = options.Server!;
        if (options.Port.HasValue && options.Port.Value != 1433)
            server = $"{server},{options.Port.Value}";

        return new ConnectionProfile
        {
            Name = $"{server}/{options.Database}",
            Server = server,
            Database = options.Database ?? "master",
            AuthType = string.IsNullOrEmpty(options.User)
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = options.User,
            Password = options.Password
        };
    }

    /// <summary>
    /// 從環境變數建立 profile
    /// </summary>
    internal static ConnectionProfile? FromEnvironment()
    {
        // 先檢查完整連線字串
        var connStr = Environment.GetEnvironmentVariable("SPECURAI_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connStr))
            return FromConnectionString(connStr);

        // 再檢查個別欄位
        var server = Environment.GetEnvironmentVariable("SPECURAI_SERVER");
        if (string.IsNullOrEmpty(server))
            return null;

        var portStr = Environment.GetEnvironmentVariable("SPECURAI_PORT");
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var port) && port != 1433)
            server = $"{server},{port}";

        var database = Environment.GetEnvironmentVariable("SPECURAI_DATABASE") ?? "master";
        var user = Environment.GetEnvironmentVariable("SPECURAI_USER");
        var password = Environment.GetEnvironmentVariable("SPECURAI_PASSWORD");

        return new ConnectionProfile
        {
            Name = $"{server}/{database}",
            Server = server,
            Database = database,
            AuthType = string.IsNullOrEmpty(user)
                ? AuthenticationType.WindowsAuthentication
                : AuthenticationType.SqlServerAuthentication,
            Username = user,
            Password = password
        };
    }

    /// <summary>
    /// 從已儲存的 profile 名稱查找（只找啟用的連線）
    /// </summary>
    private ConnectionProfile? FromProfileName(string name)
    {
        return _connectionManager.GetEnabledProfiles()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 產生「找不到連線」的錯誤訊息；若該連線存在但已停用，回傳更明確的說明。
    /// </summary>
    public static string DescribeMissing(IConnectionManager connectionManager, string name)
    {
        var disabled = connectionManager.GetAllProfiles()
            .FirstOrDefault(p =>
                !p.IsEnabled &&
                (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                 p.Id.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)));

        return disabled != null
            ? $"連線「{disabled.Name}」已停用，請先在連線設定中啟用。"
            : $"找不到連線「{name}」";
    }
}

/// <summary>
/// 全域選項
/// </summary>
public class GlobalOptions
{
    public string? Server { get; set; }
    public int? Port { get; set; }
    public string? Database { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? ConnectionString { get; set; }
    public bool ConnStdin { get; set; }
    public string? Profile { get; set; }
    public bool Json { get; set; }
}
