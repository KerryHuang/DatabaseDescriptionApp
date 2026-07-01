namespace Specurai.Domain;

/// <summary>
/// 伺服器端路徑處理輔助方法（跨 Windows／Linux）
/// </summary>
public static class ServerPathHelper
{
    /// <summary>依父路徑判定分隔字元後組合子路徑。</summary>
    public static string Combine(string parent, string name)
    {
        var sep = GetSeparator(parent);
        var trimmed = parent.TrimEnd('\\', '/');
        return $"{trimmed}{sep}{name}";
    }

    /// <summary>判定路徑所屬平台的分隔字元（Windows 路徑用 '\\'，否則 '/'）。</summary>
    public static char GetSeparator(string path)
    {
        if (path.Contains('\\')) return '\\';
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') return '\\';
        return '/';
    }

    /// <summary>取路徑最後一段（檔名）。</summary>
    public static string GetFileName(string path)
    {
        var sep = GetSeparator(path);
        var idx = path.LastIndexOf(sep);
        return idx < 0 ? path : path[(idx + 1)..];
    }

    /// <summary>判斷檔名是否為備份檔（.bak 或 .trn）。</summary>
    public static bool IsBackupFile(string name) =>
        name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".trn", StringComparison.OrdinalIgnoreCase);
}
