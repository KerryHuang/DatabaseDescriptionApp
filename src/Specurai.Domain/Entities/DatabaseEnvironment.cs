namespace Specurai.Domain.Entities;

/// <summary>
/// 資料庫連線所屬環境
/// </summary>
public enum DatabaseEnvironment
{
    /// <summary>開發環境</summary>
    Development,

    /// <summary>測試環境</summary>
    Testing,

    /// <summary>預備環境</summary>
    Staging,

    /// <summary>正式環境</summary>
    Production
}
