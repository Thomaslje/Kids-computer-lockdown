using System.Diagnostics;
using System.IO;
using KioskShared;

namespace KioskLauncher;

/// <summary>
/// "Delete when closed": removes the files/folders listed on a tile (for example Roblox's saved login) once the tile's program
/// has been closed for a while. Also done when the launcher starts, so a login left behind by a power-off is removed too.
/// Only paths inside the kiosk account's own profile can be deleted.
/// </summary>
static class CloseCleanup
{
    static readonly Dictionary<string, DateTime> Watching = new();   // tile id -> when its program was last seen (or started)
    static readonly object Lock = new();
    static readonly TimeSpan Quiet = TimeSpan.FromSeconds(45);       // nothing running for this long = closed (a game can pause between launcher and game)

    /// <summary>Process names that mean "this tile's program is running". Empty = cannot be followed.</summary>
    static List<string> Names(Tile t)
    {
        var names = new List<string>();
        if (t.Type == TileType.Chrome) names.Add("chrome");
        else if (t.Type == TileType.Exe && !string.IsNullOrWhiteSpace(t.ExePath))
        {
            var file = Path.GetFileName(t.ExePath.Replace('/', '\\'));
            if (!file.Contains('*') && !file.Contains('?')) names.Add(Path.GetFileNameWithoutExtension(file));
        }
        foreach (var n in (t.CloseFirst ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!n.Contains("crash", StringComparison.OrdinalIgnoreCase))
                names.Add(n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool HasWork(Tile t) => !string.IsNullOrWhiteSpace(t.DeleteOnClose) && t.Type != TileType.Steam;

    static bool IsRunning(List<string> names)
    {
        foreach (var n in names)
        {
            Process[] found;
            try { found = Process.GetProcessesByName(n); } catch { continue; }
            var any = found.Length > 0;
            foreach (var p in found) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    /// <summary>Call when the tile is started: waits for its program to close, then deletes.</summary>
    public static void Watch(Tile t)
    {
        if (!HasWork(t)) return;
        var names = Names(t);
        if (names.Count == 0) { Log.Write($"Delete when closed: cannot follow '{t.Name}' (no program name); it is only done at start-up"); return; }

        lock (Lock)
        {
            var already = Watching.ContainsKey(t.Id);
            Watching[t.Id] = DateTime.UtcNow;
            if (already) return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(3000);
                    if (IsRunning(names)) { lock (Lock) Watching[t.Id] = DateTime.UtcNow; continue; }
                    DateTime last;
                    lock (Lock) last = Watching[t.Id];
                    if (DateTime.UtcNow - last >= Quiet) break;
                }
                Delete(t);
            }
            catch (Exception ex) { Log.Write("Delete when closed failed: " + ex.Message); }
            finally { lock (Lock) Watching.Remove(t.Id); }
        });
    }

    /// <summary>Start-up and setup changes: for every tile whose program is not running, delete its leftovers now.</summary>
    public static void RunAll(KioskConfig cfg)
    {
        foreach (var t in cfg.Tiles)
        {
            if (!HasWork(t)) continue;
            lock (Lock) if (Watching.ContainsKey(t.Id)) continue;
            var names = Names(t);
            if (names.Count > 0 && IsRunning(names)) continue;
            Delete(t);
        }
    }

    static void Delete(Tile t)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        foreach (var raw in (t.DeleteOnClose ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var entry = Environment.ExpandEnvironmentVariables(raw).Replace('/', '\\');
                if (entry.Contains(".."))
                { Log.Write("Delete when closed: skipped (contains ..): " + raw); continue; }

                var dir = Path.GetDirectoryName(entry);
                var pattern = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(pattern)) { Log.Write("Delete when closed: skipped (not a file or folder path): " + raw); continue; }

                // safety: only inside the kiosk account's profile, at least two folders deep (so never the profile or Documents itself)
                var full = Path.GetFullPath(dir).TrimEnd('\\');
                if (!full.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase) ||
                    full[(profile.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                { Log.Write("Delete when closed: skipped (outside the safe area): " + raw); continue; }

                int removed = 0;
                if (Directory.Exists(full))
                {
                    foreach (var f in Directory.GetFiles(full, pattern))
                        try { File.Delete(f); removed++; } catch { /* in use: next time */ }
                    foreach (var d in Directory.GetDirectories(full, pattern))
                        try { Directory.Delete(d, true); removed++; } catch { }
                }
                Log.Write($"Delete when closed ({t.Name}): {raw} -> {removed} removed");
            }
            catch (Exception ex) { Log.Write($"Delete when closed: '{raw}' failed: {ex.Message}"); }
        }
    }
}
