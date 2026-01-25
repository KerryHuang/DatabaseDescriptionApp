using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 相依套件資訊
/// </summary>
public record DependencyInfo(string Name, string Description);

/// <summary>
/// 關於 TableSpec 文件 ViewModel
/// </summary>
public partial class AboutDocumentViewModel : DocumentViewModel
{
    public override string DocumentType => "About";
    public override string DocumentKey => "About"; // 單一實例

    #region 應用程式資訊

    /// <summary>
    /// 應用程式名稱
    /// </summary>
    public string AppName => "TableSpec";

    /// <summary>
    /// 應用程式描述
    /// </summary>
    public string AppDescription => "資料庫規格查詢工具";

    /// <summary>
    /// 版本號
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 版權資訊
    /// </summary>
    public string Copyright => "© 2024-2026 KerryHuang";

    /// <summary>
    /// GitHub Repository URL
    /// </summary>
    public string GitHubUrl => "https://github.com/KerryHuang/DatabaseDescriptionApp";

    #endregion

    #region 系統資訊

    /// <summary>
    /// 作業系統版本
    /// </summary>
    public string OsVersion { get; }

    /// <summary>
    /// .NET Runtime 版本
    /// </summary>
    public string DotNetVersion { get; }

    /// <summary>
    /// 系統架構
    /// </summary>
    public string Architecture { get; }

    #endregion

    #region 相依套件

    /// <summary>
    /// 相依套件清單
    /// </summary>
    public IReadOnlyList<DependencyInfo> Dependencies { get; } =
    [
        new("Avalonia", "跨平台 UI 框架"),
        new("Semi.Avalonia", "UI 主題樣式"),
        new("CommunityToolkit.Mvvm", "MVVM 基礎設施"),
        new("Microsoft.Data.SqlClient", "SQL Server 連線"),
        new("Dapper", "輕量 ORM"),
        new("ClosedXML", "Excel 匯出"),
        new("LiveChartsCore", "圖表元件")
    ];

    #endregion

    #region 授權條款

    /// <summary>
    /// 授權條款文字
    /// </summary>
    public string LicenseText { get; }

    /// <summary>
    /// 是否顯示授權條款
    /// </summary>
    [ObservableProperty]
    private bool _showLicense;

    /// <summary>
    /// 授權條款按鈕文字
    /// </summary>
    public string LicenseButtonText => ShowLicense ? "隱藏授權條款" : "顯示授權條款";

    partial void OnShowLicenseChanged(bool value)
    {
        OnPropertyChanged(nameof(LicenseButtonText));
    }

    #endregion

    /// <summary>
    /// 建構函式
    /// </summary>
    public AboutDocumentViewModel()
    {
        Title = "關於";
        Icon = "ℹ️";
        CanClose = true;

        // 取得版本號
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        Version = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

        // 取得作業系統資訊
        OsVersion = GetOsDescription();

        // 取得 .NET 版本
        DotNetVersion = $".NET {Environment.Version}";

        // 取得系統架構
        Architecture = RuntimeInformation.ProcessArchitecture.ToString();

        // 讀取授權條款
        LicenseText = LoadLicenseText();
    }

    /// <summary>
    /// 取得作業系統描述
    /// </summary>
    private static string GetOsDescription()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"Windows {Environment.OSVersion.Version}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"macOS {Environment.OSVersion.Version}";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"Linux {Environment.OSVersion.Version}";
        }
        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// 載入授權條款文字
    /// </summary>
    private static string LoadLicenseText()
    {
        try
        {
            // 嘗試從應用程式目錄讀取 LICENSE.txt
            var appDir = AppContext.BaseDirectory;
            var licensePath = Path.Combine(appDir, "LICENSE.txt");

            if (File.Exists(licensePath))
            {
                return File.ReadAllText(licensePath);
            }

            // 嘗試從上層目錄讀取（開發環境）
            var devLicensePath = Path.Combine(appDir, "..", "..", "..", "..", "..", "LICENSE.txt");
            if (File.Exists(devLicensePath))
            {
                return File.ReadAllText(devLicensePath);
            }
        }
        catch
        {
            // 忽略讀取錯誤
        }

        // 預設授權條款
        return @"MIT License

Copyright (c) 2024-2026 KerryHuang

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
    }

    /// <summary>
    /// 開啟 GitHub Repository
    /// </summary>
    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略開啟瀏覽器錯誤
        }
    }

    /// <summary>
    /// 切換授權條款顯示
    /// </summary>
    [RelayCommand]
    private void ToggleLicense()
    {
        ShowLicense = !ShowLicense;
    }
}
