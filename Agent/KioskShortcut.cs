using System.Security.Principal;
using KioskShared;
using Microsoft.Win32;

namespace KioskAgent;

/// <summary>
/// Puts a "Return to Kiosk" icon on the Kiosk account's desktop. The desktop is only visible after leaving kiosk
/// mode; clicking the icon takes the screen back without restarting the PC.
/// </summary>
static class KioskShortcut
{
    public static void Ensure()
    {
        try
        {
            var user = File.Exists(Paths.KioskUserFile) ? File.ReadAllText(Paths.KioskUserFile).Trim() : "";
            if (user == "") return;

            var sid = ((SecurityIdentifier)new NTAccount(Environment.MachineName, user)
                .Translate(typeof(SecurityIdentifier))).Value;
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
            var profile = key?.GetValue("ProfileImagePath") as string;
            if (string.IsNullOrEmpty(profile)) return;
            var desktop = Path.Combine(Environment.ExpandEnvironmentVariables(profile), "Desktop");
            if (!Directory.Exists(desktop)) return;

            var exe = Path.Combine(Updater.InstallRoot, "Launcher", "KioskLauncher.exe");
            if (!File.Exists(exe)) return;

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(Path.Combine(desktop, "Return to Kiosk.lnk"));
            link.TargetPath = exe;
            link.Arguments = "--return";
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.IconLocation = exe + ",0";
            link.Description = "Go back to the kiosk screen";
            link.Save();
        }
        catch { /* the icon is a convenience: never let it break the agent */ }
    }
}
