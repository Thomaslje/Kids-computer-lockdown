using System.Security.Cryptography;

namespace KioskShared;

/// <summary>ECDSA (P-256, SHA-256) signatures for update files. The private key stays with you (a GitHub secret); the apps only hold the public key.</summary>
public static class Signing
{
    public static (string Private, string Public) NewKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ec.ExportPkcs8PrivateKey()), Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
    }

    public static string PublicOf(string privateKeyBase64)
    {
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(string file, string privateKeyBase64)
    {
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        using var fs = File.OpenRead(file);
        return Convert.ToBase64String(ec.SignData(fs, HashAlgorithmName.SHA256));
    }

    public static bool Verify(string file, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            using var fs = File.OpenRead(file);
            return ec.VerifyData(fs, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256);
        }
        catch { return false; }
    }
}
