namespace Specurai.Domain.Entities;

/// <summary>
/// 驗證類型
/// </summary>
public enum AuthenticationType
{
    /// <summary>
    /// Windows 整合驗證
    /// </summary>
    WindowsAuthentication,

    /// <summary>
    /// SQL Server 帳號密碼驗證
    /// </summary>
    SqlServerAuthentication
}

/// <summary>
/// 資料庫連線設定檔
/// </summary>
public class ConnectionProfile
{
    /// <summary>
    /// 連線設定識別碼
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 連線名稱（顯示用）
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 伺服器位址
    /// </summary>
    public required string Server { get; set; }

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public required string Database { get; set; }

    /// <summary>
    /// 驗證類型
    /// </summary>
    public AuthenticationType AuthType { get; set; }

    /// <summary>
    /// SQL Server 帳號（SQL 驗證時使用）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SQL Server 密碼（SQL 驗證時使用）
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 是否為預設連線
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 連線所屬環境（預設預備環境）
    /// </summary>
    public DatabaseEnvironment Environment { get; set; } = DatabaseEnvironment.Staging;

    /// <summary>
    /// 是否啟用（停用的連線不會出現在各功能的連線選擇中）
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否來自外部（外部來源同步、CLI/MCP 匯入）；false 表示使用者自建
    /// </summary>
    public bool IsExternal { get; set; }

    /// <summary>
    /// 判斷兩筆連線是否指向同一個資料庫且使用同一組身分。
    /// 用於外部來源同步時排除與既有連線重複的項目；不比對密碼、名稱與環境。
    /// </summary>
    public bool HasSameConnectionSettings(ConnectionProfile other) =>
        AuthType == other.AuthType &&
        string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Database, other.Database, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Username ?? string.Empty, other.Username ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
}
