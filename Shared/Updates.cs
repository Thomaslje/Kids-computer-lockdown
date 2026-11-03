using System.Security.Cryptography;

namespace KioskShared;

public record UpdateStart(string Id, long TotalBytes);
public record UpdateChunk(string Id, int Index, string B64);
public record UpdateApply(string Id);

/// <summary>Hashes every file of the Agent and Launcher folders, so only changed files need to be sent.</summary>
public static class FileHasher
{
    /// <param name="root">Folder that contains the "Agent" and "Launcher" subfolders.</param>
    public static Dictionary<string, string> Hash(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in new[] { "Agent", "Launcher" })
        {
            var dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (name.Contains(".old-") || name.EndsWith(".new")) continue; // leftovers of an update
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                result[rel] = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
        }
        return result;
    }
}
