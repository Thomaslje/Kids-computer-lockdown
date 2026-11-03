using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace KioskShared;

public record ReleaseAsset(string Name, long Id, string Url, long Size);

public record ReleaseInfo(string Tag, string Name, string Body, DateTimeOffset Published, List<ReleaseAsset> Assets)
{
    public ReleaseAsset? Find(string name) => Assets.FirstOrDefault(a => a.Name == name);
}

/// <summary>Reads the newest GitHub release and downloads its files. A token is only needed for a private repository.</summary>
public static class Releases
{
    static HttpRequestMessage Req(string url, string? token, string accept)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token)) r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        r.Headers.TryAddWithoutValidation("Accept", accept);
        r.Headers.TryAddWithoutValidation("User-Agent", "KioskSuite-updater");
        r.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return r;
    }

    public static async Task<ReleaseInfo> LatestAsync(HttpClient http, string repo, string? token = null, CancellationToken ct = default)
    {
        using var resp = await http.SendAsync(Req($"https://api.github.com/repos/{repo}/releases/latest", token, "application/vnd.github+json"), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No release found. Is the repository public and does it have a release?");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("GitHub refused the request (token wrong or expired, or too many checks: try again later).");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var assets = new List<ReleaseAsset>();
        foreach (var a in root.GetProperty("assets").EnumerateArray())
            assets.Add(new ReleaseAsset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("id").GetInt64(),
                a.GetProperty("browser_download_url").GetString() ?? "",
                a.GetProperty("size").GetInt64()));
        DateTimeOffset.TryParse(root.TryGetProperty("published_at", out var p) ? p.GetString() : null, out var published);
        return new ReleaseInfo(
            root.GetProperty("tag_name").GetString() ?? "?",
            root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            published, assets);
    }

    public static async Task DownloadAsync(HttpClient http, string repo, string? token, ReleaseAsset asset, string destination, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(token) ? asset.Url : $"https://api.github.com/repos/{repo}/releases/assets/{asset.Id}";
        using var resp = await http.SendAsync(Req(url, token, "application/octet-stream"), HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(destination);
        await resp.Content.CopyToAsync(fs, ct);
    }

    /// <summary>
    /// Downloads a release file and its .sig and checks the signature against the key built into this app.
    /// With requireSignature, a build without a key (or a file without a valid signature) is refused.
    /// </summary>
    public static async Task<(string File, bool Verified)> DownloadVerifiedAsync(HttpClient http, string repo, string? token,
        ReleaseInfo rel, string assetName, string folder, bool requireSignature, CancellationToken ct = default)
    {
        var asset = rel.Find(assetName) ?? throw new InvalidOperationException($"Release {rel.Tag} has no {assetName} file.");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, assetName);
        await DownloadAsync(http, repo, token, asset, file, ct);

        var key = BuildInfo.SigningPublicKey;
        if (key == "")
        {
            if (requireSignature)
            {
                File.Delete(file);
                throw new InvalidOperationException("This build has no update signing key built in, so it does not install downloaded updates.");
            }
            return (file, false);
        }

        var sigAsset = rel.Find(assetName + ".sig");
        if (sigAsset == null) { File.Delete(file); throw new InvalidOperationException($"{assetName} is not signed: update refused."); }
        var sigFile = file + ".sig";
        await DownloadAsync(http, repo, token, sigAsset, sigFile, ct);
        var ok = Signing.Verify(file, File.ReadAllText(sigFile), key);
        File.Delete(sigFile);
        if (!ok) { File.Delete(file); throw new InvalidOperationException($"The signature of {assetName} is wrong: update refused."); }
        return (file, true);
    }
}
