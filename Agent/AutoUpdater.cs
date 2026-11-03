using System.Net.Http;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// If "auto-update" is on (pushed from the admin app), checks GitHub for a newer release, downloads it, checks its
/// signature and installs it, but only while the PC is idle (twice in a row, 30 seconds apart).
/// </summary>
static class AutoUpdater
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };
    static string? _appliedTag;

    static string LogFile => Path.Combine(Paths.DataDir, "autoupdate.log");
    static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\n");
        }
        catch { }
    }

    public static void StartLoop() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromMinutes(3));
        while (true)
        {
            try { await CycleAsync(); }
            catch (Exception ex) { Log("Check failed: " + ex.Message); }
            await Task.Delay(TimeSpan.FromMinutes(30));
        }
    });

    static bool Enabled()
    {
        try { return File.Exists(Paths.ConfigFile) && Json.From<KioskConfig>(File.ReadAllText(Paths.ConfigFile))?.AutoUpdate == true; }
        catch { return false; }
    }

    static async Task CycleAsync()
    {
        if (!Enabled()) return;
        if (BuildInfo.ReleaseTag == "dev" || BuildInfo.SigningPublicKey == "") return;   // own builds / no key: never auto-update

        var rel = await Releases.LatestAsync(Http, BuildInfo.GitHubRepo);
        if (rel.Tag == BuildInfo.ReleaseTag || rel.Tag == _appliedTag) return;
        Log($"New release {rel.Tag} (this PC has {BuildInfo.ReleaseTag})");

        var dir = Path.Combine(Paths.DataDir, "AutoUpdate");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        var (file, _) = await Releases.DownloadVerifiedAsync(Http, BuildInfo.GitHubRepo, null, rel, "kiosk-update.zip", dir, requireSignature: true);
        Log("Downloaded and signature checked");

        var deadline = DateTime.UtcNow.AddHours(12);
        string reason = "";
        bool idle = false;
        while (DateTime.UtcNow < deadline)
        {
            if (!Enabled()) { Cleanup(dir); Log("Auto-update was turned off"); return; }
            if (IdleCheck.IsIdle(out reason))
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                if (IdleCheck.IsIdle(out reason)) { idle = true; break; }
            }
            Log("Waiting, PC is busy: " + reason);
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
        if (!idle) { Cleanup(dir); Log("Gave up waiting for an idle PC; will try again later"); return; }

        var (agent, launcher) = Updater.ApplyZipFile(file);
        _appliedTag = rel.Tag;
        KioskShortcut.Ensure();
        Cleanup(dir);
        Log($"Installed {rel.Tag} (agent changed: {agent}, launcher changed: {launcher})");
        if (launcher) AgentWorker.WriteCommand("reloadui");   // the kids' screen restarts with the new code
        if (agent) AgentWorker.RestartSoon();
    }

    static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
