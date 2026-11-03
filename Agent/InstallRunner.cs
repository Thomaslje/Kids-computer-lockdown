using System.Collections.Concurrent;
using System.Diagnostics;
using KioskShared;

namespace KioskAgent;

/// <summary>Runs an installer silently as SYSTEM in the background; the admin app polls for the result.</summary>
static class InstallRunner
{
    static readonly ConcurrentDictionary<string, InstallStatus> Jobs = new();

    public static string Start(InstallRequest r)
    {
        string path;
        if (!string.IsNullOrEmpty(r.UploadId)) path = FileTransfer.PathOf(r.UploadId);
        else if (!string.IsNullOrWhiteSpace(r.Path)) path = r.Path.Trim().Trim('"');
        else throw new InvalidOperationException("No installer given");
        if (!File.Exists(path)) throw new FileNotFoundException("Installer not found on the PC: " + path);

        var id = Guid.NewGuid().ToString("N");
        Jobs[id] = new InstallStatus(false, false, false, "Installing...");
        var minutes = Math.Clamp(r.TimeoutMinutes <= 0 ? 60 : r.TimeoutMinutes, 1, 240);
        _ = Task.Run(() => Run(id, path, r, minutes));
        return id;
    }

    public static InstallStatus Status(string id) =>
        Jobs.TryGetValue(id, out var s) ? s : new InstallStatus(true, false, false, "Unknown job (the agent restarted?)");

    static void Run(string id, string path, InstallRequest r, int minutes)
    {
        try
        {
            var isMsi = path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            var args = r.Args ?? "";
            ProcessStartInfo psi;
            if (isMsi)
                psi = new ProcessStartInfo("msiexec.exe", $"/i \"{path}\" " + (args.Trim() == "" ? "/qn /norestart" : args));
            else
                psi = new ProcessStartInfo(path, args) { WorkingDirectory = Path.GetDirectoryName(path)! };
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            Process p;
            if (r.OnScreen)
            {
                // run it on the logged-in screen (as SYSTEM) so installers that need a window or a click can show it
                int pid = SessionLauncher.Start(psi.FileName, psi.Arguments);
                AgentWorker.WriteCommand("front");
                try { p = Process.GetProcessById(pid); }
                catch
                {
                    Jobs[id] = new InstallStatus(true, true, false, "Started on the screen and finished at once (exit code unknown)");
                    return;
                }
            }
            else p = Process.Start(psi)!;
            using var proc = p;
            if (!p.WaitForExit(minutes * 60_000))
            {
                try { p.Kill(true); } catch { }
                Jobs[id] = new InstallStatus(true, false, false, $"Stopped after {minutes} minutes (still running)");
                return;
            }
            var code = p.ExitCode;
            bool reboot = code is 3010 or 1641;
            bool ok = code == 0 || reboot;
            Jobs[id] = new InstallStatus(true, ok, reboot,
                ok ? (reboot ? "Installed, restart needed" : "Installed") : $"Installer exited with code {code}");
        }
        catch (Exception ex) { Jobs[id] = new InstallStatus(true, false, false, "Could not run: " + ex.Message); }
        finally
        {
            if (r.DeleteAfter && !string.IsNullOrEmpty(r.UploadId)) FileTransfer.Discard(r.UploadId);
        }
    }
}
