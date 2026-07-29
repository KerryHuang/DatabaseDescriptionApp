using System.Text.Json;
using Microsoft.Data.SqlClient;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 連線管理服務實作
/// </summary>
public class ConnectionManager : IConnectionManager
{
    private readonly string _configPath;
    private List<ConnectionProfile> _profiles = [];
    private List<ConnectionProfile> _temporaryProfiles = [];
    private Guid? _currentProfileId;
    private string? _currentDatabaseOverride;

    public event EventHandler<ConnectionProfile?>? CurrentProfileChanged;
    public event EventHandler<string?>? CurrentDatabaseChanged;

    public ConnectionManager() : this(GetConfigPath())
    {
    }

    /// <summary>
    /// 指定設定檔路徑的建構函式（測試用）
    /// </summary>
    public ConnectionManager(string configPath)
    {
        _configPath = configPath;
        LoadProfiles();
    }

    public IReadOnlyList<ConnectionProfile> GetAllProfiles()
        => _temporaryProfiles.Concat(_profiles)
            .OrderBy(p => p, ConnectionProfileComparer.Instance)
            .ToList().AsReadOnly();

    public IReadOnlyList<ConnectionProfile> GetEnabledProfiles()
        => GetAllProfiles().Where(p => p.IsEnabled).ToList().AsReadOnly();

    public ConnectionProfile? GetCurrentProfile()
    {
        if (_currentProfileId == null)
        {
            var defaultProfile = _profiles.FirstOrDefault(p => p.IsDefault && p.IsEnabled);
            if (defaultProfile != null)
            {
                _currentProfileId = defaultProfile.Id;
            }
        }

        return _profiles.FirstOrDefault(p => p.Id == _currentProfileId);
    }

    public void SetCurrentProfile(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId && p.IsEnabled);
        if (profile != null)
        {
            _currentProfileId = profileId;
            // 切換連線設定檔時重設資料庫覆寫，回到新設定檔的預設資料庫
            _currentDatabaseOverride = null;
            CurrentProfileChanged?.Invoke(this, profile);
        }
    }

    public void AddProfile(ConnectionProfile profile)
    {
        if (profile.IsDefault)
        {
            foreach (var p in _profiles)
            {
                p.IsDefault = false;
            }
        }

        _profiles.Add(profile);
        SaveProfiles();

        if (_currentProfileId == null || profile.IsDefault)
        {
            SetCurrentProfile(profile.Id);
        }
    }

    public void UpdateProfile(ConnectionProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0)
            return;

        if (profile.IsDefault)
        {
            foreach (var p in _profiles)
            {
                p.IsDefault = false;
            }
        }

        // 停用的連線不該保留預設身分，否則會留下一個永遠選不到的預設連線
        if (!profile.IsEnabled)
        {
            profile.IsDefault = false;
        }

        _profiles[index] = profile;
        SaveProfiles();

        // 停用目前連線時自動切離至第一個啟用的連線，沒有就變成無連線
        if (!profile.IsEnabled && _currentProfileId == profile.Id)
        {
            var fallback = _profiles.FirstOrDefault(p => p.IsEnabled);
            _currentProfileId = fallback?.Id;
            _currentDatabaseOverride = null;
            CurrentProfileChanged?.Invoke(this, fallback);
            return;
        }

        if (_currentProfileId == profile.Id)
        {
            CurrentProfileChanged?.Invoke(this, profile);
        }
    }

    public void DeleteProfile(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
        {
            _profiles.Remove(profile);
            SaveProfiles();

            if (_currentProfileId == profileId)
            {
                _currentProfileId = null;
                var newCurrent = GetCurrentProfile();
                CurrentProfileChanged?.Invoke(this, newCurrent);
            }
        }
    }

    public async Task<bool> TestConnectionAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        try
        {
            var connectionString = BuildConnectionString(profile);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string BuildConnectionString(ConnectionProfile profile)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };

        if (profile.AuthType == AuthenticationType.WindowsAuthentication)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = profile.Username;
            builder.Password = profile.Password;
        }

        return builder.ConnectionString;
    }

    public string? GetCurrentConnectionString()
    {
        var profile = GetCurrentProfile();
        if (profile == null)
            return null;

        var connectionString = BuildConnectionString(profile);
        if (_currentDatabaseOverride == null)
            return connectionString;

        // 目前資料庫覆寫僅影響「目前連線」，不影響 BuildConnectionString / GetConnectionString(profileId)
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = _currentDatabaseOverride
        };
        return builder.ConnectionString;
    }

    public string? GetCurrentDatabase()
        => _currentDatabaseOverride ?? GetCurrentProfile()?.Database;

    public void SetCurrentDatabase(string? databaseName)
    {
        var before = GetCurrentDatabase();
        _currentDatabaseOverride = databaseName;
        var after = GetCurrentDatabase();

        // 生效資料庫沒變就不觸發事件，避免訂閱端重複載入
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            CurrentDatabaseChanged?.Invoke(this, after);
        }
    }

    public Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var profile = GetCurrentProfile();
        if (profile == null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return GetDatabasesAsync(profile, ct);
    }

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(BuildConnectionString(profile));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 ORDER BY name";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public string? GetConnectionString(Guid profileId)
    {
        var temporary = _temporaryProfiles.FirstOrDefault(p => p.Id == profileId);
        if (temporary != null)
            return BuildConnectionString(temporary);

        var profile = _profiles.FirstOrDefault(p => p.Id == profileId && p.IsEnabled);
        return profile != null ? BuildConnectionString(profile) : null;
    }

    public string GetProfileName(Guid profileId)
    {
        var profile = _temporaryProfiles.Concat(_profiles).FirstOrDefault(p => p.Id == profileId);
        return profile?.Name ?? profileId.ToString();
    }

    public void RegisterTemporaryProfiles(IReadOnlyList<ConnectionProfile> profiles)
    {
        _temporaryProfiles = [..profiles];
    }

    private static string GetConfigPath() =>
        SpecuraiPaths.ResolveConfigFile("connections.json");

    private void LoadProfiles()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var data = JsonSerializer.Deserialize<ConnectionData>(json);
                if (data != null)
                {
                    _profiles = data.Profiles ?? [];
                    _currentProfileId = data.CurrentProfileId;
                }
            }
            catch
            {
                _profiles = [];
            }
        }
    }

    private void SaveProfiles()
    {
        try
        {
            var data = new ConnectionData
            {
                Profiles = _profiles,
                CurrentProfileId = _currentProfileId
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // Log error
        }
    }

    private class ConnectionData
    {
        public List<ConnectionProfile>? Profiles { get; set; }
        public Guid? CurrentProfileId { get; set; }
    }
}
