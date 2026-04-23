namespace Specurai.Infrastructure.Services;

/// <summary>
/// Specurai 跨平台資料目錄解析。
/// </summary>
/// <remarks>
/// <para>
/// 以手寫規則固定各平台路徑，確保 Desktop / MCP / CLI 三者在同一台機器上讀寫同一個資料夾
/// （macOS 上為 <c>~/Library/Application Support/Specurai</c>）。不使用
/// <see cref="Environment.SpecialFolder.ApplicationData"/>，因為它在 macOS 上會隨 .NET runtime
/// 版本給出不同結果（.NET 7 回 <c>~/.config</c>、.NET 8 回 <c>~/Library/Application Support</c>），
/// 在使用者 Desktop 跑新 runtime 但 MCP/CLI 跑舊 runtime 時會造成檔案分裂。
/// 參見 <see href="https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getfolderpath-unix"/>。
/// </para>
/// <para>
/// macOS 使用者若歷史上 MCP/CLI 曾在錯誤路徑（<c>~/.config/Specurai</c>）寫入檔案，
/// <see cref="ResolveConfigFile"/> 會把遺留檔案搬回正確位置，作為資料不遺失的安全網。
/// </para>
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
    /// macOS 上若標準路徑不存在、但歷史上曾在 <c>~/.config/Specurai/&lt;fileName&gt;</c> 留下殘留檔
    /// （例如早期 MCP/CLI 跑在 .NET 7 時的錯誤寫入位置），會把殘留檔搬回標準路徑作為資料不遺失的安全網。
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
    /// 殘留檔回收的純函式版本。IO 動作透過 delegate 注入，便於測試。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 標準路徑是 Desktop 讀寫的位置；<c>legacyPath</c> 泛指歷史上曾被錯誤寫入的其他位置
    /// （macOS 上為 <c>~/.config/Specurai</c>），現已不再使用，但既有檔案需搬回標準路徑。
    /// </para>
    /// 規則：
    /// <list type="bullet">
    ///   <item>標準路徑已存在：不搬、不刪殘留檔，回傳標準路徑。避免覆蓋 Desktop 或其他 process
    ///     剛寫入的資料；殘留檔留在原位由使用者自行處置。</item>
    ///   <item>標準路徑不存在、殘留檔也不存在：回傳標準路徑（呼叫端自行建立）。</item>
    ///   <item>標準路徑不存在、殘留檔存在：把殘留檔搬回標準路徑。</item>
    ///   <item>搬移失敗：優先判斷是否為 race（其他 process 搶先完成）— 若標準路徑此時已存在則視為
    ///     race 成功，回傳標準路徑；否則 fallback 殘留檔路徑以免資料遺失。</item>
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
