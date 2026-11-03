using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KioskAdmin;

/// <summary>Finds the newest release of the GitHub repository and downloads its kiosk-update.zip.</summary>
public static class GitHubUpdate
{
    public const string AssetName = "kiosk-update.zip";
    public record Release(string Tag, long AssetId, long Size);

    // the token is stored encrypted for this Windows user only
    public static string Protect(string token) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    static HttpRequestMessage Req(string url, string token, string accept)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        r.Headers.TryAddWithoutValidation("Accept", accept);
        r.Headers.TryAddWithoutValidation("User-Agent", "KioskAdmin");
        r.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return r;
    }

    public static async Task<Release> LatestAsync(HttpClient http, string repo, string token)
    {
        using var resp = await http.SendAsync(Req($"https://api.github.com/repos/{repo}/releases/latest", token, "application/vnd.github+json"));
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new Exception("Repository or release not found. Check the repository name and that the token may read it (and that a release exists).");
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new Exception("GitHub refused the token (expired or wrong).");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == AssetName)
                return new Release(tag, a.GetProperty("id").GetInt64(), a.GetProperty("size").GetInt64());
        throw new Exception($"Release {tag} has no {AssetName} file.");
    }

    /// <summary>Downloads and unpacks the release. Returns the folder that contains Agent and Launcher.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string repo, string token, Release rel)
    {
        var work = Path.Combine(Path.GetTempPath(), "KioskUpdate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, AssetName);
        using (var resp = await http.SendAsync(
                   Req($"https://api.github.com/repos/{repo}/releases/assets/{rel.AssetId}", token, "application/octet-stream"),
                   HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs);
        }
        var dir = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, dir);
        File.Delete(zip);

        foreach (var cand in new[] { dir }.Concat(Directory.GetDirectories(dir)))
            if (File.Exists(Path.Combine(cand, "Agent", "KioskAgent.exe")) && File.Exists(Path.Combine(cand, "Launcher", "KioskLauncher.exe")))
                return cand;
        throw new Exception("The downloaded file does not contain the Agent and Launcher folders.");
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
    }
}
