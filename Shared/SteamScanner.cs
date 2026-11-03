using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KioskShared;

public static class SteamScanner
{
    public static string? SteamPath()
    {
        var p = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
             ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        if (p == null)
        {
            const string def = @"C:\Program Files (x86)\Steam";
            if (Directory.Exists(def)) p = def;
        }
        return p;
    }

    public static List<string> Libraries()
    {
        var result = new List<string>();
        var steam = SteamPath();
        if (steam == null) return result;
        var main = Path.Combine(steam, "steamapps");
        result.Add(main);
        var vdf = Path.Combine(main, "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps");
                if (Directory.Exists(lib) && !result.Contains(lib, StringComparer.OrdinalIgnoreCase))
                    result.Add(lib);
            }
        }
        return result;
    }

    public static List<SteamGame> Scan()
    {
        var list = new List<SteamGame>();
        foreach (var lib in Libraries())
        {
            if (!Directory.Exists(lib)) continue;
            foreach (var f in Directory.GetFiles(lib, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(f);
                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                // 228980 = Steamworks Common Redistributables (not a game)
                if (id != "" && name != "" && id != "228980" && !list.Any(g => g.AppId == id))
                    list.Add(new SteamGame(id, name));
            }
        }
        return list.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Full folder path(s) of an installed Steam game, each ending in a backslash.</summary>
    public static List<string> InstallDirs(string appId)
    {
        var result = new List<string>();
        foreach (var lib in Libraries())
        {
            var f = Path.Combine(lib, $"appmanifest_{appId}.acf");
            if (!File.Exists(f)) continue;
            var dir = Regex.Match(File.ReadAllText(f), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
            if (dir != "") result.Add(Path.Combine(lib, "common", dir) + Path.DirectorySeparatorChar);
        }
        return result;
    }

    public static bool IsInstalled(string appId) =>
        Libraries().Any(l => File.Exists(Path.Combine(l, $"appmanifest_{appId}.acf")));
}

public static class ChromeLocator
{
    public static string? Find()
    {
        var fromReg = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string;
        if (!string.IsNullOrEmpty(fromReg) && File.Exists(fromReg)) return fromReg;
        foreach (var c in new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        })
            if (File.Exists(c)) return c;
        return null;
    }
}
