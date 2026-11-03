using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using KioskShared;

namespace KioskAdmin;

/// <summary>Checks GitHub for a newer admin app and installs it over the running one.</summary>
public static class AppUpdater
{
    /// <summary>The newest release if it differs from this build; null if up to date (or this is a local build).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(HttpClient http, AdminData d, bool manual)
    {
        if (BuildInfo.ReleaseTag == "dev" && !manual) return null;
        var rel = await Releases.LatestAsync(http, GitHubUpdate.Repo(d), GitHubUpdate.Token(d));
        if (rel.Tag == BuildInfo.ReleaseTag) return null;
        return rel;
    }

    static bool FolderWritable(string dir)
    {
        try
        {
            var t = Path.Combine(dir, ".w" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(t, "");
            File.Delete(t);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Downloads, checks the signature and installs. Starts the new version and returns true (the caller then closes this one).
    /// When the program folder is writable (installed "for me only") no admin rights are needed; otherwise the installer asks for them.
    /// </summary>
    public static async Task<bool> UpdateAsync(HttpClient http, AdminData d, ReleaseInfo rel, Action<string> status)
    {
        var repo = GitHubUpdate.Repo(d);
        var token = GitHubUpdate.Token(d);
        var work = GitHubUpdate.NewWorkFolder();
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var exe = Path.Combine(appDir, "KioskAdmin.exe");

        if (FolderWritable(appDir))
        {
            status("Downloading " + rel.Tag + "...");
            var (zip, _) = await Releases.DownloadVerifiedAsync(http, repo, token, rel, "admin.zip", work, requireSignature: true);
            var files = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, files);
            if (!File.Exists(Path.Combine(files, "KioskAdmin.exe"))) throw new InvalidDataException("The download does not contain the admin app.");

            status("Installing...");
            var renamed = new List<(string Old, string Target)>();
            var added = new List<string>();
            try
            {
                foreach (var src in Directory.GetFiles(files, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(appDir, Path.GetRelativePath(files, src));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target))
                    {
                        var old = target + ".old-" + Guid.NewGuid().ToString("N")[..8];
                        File.Move(target, old);
                        renamed.Add((old, target));
                    }
                    else added.Add(target);
                    File.Copy(src, target);
                }
            }
            catch
            {
                // put everything back
                foreach (var a in added) try { File.Delete(a); } catch { }
                foreach (var (old, target) in renamed) { try { File.Delete(target); } catch { } try { File.Move(old, target); } catch { } }
                throw;
            }
        }
        else
        {
            status("Downloading " + rel.Tag + " (installer)...");
            var (setup, _) = await Releases.DownloadVerifiedAsync(http, repo, token, rel, "KioskAdminSetup.exe", work, requireSignature: true);
            status("Installing... (Windows may ask for permission)");
            var ps = $"Start-Process -FilePath '{setup}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/ALLUSERS','/DIR=\"{appDir}\"' -Verb RunAs -Wait";
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -Command \"" + ps.Replace("\"", "\\\"") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) throw new InvalidOperationException("The installer was cancelled or failed.");
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = appDir });
        return true;
    }
}
