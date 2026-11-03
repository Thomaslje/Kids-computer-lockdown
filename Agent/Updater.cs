using System.IO.Compression;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// Receives a zip with changed files, then swaps them in. Running programs can't be overwritten but CAN be
/// renamed, so each old file is renamed to "*.old-xxxx" and the new one takes its place; the leftovers are
/// deleted the next time the agent starts.
/// </summary>
public static class Updater
{
    /// <summary>Folder that contains the Agent and Launcher folders (normally C:\Program Files\KioskSuite).</summary>
    public static string InstallRoot { get; set; } =
        Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\', '/'))!.FullName;

    static string StageDir(string id) => Path.Combine(Paths.DataDir, "Update", Path.GetFileName(id));

    public static Dictionary<string, string> Hashes() => FileHasher.Hash(InstallRoot);

    public static void Start(UpdateStart s)
    {
        var dir = StageDir(s.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "update.zip"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(dir, "total.txt"), s.TotalBytes.ToString());
    }

    public static void Chunk(UpdateChunk c)
    {
        var path = Path.Combine(StageDir(c.Id), "update.zip");
        if (!File.Exists(path)) throw new InvalidOperationException("No update in progress");
        var bytes = Convert.FromBase64String(c.B64);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write);
        fs.Write(bytes, 0, bytes.Length);
    }

    public static (bool Agent, bool Launcher) Apply(string id)
    {
        var dir = StageDir(id);
        var zipPath = Path.Combine(dir, "update.zip");
        var total = long.Parse(File.ReadAllText(Path.Combine(dir, "total.txt")));
        if (new FileInfo(zipPath).Length != total) throw new InvalidDataException("The upload is incomplete");

        var agentDir = Path.GetFullPath(Path.Combine(InstallRoot, "Agent")) + Path.DirectorySeparatorChar;
        var launcherDir = Path.GetFullPath(Path.Combine(InstallRoot, "Launcher")) + Path.DirectorySeparatorChar;
        bool agent = false, launcher = false;

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            // validate everything first so a bad package changes nothing
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                var t = Path.GetFullPath(Path.Combine(InstallRoot, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!t.StartsWith(agentDir, StringComparison.OrdinalIgnoreCase) &&
                    !t.StartsWith(launcherDir, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bad path in update: " + e.FullName);
            }

            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                var target = Path.GetFullPath(Path.Combine(InstallRoot, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                var tmp = target + ".new";
                e.ExtractToFile(tmp, true);
                if (File.Exists(target))
                    File.Move(target, target + ".old-" + Guid.NewGuid().ToString("N")[..8]);
                File.Move(tmp, target);

                if (target.StartsWith(agentDir, StringComparison.OrdinalIgnoreCase)) agent = true; else launcher = true;
            }
        }
        Directory.Delete(dir, true);
        return (agent, launcher);
    }

    public static void CleanupOld()
    {
        try
        {
            foreach (var sub in new[] { "Agent", "Launcher" })
            {
                var d = Path.Combine(InstallRoot, sub);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.old-*", SearchOption.AllDirectories))
                    try { File.Delete(f); } catch { /* still in use: next time */ }
            }
            var up = Path.Combine(Paths.DataDir, "Update");
            if (Directory.Exists(up)) Directory.Delete(up, true);
        }
        catch { }
    }
}
