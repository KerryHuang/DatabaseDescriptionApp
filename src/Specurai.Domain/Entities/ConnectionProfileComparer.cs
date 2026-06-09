namespace Specurai.Domain.Entities;

/// <summary>
/// 連線設定檔顯示排序：預設連線優先 → 環境（列舉順序）→ 名稱（不分大小寫）。
/// </summary>
public sealed class ConnectionProfileComparer : IComparer<ConnectionProfile>
{
    /// <summary>共用單例。</summary>
    public static readonly ConnectionProfileComparer Instance = new();

    public int Compare(ConnectionProfile? x, ConnectionProfile? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 預設連線優先（IsDefault = true 排前面）
        var byDefault = y.IsDefault.CompareTo(x.IsDefault);
        if (byDefault != 0) return byDefault;

        // 環境（列舉順序：Development=0 → Production=3）
        var byEnv = x.Environment.CompareTo(y.Environment);
        if (byEnv != 0) return byEnv;

        // 名稱（不分大小寫）
        return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
    }
}
