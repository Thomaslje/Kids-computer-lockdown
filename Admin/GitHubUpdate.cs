using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace KioskAdmin;

/// <summary>Helpers around GitHub releases: the saved token (optional, only for a private repository) and unpacking the kiosk update.</summary>
public static class GitHubUpdate
{
    public const string KioskAsset = "kiosk-update.zip";

    // the token is stored encrypted for this Windows user only
    public static string Protect(string token) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    public static string Repo(AdminData d) => string.IsNullOrWhiteSpace(d.GitHubRepo) ? KioskShared.BuildInfo.GitHubRepo : d.GitHubRepo!;
    public static string? Token(AdminData d) =>
        string.IsNullOrEmpty(d.GitHubToken) ? null : (Unprotect(d.GitHubToken) is { Length: > 0 } t ? t : null);

    /// <summary>Unpacks the kiosk zip. Returns the folder that contains Agent and Launcher.</summary>
    public static string ExtractKiosk(string zipFile)
    {
        var dir = Path.Combine(Path.GetDirectoryName(zipFile)!, "files");
        ZipFile.ExtractToDirectory(zipFile, dir);
        File.Delete(zipFile);
        foreach (var cand in new[] { dir }.Concat(Directory.GetDirectories(dir)))
            if (File.Exists(Path.Combine(cand, "Agent", "KioskAgent.exe")) && File.Exists(Path.Combine(cand, "Launcher", "KioskLauncher.exe")))
                return cand;
        throw new InvalidDataException("The downloaded file does not contain the Agent and Launcher folders.");
    }

    public static string NewWorkFolder()
    {
        var w = Path.Combine(Path.GetTempPath(), "KioskUpdate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(w);
        return w;
    }

    /// <summary>Deletes old downloads (called at start-up).</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "KioskUpdate-*"))
                try { Directory.Delete(d, true); } catch { }
        }
        catch { }
        // files this app renamed while updating itself
        try
        {
            foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, "*.old-*", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }
}
