using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 連線設定匯出/匯入服務實作
/// </summary>
public class ConnectionExportService : IConnectionExportService
{
    /// <summary>加密格式 magic bytes</summary>
    private static readonly byte[] MagicBytes = "TSEC"u8.ToArray();

    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32; // AES-256
    private const int Iterations = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    /// <inheritdoc />
    public byte[] ExportToJson(IReadOnlyList<ConnectionProfile> profiles, bool includePasswords)
    {
        var exportProfiles = PrepareProfiles(profiles, includePasswords);
        var exportData = new ConnectionExportData
        {
            Profiles = exportProfiles
        };
        return JsonSerializer.SerializeToUtf8Bytes(exportData, JsonOptions);
    }

    /// <inheritdoc />
    public byte[] ExportToEncryptedJson(IReadOnlyList<ConnectionProfile> profiles, string password, bool includePasswords)
    {
        var jsonBytes = ExportToJson(profiles, includePasswords);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(jsonBytes, 0, jsonBytes.Length);

        // [TSEC][Salt][IV][Encrypted]
        var result = new byte[MagicBytes.Length + SaltSize + IvSize + encrypted.Length];
        MagicBytes.CopyTo(result, 0);
        salt.CopyTo(result, MagicBytes.Length);
        iv.CopyTo(result, MagicBytes.Length + SaltSize);
        encrypted.CopyTo(result, MagicBytes.Length + SaltSize + IvSize);

        return result;
    }

    /// <inheritdoc />
    public ConnectionExportData ImportFromJson(byte[] data)
    {
        try
        {
            var exportData = JsonSerializer.Deserialize<ConnectionExportData>(data, JsonOptions);
            return exportData ?? throw new InvalidOperationException("檔案格式無法辨識");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("檔案格式無法辨識", ex);
        }
    }

    /// <inheritdoc />
    public ConnectionExportData ImportFromEncryptedJson(byte[] data, string password)
    {
        if (!IsEncryptedFormat(data))
            throw new InvalidOperationException("檔案格式無法辨識，非加密格式");

        var offset = MagicBytes.Length;
        var salt = data[offset..(offset + SaltSize)];
        offset += SaltSize;
        var iv = data[offset..(offset + IvSize)];
        offset += IvSize;
        var encrypted = data[offset..];

        var key = DeriveKey(password, salt);

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var jsonBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

            return ImportFromJson(jsonBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("密碼不正確，請重新輸入", ex);
        }
    }

    /// <inheritdoc />
    public bool IsEncryptedFormat(byte[] data)
    {
        if (data.Length < MagicBytes.Length)
            return false;

        return data[..MagicBytes.Length].SequenceEqual(MagicBytes);
    }

    /// <summary>
    /// 準備匯出用的連線清單（處理密碼）
    /// </summary>
    private static List<ConnectionProfile> PrepareProfiles(IReadOnlyList<ConnectionProfile> profiles, bool includePasswords)
    {
        return profiles.Select(p => new ConnectionProfile
        {
            Id = p.Id,
            Name = p.Name,
            Server = p.Server,
            Database = p.Database,
            AuthType = p.AuthType,
            Username = p.Username,
            Password = includePasswords ? p.Password : null,
            IsDefault = p.IsDefault,
            Environment = p.Environment
        }).ToList();
    }

    /// <summary>
    /// 使用 PBKDF2 從密碼衍生金鑰
    /// </summary>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
