using System;
using System.IO;
using System.Text.Json;

namespace Specurai.Desktop.Services;

/// <summary>
/// 使用者偏好設定，儲存於 AppData/Specurai/preferences.json
/// </summary>
public static class UserPreferences
{
    private static readonly string ConfigPath = GetConfigPath();

    private static PreferencesData? _cache;

    public static bool IsSidebarOpen
    {
        get => Load().IsSidebarOpen;
        set
        {
            var data = Load();
            data.IsSidebarOpen = value;
            Save(data);
        }
    }

    private static PreferencesData Load()
    {
        if (_cache != null) return _cache;

        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                _cache = JsonSerializer.Deserialize<PreferencesData>(json) ?? new PreferencesData();
            }
            catch
            {
                _cache = new PreferencesData();
            }
        }
        else
        {
            _cache = new PreferencesData();
        }

        return _cache;
    }

    private static void Save(PreferencesData data)
    {
        _cache = data;
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 忽略儲存錯誤
        }
    }

    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "Specurai");
        return Path.Combine(configDir, "preferences.json");
    }

    private class PreferencesData
    {
        public bool IsSidebarOpen { get; set; } = true;
    }
}
