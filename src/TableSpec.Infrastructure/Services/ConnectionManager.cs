using System.Text.Json;
using Microsoft.Data.SqlClient;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// 連線管理服務實作
/// </summary>
public class ConnectionManager : IConnectionManager
{
    private readonly string _configPath;
    private List<ConnectionProfile> _profiles = [];
    private Guid? _currentProfileId;

    public event EventHandler<ConnectionProfile?>? CurrentProfileChanged;

    public ConnectionManager()
    {
        _configPath = GetConfigPath();
        LoadProfiles();
    }

    public IReadOnlyList<ConnectionProfile> GetAllProfiles() => _profiles.AsReadOnly();

    public ConnectionProfile? GetCurrentProfile()
    {
        if (_currentProfileId == null)
        {
            var defaultProfile = _profiles.FirstOrDefault(p => p.IsDefault);
            if (defaultProfile != null)
            {
                _currentProfileId = defaultProfile.Id;
            }
        }

        return _profiles.FirstOrDefault(p => p.Id == _currentProfileId);
    }

    public void SetCurrentProfile(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
        {
            _currentProfileId = profileId;
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
        if (index >= 0)
        {
            if (profile.IsDefault)
            {
                foreach (var p in _profiles)
                {
                    p.IsDefault = false;
                }
            }

            _profiles[index] = profile;
            SaveProfiles();

            if (_currentProfileId == profile.Id)
            {
                CurrentProfileChanged?.Invoke(this, profile);
            }
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
        return profile != null ? BuildConnectionString(profile) : null;
    }

    public string? GetConnectionString(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        return profile != null ? BuildConnectionString(profile) : null;
    }

    public string GetProfileName(Guid profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        return profile?.Name ?? profileId.ToString();
    }

    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "TableSpec");
        Directory.CreateDirectory(configDir);
        return Path.Combine(configDir, "connections.json");
    }

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
