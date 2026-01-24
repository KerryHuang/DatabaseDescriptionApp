namespace TableSpec.Domain.Entities;

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
}
