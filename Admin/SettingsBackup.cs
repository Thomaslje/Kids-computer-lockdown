using System.IO;
using System.Security.Cryptography;
using System.Text;
using KioskShared;

namespace KioskAdmin;

/// <summary>
/// Export/import of all admin app settings (pairing key, PCs, tiles, passwords' hashes, GitHub settings...) in ONE file that is
/// locked with a password: file = "KAB1" + salt(16) + AES-GCM(key from the password, PBKDF2-SHA256 300,000 rounds).
/// </summary>
static class SettingsBackup
{
    const string Magic = "KAB1";
    const int Iterations = 300_000;
    public const int MinPasswordLength = 8;

    sealed class Payload
    {
        public int V { get; set; } = 1;
        public AdminData Data { get; set; } = new();
        public string? GitHubTokenPlain { get; set; }   // the saved token is encrypted for this Windows user only, so it is carried in clear inside the locked file
    }

    static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static void Export(AdminData d, string password, string file)
    {
        var copy = Json.From<AdminData>(Json.To(d)) ?? new AdminData();   // a copy, so the live settings are not touched
        var payload = new Payload { Data = copy, GitHubTokenPlain = GitHubUpdate.Token(d) };
        copy.GitHubToken = null;
        var salt = RandomNumberGenerator.GetBytes(16);
        var blob = Crypto.Encrypt(Derive(password, salt), Json.To(payload));
        var all = new byte[4 + 16 + blob.Length];
        Encoding.ASCII.GetBytes(Magic).CopyTo(all, 0);
        salt.CopyTo(all, 4);
        blob.CopyTo(all, 20);
        File.WriteAllBytes(file, all);
    }

    /// <summary>Returns the settings from the file. Throws a message that is fit to show when the file or the password is wrong.</summary>
    public static AdminData Import(string file, string password)
    {
        var all = File.ReadAllBytes(file);
        if (all.Length < 20 + 28 || Encoding.ASCII.GetString(all, 0, 4) != Magic)
            throw new InvalidDataException("This is not a Kiosk Admin settings file.");
        string json;
        try { json = Crypto.Decrypt(Derive(password, all[4..20]), all[20..]); }
        catch (CryptographicException) { throw new InvalidDataException("Wrong password (or the file is damaged)."); }
        var payload = Json.From<Payload>(json) ?? throw new InvalidDataException("The file is empty.");
        var d = payload.Data;
        if (string.IsNullOrEmpty(d.KeyBase64)) throw new InvalidDataException("The file has no pairing key.");
        d.GitHubToken = string.IsNullOrEmpty(payload.GitHubTokenPlain) ? null : GitHubUpdate.Protect(payload.GitHubTokenPlain);
        return d;
    }
}
