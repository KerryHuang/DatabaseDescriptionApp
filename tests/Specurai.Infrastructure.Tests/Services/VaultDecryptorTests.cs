using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class VaultDecryptorTests
{
    [Fact]
    public void Decrypt_程式產生的Vault_來回一致()
    {
        const string plaintext = "hello, specurai!";
        const string password = "testpassword";

        var vault = CreateVault(plaintext, password);
        var result = VaultDecryptor.Decrypt(vault, password);

        result.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_格式不符_拋出例外()
    {
        var act = () => VaultDecryptor.Decrypt("not a vault", "password");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decrypt_空字串_拋出例外()
    {
        var act = () => VaultDecryptor.Decrypt("", "password");
        act.Should().Throw<Exception>();
    }

    private static string CreateVault(string plaintext, string password)
    {
        // 1. 生成隨機 salt（32 bytes）
        var salt = RandomNumberGenerator.GetBytes(32);

        // 2. PBKDF2-SHA256 衍生 80 bytes
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            10000,
            HashAlgorithmName.SHA256,
            80);

        var key = derived[..32];
        var hmacKey = derived[32..64];
        var iv = derived[64..80];

        // 3. PKCS7 padding
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var padLen = 16 - (plaintextBytes.Length % 16);
        var padded = plaintextBytes.Concat(Enumerable.Repeat((byte)padLen, padLen)).ToArray();

        // 4. AES-256-CTR 加密
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        var ciphertext = new byte[padded.Length];
        var counter = (byte[])iv.Clone();
        var keyStream = new byte[16];
        using var encryptor = aes.CreateEncryptor();

        for (int offset = 0; offset < padded.Length; offset += 16)
        {
            encryptor.TransformBlock(counter, 0, 16, keyStream, 0);
            // 遞增 counter（big-endian）
            for (int i = counter.Length - 1; i >= 0; i--)
                if (++counter[i] != 0) break;
            int blockSize = Math.Min(16, padded.Length - offset);
            for (int i = 0; i < blockSize; i++)
                ciphertext[offset + i] = (byte)(padded[offset + i] ^ keyStream[i]);
        }

        // 5. HMAC-SHA256
        var hmac = HMACSHA256.HashData(hmacKey, ciphertext);

        // 6. 組合 inner hex：salt + hmac + ciphertext
        var inner = Convert.ToHexString(salt).ToLower() + "\n" +
                    Convert.ToHexString(hmac).ToLower() + "\n" +
                    Convert.ToHexString(ciphertext).ToLower();

        // 7. hex encode inner
        var innerHex = Convert.ToHexString(Encoding.UTF8.GetBytes(inner)).ToLower();

        // 8. 每 80 個字元換行（Ansible vault 格式）
        var wrapped = string.Concat(
            Enumerable.Range(0, (innerHex.Length + 79) / 80)
                .Select(i => innerHex.Substring(i * 80, Math.Min(80, innerHex.Length - i * 80)) + "\n"));

        return "$ANSIBLE_VAULT;1.1;AES256\n" + wrapped;
    }
}
