using System.Diagnostics;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// The PC is idle when no program from the kids' tile list is running and nothing started from the admin app is open.
/// (The agent runs as SYSTEM and cannot see windows, so it looks at processes.)
/// </summary>
static class IdleCheck
{
    public static bool IsIdle(out string reason)
    {
        if (Activity.IsBusy(out reason)) return false;

        KioskConfig? cfg = null;
        try { if (File.Exists(Paths.ConfigFile)) cfg = Json.From<KioskConfig>(File.ReadAllText(Paths.ConfigFile)); } catch { }

        var names = new List<string>();
        bool steam = false;
        foreach (var t in cfg?.Tiles ?? new List<Tile>())
        {
            if (t.Type == TileType.Steam) steam = true;
            else if (t.Type == TileType.Chrome) names.Add("chrome");
            else if (t.Type == TileType.Exe && !string.IsNullOrWhiteSpace(t.ExePath))
            {
                var file = Path.GetFileName(t.ExePath.Replace('/', '\\'));
                if (!file.Contains('*') && !file.Contains('?')) names.Add(Path.GetFileNameWithoutExtension(file));
                if (t.ExePath.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)) steam = true;
            }
            // programs the tile closes before it starts (Roblox: the game itself); crash reporters often linger, so they do not count
            foreach (var n in (t.CloseFirst ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!n.Contains("crash", StringComparison.OrdinalIgnoreCase))
                    names.Add(n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n);
        }

        foreach (var n in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] found;
            try { found = Process.GetProcessesByName(n); } catch { continue; }
            var any = found.Length > 0;
            foreach (var p in found) p.Dispose();
            if (any) { reason = n + " is running"; return false; }
        }

        if (steam)
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    string? image = null;
                    try { image = p.MainModule?.FileName; } catch { }
                    if (image != null && image.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase))
                    { reason = "a Steam game is running"; return false; }
                }
            }
        }
        reason = "";
        return true;
    }
}
