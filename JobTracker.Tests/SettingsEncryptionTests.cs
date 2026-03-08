using JobTracker.Services;
using Xunit;

namespace JobTracker.Tests;

public class SettingsEncryptionTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var original = "MySecretPassword123!";
        var encrypted = SettingsEncryptionService.Encrypt(original);
        var decrypted = SettingsEncryptionService.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ReturnsEncPrefix()
    {
        var encrypted = SettingsEncryptionService.Encrypt("password");
        Assert.StartsWith("ENC:", encrypted);
    }

    [Fact]
    public void Encrypt_AlreadyEncrypted_ReturnsUnchanged()
    {
        var encrypted = SettingsEncryptionService.Encrypt("password");
        var doubleEncrypted = SettingsEncryptionService.Encrypt(encrypted);
        Assert.Equal(encrypted, doubleEncrypted);
    }

    [Fact]
    public void Decrypt_PlainText_ReturnsAsIs()
    {
        var result = SettingsEncryptionService.Decrypt("plain-password");
        Assert.Equal("plain-password", result);
    }

    [Fact]
    public void Encrypt_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SettingsEncryptionService.Encrypt(null));
        Assert.Equal(string.Empty, SettingsEncryptionService.Encrypt(""));
    }

    [Fact]
    public void Decrypt_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SettingsEncryptionService.Decrypt(null));
        Assert.Equal(string.Empty, SettingsEncryptionService.Decrypt(""));
    }

    [Fact]
    public void IsEncrypted_DetectsPrefix()
    {
        Assert.False(SettingsEncryptionService.IsEncrypted(null));
        Assert.False(SettingsEncryptionService.IsEncrypted(""));
        Assert.False(SettingsEncryptionService.IsEncrypted("plain-text"));
        Assert.True(SettingsEncryptionService.IsEncrypted("ENC:abc123"));
    }

    [Fact]
    public void Encrypt_DifferentValues_ProduceDifferentCiphertexts()
    {
        var enc1 = SettingsEncryptionService.Encrypt("password1");
        var enc2 = SettingsEncryptionService.Encrypt("password2");
        Assert.NotEqual(enc1, enc2);
    }

    [Fact]
    public void Encrypt_SameValue_ProducesDifferentCiphertexts()
    {
        // Due to random nonce, same plaintext should produce different ciphertext
        var enc1 = SettingsEncryptionService.Encrypt("same-password");
        var enc2 = SettingsEncryptionService.Encrypt("same-password");
        Assert.NotEqual(enc1, enc2);

        // But both should decrypt to the same value
        Assert.Equal("same-password", SettingsEncryptionService.Decrypt(enc1));
        Assert.Equal("same-password", SettingsEncryptionService.Decrypt(enc2));
    }

    [Fact]
    public void Decrypt_CorruptedCiphertext_ReturnsEmpty()
    {
        var result = SettingsEncryptionService.Decrypt("ENC:not-valid-base64!!!");
        // Should not throw, returns empty on corruption
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Encrypt_SpecialCharacters_RoundTrips()
    {
        var special = "p@$$w0rd!#%^&*()_+-=[]{}|;':\",./<>?";
        var encrypted = SettingsEncryptionService.Encrypt(special);
        var decrypted = SettingsEncryptionService.Decrypt(encrypted);
        Assert.Equal(special, decrypted);
    }

    [Fact]
    public void Encrypt_LongValue_RoundTrips()
    {
        var longValue = new string('x', 10000);
        var encrypted = SettingsEncryptionService.Encrypt(longValue);
        var decrypted = SettingsEncryptionService.Decrypt(encrypted);
        Assert.Equal(longValue, decrypted);
    }
}
