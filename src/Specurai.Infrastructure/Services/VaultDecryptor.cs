using System.Security.Cryptography;
using System.Text;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// Ansible Vault AES-256-CTR 解密工具
/// </summary>
public static class VaultDecryptor
{
    private const string Header = "$ANSIBLE_VAULT;1.1;AES256";

    /// <summary>
    /// 解密 Ansible Vault 加密內容
    /// </summary>
    /// <param name="vaultContent">Vault 加密文字（以 $ANSIBLE_VAULT 開頭）</param>
    /// <param name="password">Vault 密碼</param>
    /// <returns>解密後的明文</returns>
    public static string Decrypt(string vaultContent, string password)
    {
        var lines = vaultContent.Trim().Split('\n', StringSplitOptions.TrimEntries);

        if (lines.Length < 2 || lines[0] != Header)
            throw new InvalidOperationException("不支援的 Vault 格式，僅支援 $ANSIBLE_VAULT;1.1;AES256");

        var hexData = string.Concat(lines[1..]);
        var rawBytes = Convert.FromHexString(hexData);
        var inner = Encoding.UTF8.GetString(rawBytes);
        var parts = inner.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3)
            throw new InvalidOperationException($"Vault 內部格式錯誤，應為 3 段，得到 {parts.Length} 段");

        var salt = Convert.FromHexString(parts[0].Trim());
        var hmacBytes = Convert.FromHexString(parts[1].Trim());
        var ciphertext = Convert.FromHexString(parts[2].Trim());

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            10000,
            HashAlgorithmName.SHA256,
            80);

        var key = derived[..32];
        var hmacKey = derived[32..64];
        var iv = derived[64..80];

        var computedHmac = HMACSHA256.HashData(hmacKey, ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(computedHmac, hmacBytes))
            throw new InvalidOperationException("HMAC 驗證失敗，密碼錯誤或資料損毀");

        var plaintext = AesCtr(key, iv, ciphertext);

        var pad = plaintext[^1];
        if (pad > 0 && pad <= 16 && plaintext[^pad..].All(b => b == pad))
            plaintext = plaintext[..^pad];

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] AesCtr(byte[] key, byte[] iv, byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        var result = new byte[ciphertext.Length];
        var counter = (byte[])iv.Clone();
        var keyStream = new byte[16];

        using var encryptor = aes.CreateEncryptor();

        for (int offset = 0; offset < ciphertext.Length; offset += 16)
        {
            encryptor.TransformBlock(counter, 0, 16, keyStream, 0);
            IncrementCounter(counter);

            int blockSize = Math.Min(16, ciphertext.Length - offset);
            for (int i = 0; i < blockSize; i++)
                result[offset + i] = (byte)(ciphertext[offset + i] ^ keyStream[i]);
        }

        return result;
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0) break;
        }
    }
}
