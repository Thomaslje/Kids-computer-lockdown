using System.Diagnostics;

namespace KioskAgent;

/// <summary>Keeps the PC reachable: Private network profile, network discovery and file/printer sharing on.
/// Runs at start-up and every 10 minutes, so a newly joined Wi-Fi/network is fixed too.</summary>
static class NetworkSetup
{
    const string Script = """
$ErrorActionPreference = 'Continue'
try { Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' } | Set-NetConnectionProfile -NetworkCategory Private } catch { }
foreach ($g in '@FirewallAPI.dll,-32752', '@FirewallAPI.dll,-28502', '@FirewallAPI.dll,-28752') {   # Network Discovery, File and Printer Sharing, Network Discovery (WSD)
    try { Get-NetFirewallRule -Group $g -ErrorAction Stop | Enable-NetFirewallRule } catch { }
}
foreach ($s in 'fdPHost', 'FDResPub', 'SSDPSRV', 'upnphost', 'LanmanServer') {
    try { Set-Service $s -StartupType Automatic -ErrorAction Stop; if ((Get-Service $s).Status -ne 'Running') { Start-Service $s } } catch { }
}
try { $wk = 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation'; if (-not (Test-Path $wk)) { New-Item $wk -Force | Out-Null }; Set-ItemProperty $wk AllowInsecureGuestAuth 1 -Type DWord } catch { }
""";

    public static void Ensure()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(Script)));
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
        }
        catch { }
    }

    public static void StartLoop() => _ = Task.Run(async () =>
    {
        while (true)
        {
            Ensure();
            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    });
}
