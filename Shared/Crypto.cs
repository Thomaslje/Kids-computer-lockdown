using System.Security.Cryptography;
using System.Text;

namespace KioskShared;

public static class Crypto
{
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    // Layout: nonce(12) + tag(16) + ciphertext
    public static byte[] Encrypt(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, pt, ct, tag);
        var result = new byte[28 + ct.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, 12);
        ct.CopyTo(result, 28);
        return result;
    }

    public static string Decrypt(byte[] key, byte[] data)
    {
        if (data.Length < 28) throw new CryptographicException("Message too short");
        var nonce = data.AsSpan(0, 12);
        var tag = data.AsSpan(12, 16);
        var ct = data.AsSpan(28);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}

public static class Passwords
{
    // Used only until the admin app has pushed a real password.
    public const string DefaultPassword = "change-me";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return password == DefaultPassword;
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
