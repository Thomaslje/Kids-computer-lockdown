using System.Diagnostics;
using System.Text.RegularExpressions;
using KioskShared;
using Microsoft.Win32;

namespace KioskAgent;

/// <summary>Applies the tweaks the admin app sends (power settings and similar). Each one reports what it did.</summary>
static class TweakApplier
{
    const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static OptionsResult Apply(TweaksRequest req)
    {
        var notes = new List<string>();
        bool restart = false;
        bool Has(string id) => req.Ids.Contains(id);

        void Step(string name, Func<string?> act)
        {
            try { var n = act(); if (!string.IsNullOrEmpty(n)) notes.Add(n); }
            catch (Exception ex) { notes.Add($"{name} failed: {ex.Message}"); }
        }

        // the power plan first, because the timeouts below are written to the active plan
        if (Has("power-high")) Step("High performance plan", () =>
        {
            var list = Exe("powercfg", "/list");
            if (!list.Contains(HighPerf, StringComparison.OrdinalIgnoreCase))
            {
                var dup = Exe("powercfg", "/duplicatescheme", HighPerf);
                var m = Regex.Match(dup, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
                if (!m.Success) return "High performance plan is not available on this PC (kept the current plan).";
                Exe("powercfg", "/setactive", m.Value);
                return null;
            }
            Exe("powercfg", "/setactive", HighPerf);
            return null;
        });

        if (Has("screen-never")) Step("Screen timeout", () =>
        {
            Exe("powercfg", "/change", "monitor-timeout-ac", "0");
            Exe("powercfg", "/change", "monitor-timeout-dc", "0");
            return null;
        });

        if (Has("sleep-never")) Step("Sleep", () =>
        {
            foreach (var n in new[] { "standby-timeout-ac", "standby-timeout-dc", "hibernate-timeout-ac", "hibernate-timeout-dc" })
                Exe("powercfg", "/change", n, "0");
            return null;
        });

        if (Has("usb-suspend-off")) Step("USB selective suspend", () =>
        {
            SetPlanValue("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0);
            return null;
        });

        if (Has("wake-timers-off")) Step("Wake timers", () =>
        {
            SetPlanValue("238c9fa8-0aad-41ed-83f4-97be242c8f20", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0);
            return null;
        });

        if (Has("uac-default")) Step("UAC", () =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
            k.SetValue("EnableLUA", 1, RegistryValueKind.DWord);
            k.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);   // Windows default: prompt for consent for non-Windows programs
            k.SetValue("ConsentPromptBehaviorUser", 3, RegistryValueKind.DWord);    // standard users: ask for admin credentials
            k.SetValue("PromptOnSecureDesktop", 1, RegistryValueKind.DWord);
            k.SetValue("EnableInstallerDetection", 1, RegistryValueKind.DWord);
            k.DeleteValue("FilterAdministratorToken", false);
            restart = true;
            return null;
        });

        if (Has("cpu-min-100")) Step("Processor minimum", () =>
        {
            SetPlanValue("54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", 100);
            return null;
        });

        if (Has("fast-startup-off")) Step("Fast Startup", () =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", true);
            k.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
            restart = true;
            return null;
        });

        if (Has("nic-nosleep")) Step("Network card power saving", () =>
        {
            var o = Ps(@"
$n = 0
foreach ($a in Get-NetAdapter -Physical -ErrorAction SilentlyContinue) {
    $base = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}'
    foreach ($k in Get-ChildItem $base -ErrorAction SilentlyContinue) {
        if ((Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue).NetCfgInstanceId -eq $a.InterfaceGuid) {
            Set-ItemProperty $k.PSPath -Name PnPCapabilities -Value 24 -Type DWord   # 'allow the computer to turn off this device' = off
            $n++
        }
    }
    try { Set-NetAdapterPowerManagement -Name $a.Name -SelectiveSuspend Disabled -NoRestart -ErrorAction Stop } catch { }
}
$n");
            restart = true;
            return o.Trim() == "0" ? "No network card found to change." : null;
        });

        if (Has("nic-wol")) Step("Wake-on-LAN", () =>
        {
            var o = Ps(@"
$n = 0
foreach ($a in Get-NetAdapter -Physical -ErrorAction SilentlyContinue) {
    try { Set-NetAdapterPowerManagement -Name $a.Name -WakeOnMagicPacket Enabled -NoRestart -ErrorAction Stop; $n++ } catch { }
}
$n");
            return o.Trim() == "0" ? "Wake-on-LAN is not supported by the network card." : null;
        });

        notes.Insert(0, $"{req.Ids.Count} tweak(s) sent" + (notes.Count > 0 ? ", with problems:" : "."));
        return new OptionsResult(restart, notes);
    }

    // sets a value on the active plan, for mains and battery, then re-activates the plan so it takes effect
    static void SetPlanValue(string subgroup, string setting, int value)
    {
        Exe("powercfg", "/setacvalueindex", "SCHEME_CURRENT", subgroup, setting, value.ToString());
        Exe("powercfg", "/setdcvalueindex", "SCHEME_CURRENT", subgroup, setting, value.ToString());
        Exe("powercfg", "/setactive", "SCHEME_CURRENT");
    }

    static string Ps(string script) =>
        Exe("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));

    static string Exe(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60000);
        if (p.ExitCode != 0 && file == "powercfg")
            throw new InvalidOperationException(((output + err.Result).Trim()) is { Length: > 0 } t ? t : $"exit code {p.ExitCode}");
        return output;
    }
}
