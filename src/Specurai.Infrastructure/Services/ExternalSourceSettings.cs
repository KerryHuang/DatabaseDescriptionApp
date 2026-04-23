using System.Text.Json;
using Specurai.Application.Services;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 外部連線來源設定服務實作
/// </summary>
public class ExternalSourceSettings : IExternalSourceSettings
{
    private readonly string _configPath;

    private static readonly JsonSerializerOptions WriteOptions =
        new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 建立實例，使用 Specurai 標準資料目錄（與桌面 App 共用；macOS 上若有歷史殘留檔會自動回收）
    /// </summary>
    public ExternalSourceSettings()
    {
        _configPath = SpecuraiPaths.ResolveConfigFile("external-source.json");
    }

    /// <summary>
    /// 建立實例，使用指定的組態目錄（用於測試）
    /// </summary>
    /// <param name="configDir">組態目錄路徑</param>
    public ExternalSourceSettings(string configDir)
    {
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "external-source.json");
    }

    /// <summary>
    /// 載入外部連線來源設定
    /// </summary>
    /// <returns>外部連線來源設定，若檔案不存在或解析失敗則回傳空字串設定</returns>
    public ExternalSourceConfig Load()
    {
        if (!File.Exists(_configPath))
            return new ExternalSourceConfig(string.Empty, string.Empty);

        try
        {
            var json = File.ReadAllText(_configPath);
            var data = JsonSerializer.Deserialize<ConfigData>(json, ReadOptions);
            return new ExternalSourceConfig(
                data?.SourceDirectory ?? string.Empty,
                data?.KeyFilePath ?? string.Empty);
        }
        catch
        {
            return new ExternalSourceConfig(string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// 儲存外部連線來源設定
    /// </summary>
    /// <param name="config">要儲存的設定</param>
    public void Save(ExternalSourceConfig config)
    {
        try
        {
            var data = new ConfigData
            {
                SourceDirectory = config.SourceDirectory,
                KeyFilePath = config.KeyFilePath
            };
            var json = JsonSerializer.Serialize(data, WriteOptions);
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // 儲存失敗靜默略過，不中斷 UI 操作
        }
    }

    /// <summary>
    /// JSON 反序列化用的資料類別
    /// </summary>
    private class ConfigData
    {
        public string? SourceDirectory { get; set; }
        public string? KeyFilePath { get; set; }
    }
}
