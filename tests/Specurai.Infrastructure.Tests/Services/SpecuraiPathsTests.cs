using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// SpecuraiPaths 測試：覆蓋跨平台路徑解析與 legacy 遷移邏輯。
/// 透過 internal 純函式 overload 讓測試不依賴實際 OS 行為（避免 .NET 8 ApplicationData 在 macOS 的 runtime 差異）。
/// </summary>
public class SpecuraiPathsTests
{
    #region GetAppDataRootCore

    [Fact]
    public void GetAppDataRootCore_Windows_應使用_Environment_ApplicationData()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: true,
            isMacOS: false,
            homeDir: @"C:\Users\U",
            xdgConfigHome: null,
            windowsAppData: @"C:\Users\U\AppData\Roaming");

        result.Should().Be(@"C:\Users\U\AppData\Roaming");
    }

    [Fact]
    public void GetAppDataRootCore_macOS_應回傳_Library_Application_Support()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: false,
            isMacOS: true,
            homeDir: "/Users/zihao",
            xdgConfigHome: null,
            windowsAppData: string.Empty);

        result.Should().Be(Path.Combine("/Users/zihao", "Library", "Application Support"));
    }

    [Fact]
    public void GetAppDataRootCore_macOS_即使有_XDG_CONFIG_HOME_也忽略()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: false,
            isMacOS: true,
            homeDir: "/Users/zihao",
            xdgConfigHome: "/custom/xdg",
            windowsAppData: string.Empty);

        result.Should().Be(Path.Combine("/Users/zihao", "Library", "Application Support"));
    }

    [Fact]
    public void GetAppDataRootCore_Linux_無_XDG_應回退到_HomeDir_config()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: false,
            isMacOS: false,
            homeDir: "/home/u",
            xdgConfigHome: null,
            windowsAppData: string.Empty);

        result.Should().Be(Path.Combine("/home/u", ".config"));
    }

    [Fact]
    public void GetAppDataRootCore_Linux_有_XDG_應優先使用_XDG_CONFIG_HOME()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: false,
            isMacOS: false,
            homeDir: "/home/u",
            xdgConfigHome: "/custom/xdg",
            windowsAppData: string.Empty);

        result.Should().Be("/custom/xdg");
    }

    [Fact]
    public void GetAppDataRootCore_Linux_XDG_空字串_應回退到_HomeDir_config()
    {
        var result = SpecuraiPaths.GetAppDataRootCore(
            isWindows: false,
            isMacOS: false,
            homeDir: "/home/u",
            xdgConfigHome: string.Empty,
            windowsAppData: string.Empty);

        result.Should().Be(Path.Combine("/home/u", ".config"));
    }

    #endregion

    #region MigrateLegacyFile

    [Fact]
    public void MigrateLegacyFile_新路徑已存在_不應觸發搬移()
    {
        var moved = false;
        var result = SpecuraiPaths.MigrateLegacyFile(
            newPath: "/new/file.json",
            legacyPath: "/legacy/file.json",
            fileExists: p => p == "/new/file.json",
            fileMove: (_, _) => moved = true);

        result.Should().Be("/new/file.json");
        moved.Should().BeFalse();
    }

    [Fact]
    public void MigrateLegacyFile_兩者皆不存在_應回傳新路徑且不搬移()
    {
        var moved = false;
        var result = SpecuraiPaths.MigrateLegacyFile(
            newPath: "/new/file.json",
            legacyPath: "/legacy/file.json",
            fileExists: _ => false,
            fileMove: (_, _) => moved = true);

        result.Should().Be("/new/file.json");
        moved.Should().BeFalse();
    }

    [Fact]
    public void MigrateLegacyFile_僅_legacy_存在_應搬移至新路徑()
    {
        string? from = null;
        string? to = null;
        var result = SpecuraiPaths.MigrateLegacyFile(
            newPath: "/new/file.json",
            legacyPath: "/legacy/file.json",
            fileExists: p => p == "/legacy/file.json",
            fileMove: (f, t) => { from = f; to = t; });

        result.Should().Be("/new/file.json");
        from.Should().Be("/legacy/file.json");
        to.Should().Be("/new/file.json");
    }

    [Fact]
    public void MigrateLegacyFile_搬移失敗_應回傳_legacy_路徑避免資料遺失()
    {
        var result = SpecuraiPaths.MigrateLegacyFile(
            newPath: "/new/file.json",
            legacyPath: "/legacy/file.json",
            fileExists: p => p == "/legacy/file.json",
            fileMove: (_, _) => throw new IOException("磁碟已滿"));

        result.Should().Be("/legacy/file.json");
    }

    [Fact]
    public void MigrateLegacyFile_搬移失敗但其他_process_已搬好新路徑_應回傳新路徑()
    {
        // 模擬 Desktop 與 MCP 並行啟動的 race：
        // - 第一次檢查 newPath：不存在（進入 migration）
        // - 檢查 legacyPath：存在（準備搬移）
        // - fileMove 拋例外（因為其他 process 已完成）
        // - catch 內再次檢查 newPath：已存在（被其他 process 建立）
        var newExistsCheckCount = 0;
        var result = SpecuraiPaths.MigrateLegacyFile(
            newPath: "/new/file.json",
            legacyPath: "/legacy/file.json",
            fileExists: p =>
            {
                if (p == "/new/file.json")
                {
                    newExistsCheckCount++;
                    return newExistsCheckCount > 1;
                }
                return true;
            },
            fileMove: (_, _) => throw new IOException("競爭搬移"));

        result.Should().Be("/new/file.json");
    }

    #endregion

    #region GetAppDataRoot（實機煙霧測試）

    [Fact]
    public void GetAppDataRoot_應回傳非空絕對路徑()
    {
        var result = SpecuraiPaths.GetAppDataRoot();

        result.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(result).Should().BeTrue();
    }

    [Fact]
    public void GetSpecuraiDataDir_應以_Specurai_結尾()
    {
        var result = SpecuraiPaths.GetSpecuraiDataDir();

        result.Should().EndWith("Specurai");
    }

    #endregion
}
