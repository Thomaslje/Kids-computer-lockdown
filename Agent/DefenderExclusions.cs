using System.Diagnostics;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// The kiosk programs are unsigned and do things that look suspicious to antivirus heuristics (keyboard hook, shell
/// replacement, a service that takes commands). So the agent asks Microsoft Defender to leave its own folders alone, before
/// any update arrives. Adding the same exclusion twice does nothing.
/// </summary>
static class DefenderExclusions
{
    public static void Ensure()
    {
        try
        {
            var paths = new[] { Updater.InstallRoot, Paths.DataDir };
            var list = string.Join(",", paths.Select(p => "'" + p.Replace("'", "''") + "'"));

            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("Add-MpPreference -ExclusionPath " + list);
            using var p = Process.Start(psi);
            p?.StandardOutput.ReadToEnd();
            p?.StandardError.ReadToEnd();
            p?.WaitForExit(30000);
        }
        catch { /* another antivirus, or Defender managed centrally: nothing to do here */ }
    }
}
