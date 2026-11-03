using System.Diagnostics;
using KioskShared;
using Microsoft.Win32;

namespace KioskAgent;

/// <summary>Applies kiosk options (account password, Ctrl+Alt+Del) on request from the admin app.</summary>
static class OptionsApplier
{
    const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    const string KeyboardLayoutKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    const string PasswordLessKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";

    // Disables both Delete keys (Ctrl+Alt+Del can't be pressed without one). Effective after a restart.
    static readonly byte[] ScancodeMap =
        { 0,0,0,0, 0,0,0,0, 3,0,0,0, 0,0,0x53,0xE0, 0,0,0x53,0x00, 0,0,0,0 };

    public static OptionsResult Apply(KioskOptions o)
    {
        var notes = new List<string>();
        bool restart = false;

        var user = File.Exists(Paths.KioskUserFile) ? File.ReadAllText(Paths.KioskUserFile).Trim() : "";
        if (user == "") throw new InvalidOperationException("No kiosk account is registered on this PC");

        // ---- password / automatic login ----
        if (!string.IsNullOrEmpty(o.Password))
        {
            var rc = NetUser(user, o.Password, out var output);
            if (rc != 0)
                throw new InvalidOperationException("Windows refused that password (too short or against a password policy?): " + output);

            using var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey, true)
                           ?? throw new InvalidOperationException("Winlogon settings not found");
            wl.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
            wl.SetValue("DefaultUserName", user, RegistryValueKind.String);
            wl.SetValue("DefaultPassword", o.Password, RegistryValueKind.String);   // automatic login keeps working
            notes.Add("Password changed.");
        }

        // Windows 10/11 can refuse automatic login ("Require Windows Hello sign-in")
        using (var pl = Registry.LocalMachine.OpenSubKey(PasswordLessKey, true))
            pl?.SetValue("DevicePasswordLessBuildVersion", 0, RegistryValueKind.DWord);

        // ---- Ctrl+Alt+Del ----
        using (var kl = Registry.LocalMachine.OpenSubKey(KeyboardLayoutKey, true)
                        ?? throw new InvalidOperationException("Keyboard settings not found"))
        {
            var existing = kl.GetValue("Scancode Map") as byte[];
            bool ours = existing != null && existing.SequenceEqual(ScancodeMap);
            if (o.BlockCtrlAltDel && !ours)
            {
                kl.SetValue("Scancode Map", ScancodeMap, RegistryValueKind.Binary);
                restart = true;
                notes.Add("Ctrl+Alt+Del will be blocked after a restart.");
            }
            else if (!o.BlockCtrlAltDel && ours)
            {
                kl.DeleteValue("Scancode Map");
                restart = true;
                notes.Add("The Delete keys work again after a restart.");
            }
        }

        return new OptionsResult(restart, notes);
    }

    static int NetUser(string user, string password, out string output)
    {
        var psi = new ProcessStartInfo("net.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("user");
        psi.ArgumentList.Add(user);
        psi.ArgumentList.Add(password);   // an empty string is passed as "" = blank password
        using var p = Process.Start(psi)!;
        output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
        p.WaitForExit();
        return p.ExitCode;
    }

}
