namespace KioskShared;

/// <summary>
/// A program path may contain environment variables (%LOCALAPPDATA%) and wildcards (* and ?) in any folder or file name,
/// for example %LOCALAPPDATA%\Roblox\Versions\*\RobloxPlayerLauncher.exe. This finds the real file on this PC;
/// if several match, the most recently changed folder wins (the newest version).
/// </summary>
public static class ExePathResolver
{
    public static bool IsPattern(string? path) =>
        !string.IsNullOrEmpty(path) && (path.Contains('*') || path.Contains('?') || path.Contains('%'));

    /// <summary>The existing file the path points to, or null.</summary>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!full.Contains('*') && !full.Contains('?')) return File.Exists(full) ? Path.GetFullPath(full) : null;

            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return null;
            var parts = full[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            var candidates = new List<string> { root };
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                var next = new List<string>();
                foreach (var dir in candidates)
                {
                    if (!Directory.Exists(dir)) continue;
                    var seg = parts[i];
                    if (!seg.Contains('*') && !seg.Contains('?'))
                    {
                        var p = Path.Combine(dir, seg);
                        if (last ? File.Exists(p) : Directory.Exists(p)) next.Add(p);
                    }
                    else
                    {
                        var found = last ? Directory.EnumerateFiles(dir, seg) : Directory.EnumerateDirectories(dir, seg);
                        next.AddRange(found.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).Take(20));   // newest first
                    }
                }
                candidates = next;
                if (candidates.Count == 0) return null;
            }
            return candidates.FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }
}
