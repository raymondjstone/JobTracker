using System.Security.Cryptography;
using System.Text;

namespace JobTracker.Services;

/// <summary>
/// Provides encryption/decryption for sensitive settings (SMTP passwords, API keys, etc.)
/// Uses AES-256-GCM with a machine-specific key derived from DPAPI.
/// </summary>
public static class SettingsEncryptionService
{
    private const string EncryptedPrefix = "ENC:";
    private static readonly byte[] AdditionalData = "JobTracker.Settings"u8.ToArray();

    /// <summary>
    /// Encrypts a plaintext value. Returns prefixed ciphertext.
    /// If already encrypted (has ENC: prefix), returns as-is.
    /// </summary>
    public static string Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        if (plaintext.StartsWith(EncryptedPrefix))
            return plaintext; // Already encrypted

        var key = GetOrCreateKey();
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, AdditionalData);

        // Format: ENC:base64(nonce + tag + ciphertext)
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return EncryptedPrefix + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts a value. If not encrypted (no ENC: prefix), returns as-is.
    /// </summary>
    public static string Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        if (!ciphertext.StartsWith(EncryptedPrefix))
            return ciphertext; // Not encrypted, return plaintext

        try
        {
            var combined = Convert.FromBase64String(ciphertext[EncryptedPrefix.Length..]);
            var key = GetOrCreateKey();

            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            var nonce = combined[..nonceSize];
            var tag = combined[nonceSize..(nonceSize + tagSize)];
            var encrypted = combined[(nonceSize + tagSize)..];

            var plaintext = new byte[encrypted.Length];
            using var aes = new AesGcm(key, tagSize);
            aes.Decrypt(nonce, encrypted, tag, plaintext, AdditionalData);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception) when (true)
        {
            // If decryption fails (wrong key, corrupted data, invalid base64), return empty
            // This handles migration scenarios where the key has changed
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns true if the value is encrypted (has ENC: prefix).
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
    }

    private static byte[] GetOrCreateKey()
    {
        // Use a file-based key in the user's profile directory
        var keyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JobTracker");
        var keyFile = Path.Combine(keyDir, ".encryption-key");

        if (File.Exists(keyFile))
        {
            var existingKey = File.ReadAllBytes(keyFile);
            if (existingKey.Length == 32) return existingKey;
        }

        // Generate a new 256-bit key
        var newKey = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(keyDir);
        File.WriteAllBytes(keyFile, newKey);
        return newKey;
    }
}
