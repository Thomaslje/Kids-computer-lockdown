using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using KioskShared;

namespace KioskLauncher;

/// <summary>Finds out whether a tile's game is already running, and brings its window to the front.</summary>
static class RunningCheck
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

    const uint QueryLimited = 0x1000;
    const int SwRestore = 9;

    static string? ImagePath(int pid)
    {
        var h = OpenProcess(QueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// Standalone game: a process running exactly that .exe.
    /// Steam game: any process running from the game's install folder (Steam itself is not counted).
    /// </summary>
    public static bool IsRunning(Tile t, out IntPtr window)
    {
        window = IntPtr.Zero;
        string? exact = null;
        var prefixes = new List<string>();

        if (t.Type == TileType.Exe && !string.IsNullOrEmpty(t.ExePath))
        {
            exact = ExePathResolver.Resolve(t.ExePath);
            if (exact == null) return false;   // not installed here
        }
        else if (t.Type == TileType.Steam && !string.IsNullOrEmpty(t.SteamAppId))
            prefixes = SteamScanner.InstallDirs(t.SteamAppId);
        if (exact == null && prefixes.Count == 0) return false;

        bool found = false;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                var path = ImagePath(p.Id);
                if (path == null) continue;
                bool match = exact != null
                    ? path.Equals(exact, StringComparison.OrdinalIgnoreCase)
                    : prefixes.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;
                found = true;
                if (window == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero) window = p.MainWindowHandle;
            }
            catch { /* process ended while we looked */ }
            finally { p.Dispose(); }
        }
        return found;
    }

    public static void Focus(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        if (IsIconic(window)) ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
    }
}
