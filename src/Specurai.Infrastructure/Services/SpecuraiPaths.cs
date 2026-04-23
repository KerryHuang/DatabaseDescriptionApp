namespace Specurai.Infrastructure.Services;

/// <summary>
/// Specurai 跨平台資料目錄解析。
/// </summary>
/// <remarks>
/// 不依賴 <see cref="Environment.SpecialFolder.ApplicationData"/> 的行為，改以手寫規則固定各平台路徑，
/// 避開 .NET 8 在 macOS 的 breaking change：<c>ApplicationData</c> 從 <c>~/.config</c>
/// 改為 <c>~/Library/Application Support</c>，跨 runtime 版本會產生不同路徑，導致 Desktop / MCP /
/// CLI 在同一台 macOS 上看到不同的 connections.json。
/// 參見 <see href="https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getfolderpath-unix"/>。
/// </remarks>
public static class SpecuraiPaths
{
    private const string AppName = "Specurai";

    /// <summary>
    /// 取得跨平台 App Data 根目錄。
    /// Windows 回傳 <c>%APPDATA%</c>；macOS 固定 <c>~/Library/Application Support</c>；
    /// Linux 優先 <c>$XDG_CONFIG_HOME</c>，否則 <c>~/.config</c>。
    /// </summary>
    public static string GetAppDataRoot() => GetAppDataRootCore(
        isWindows: OperatingSystem.IsWindows(),
        isMacOS: OperatingSystem.IsMacOS(),
        homeDir: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        xdgConfigHome: Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        windowsAppData: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// 取得 Specurai 資料目錄（App Data 根目錄下的 <c>Specurai</c> 子目錄）。
    /// </summary>
    public static string GetSpecuraiDataDir() =>
        Path.Combine(GetAppDataRoot(), AppName);

    /// <summary>
    /// 解析 Specurai 設定檔完整路徑，保證目錄存在。
    /// 若執行於 macOS 且新路徑不存在、但 legacy <c>~/.config/Specurai/&lt;fileName&gt;</c> 存在，會自動搬移至新路徑。
    /// </summary>
    /// <param name="fileName">檔案名稱，例如 <c>connections.json</c>。</param>
    /// <returns>實際應使用的檔案完整路徑。</returns>
    public static string ResolveConfigFile(string fileName)
    {
        var newDir = GetSpecuraiDataDir();
        Directory.CreateDirectory(newDir);
        var newPath = Path.Combine(newDir, fileName);

        if (!OperatingSystem.IsMacOS())
            return newPath;

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            AppName,
            fileName);

        return MigrateLegacyFile(newPath, legacyPath, File.Exists, File.Move);
    }

    /// <summary>
    /// 跨平台 App Data 根目錄的純函式版本，便於測試注入各分支輸入。
    /// </summary>
    internal static string GetAppDataRootCore(
        bool isWindows,
        bool isMacOS,
        string homeDir,
        string? xdgConfigHome,
        string windowsAppData)
    {
        if (isWindows)
            return windowsAppData;

        if (isMacOS)
            return Path.Combine(homeDir, "Library", "Application Support");

        return !string.IsNullOrEmpty(xdgConfigHome)
            ? xdgConfigHome
            : Path.Combine(homeDir, ".config");
    }

    /// <summary>
    /// Legacy 檔案遷移的純函式版本。IO 動作透過 delegate 注入，便於測試。
    /// </summary>
    /// <remarks>
    /// 規則：
    /// <list type="bullet">
    ///   <item>新路徑已存在：不搬、不刪 legacy，回傳新路徑。此策略保守處理「雙方都存在」的情境
    ///     （例如 .NET 8 process 已在新路徑寫入、或使用者手動建檔），避免覆蓋新寫入的資料。</item>
    ///   <item>新路徑不存在、legacy 不存在：回傳新路徑（呼叫端自行建立）。</item>
    ///   <item>新路徑不存在、legacy 存在：搬移 legacy → new。</item>
    ///   <item>搬移失敗：優先判斷是否為 race（其他 process 搶先完成）— 若新路徑此時已存在則視為
    ///     race 成功，回傳新路徑；否則 fallback legacy 路徑以免資料遺失。</item>
    /// </list>
    /// </remarks>
    internal static string MigrateLegacyFile(
        string newPath,
        string legacyPath,
        Func<string, bool> fileExists,
        Action<string, string> fileMove)
    {
        if (fileExists(newPath))
            return newPath;

        if (!fileExists(legacyPath))
            return newPath;

        try
        {
            fileMove(legacyPath, newPath);
            return newPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fileExists(newPath) ? newPath : legacyPath;
        }
    }
}
