using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// Removes the kiosk from this PC. The agent cannot delete its own running files, so it writes a clean-up script and
/// starts it as a one-time scheduled task running as SYSTEM, independent of the agent. The script logs to
/// C:\Windows\Temp\KioskUninstall.log.
/// </summary>
static class Uninstaller
{
    public static void Start(bool removeAccount)
    {
        var user = File.Exists(Paths.KioskUserFile) ? File.ReadAllText(Paths.KioskUserFile).Trim() : "";
        if (!Regex.IsMatch(user, @"^[A-Za-z0-9 ._-]{1,20}$"))
            throw new InvalidOperationException("No valid kiosk account is registered on this PC");

        ChromePolicy.Remove();

        var script = Script
            .Replace("@@USER@@", Q(user))
            .Replace("@@REMOVE@@", removeAccount ? "$true" : "$false")
            .Replace("@@ROOT@@", Q(Updater.InstallRoot))
            .Replace("@@DATA@@", Q(Paths.DataDir));
        var temp = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Temp");
        var file = Path.Combine(temp, "KioskUninstall-" + Guid.NewGuid().ToString("N")[..8] + ".ps1");
        File.WriteAllText(file, script, new UTF8Encoding(true));

        Run("schtasks.exe", "/create", "/tn", "KioskUninstall", "/sc", "once", "/st", "23:59", "/ru", "SYSTEM",
            "/rl", "HIGHEST", "/f", "/tr", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File " + file);
        Run("schtasks.exe", "/run", "/tn", "KioskUninstall");
    }

    static string Q(string s) => s.Replace("'", "''");   // for a PowerShell single-quoted string

    static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
        p.WaitForExit(30000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} failed: {output}");
    }

    const string Script = """
$ErrorActionPreference = 'Continue'
Start-Transcript -Path "$env:SystemRoot\Temp\KioskUninstall.log" -Force | Out-Null
$user = '@@USER@@'
$removeAccount = @@REMOVE@@
$root = '@@ROOT@@'
$data = '@@DATA@@'
Write-Host "Kiosk uninstall started $(Get-Date)"
Start-Sleep -Seconds 5      # let the agent finish replying to the admin app

# 1. the agent service
Stop-Service KioskAgent -Force -ErrorAction SilentlyContinue
sc.exe delete KioskAgent | Out-Null

# 2. the kiosk programs (the launcher is the Kiosk account's shell, so this logs that account off)
1..3 | ForEach-Object {
    Get-Process KioskLauncher, KioskAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# log the Kiosk account off (without its shell its session would just be a blank screen with a cursor)
try {
    quser 2>$null | Select-Object -Skip 1 | ForEach-Object {
        $line = ($_ -replace '^>', ' ').Trim()
        if ($line -match ('^' + [regex]::Escape($user) + '\s+(?:\S+\s+)?(\d+)\s')) { logoff $Matches[1] }
    }
} catch { }

# 3. automatic login and PC-wide settings
$wl = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
Set-ItemProperty $wl AutoAdminLogon '0'
Remove-ItemProperty $wl DefaultPassword -ErrorAction SilentlyContinue
if ((Get-ItemProperty $wl).DefaultUserName -eq $user) { Remove-ItemProperty $wl DefaultUserName -ErrorAction SilentlyContinue }
Set-ItemProperty $wl Shell 'explorer.exe'
Remove-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' HideFastUserSwitching -ErrorAction SilentlyContinue

# the Delete-key block (only if it is the one this program made); needs a restart
$kl = 'HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout'
$cur = (Get-ItemProperty $kl -ErrorAction SilentlyContinue).'Scancode Map'
$ours = [byte[]](0,0,0,0, 0,0,0,0, 3,0,0,0, 0,0,0x53,0xE0, 0,0,0x53,0x00, 0,0,0,0)
if ($cur -and (($cur -join ',') -eq ($ours -join ','))) {
    Remove-ItemProperty $kl 'Scancode Map' -ErrorAction SilentlyContinue
    Write-Host 'Delete keys work again after a restart'
}

# 4. the Kiosk Windows account
$lu = Get-LocalUser -Name $user -ErrorAction SilentlyContinue
if ($lu) {
    $sid = $lu.SID.Value
    $prof = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
    for ($i = 0; $i -lt 30 -and (Test-Path "Registry::HKEY_USERS\$sid"); $i++) { Start-Sleep -Seconds 1 }   # wait until it is logged off

    if ($removeAccount) {
        $isAdmin = Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction SilentlyContinue | Where-Object { $_.SID.Value -eq $sid }
        if ($lu.FullName -ne 'Kiosk') { Write-Host "Account NOT removed: '$user' was not created by the kiosk installer" }
        elseif ($isAdmin) { Write-Host "Account NOT removed: '$user' is an administrator" }
        else {
            if ($prof) { $prof | Remove-CimInstance -ErrorAction SilentlyContinue }
            Remove-LocalUser -Name $user -ErrorAction SilentlyContinue
            Write-Host "Account '$user' removed"
        }
    }

    # account still there: undo what the kiosk wrote into it, or it would log in to an empty screen
    if (Get-LocalUser -Name $user -ErrorAction SilentlyContinue) {
        $path = $prof.LocalPath
        if ($path) {
            Remove-Item (Join-Path $path 'Desktop\Return to Kiosk.lnk') -Force -ErrorAction SilentlyContinue
            $loaded = Test-Path "Registry::HKEY_USERS\$sid"
            $hive = "Registry::HKEY_USERS\$sid"
            if (-not $loaded) {
                reg.exe load HKU\KioskCleanup "$path\NTUSER.DAT" | Out-Null
                $hive = 'Registry::HKEY_USERS\KioskCleanup'
            }
            Remove-ItemProperty "$hive\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" Shell -ErrorAction SilentlyContinue
            foreach ($n in 'DisableTaskMgr', 'DisableLockWorkstation', 'DisableChangePassword') {
                Remove-ItemProperty "$hive\Software\Microsoft\Windows\CurrentVersion\Policies\System" $n -ErrorAction SilentlyContinue
            }
            Remove-ItemProperty "$hive\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" NoLogoff -ErrorAction SilentlyContinue
            if (-not $loaded) { [gc]::Collect(); Start-Sleep -Seconds 1; reg.exe unload HKU\KioskCleanup | Out-Null }
            Write-Host "Kiosk settings removed from the '$user' account"
        }
    }
}

# 5. firewall rule, Defender exclusions, files
Get-NetFirewallRule -DisplayName 'Kiosk Agent' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
foreach ($d in @($root, $data)) {
    for ($i = 0; $i -lt 10 -and (Test-Path $d); $i++) {
        Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $d) { Start-Sleep -Seconds 2 }
    }
}
Remove-Item "$env:ProgramData\KioskInstall.log" -Force -ErrorAction SilentlyContinue
Remove-MpPreference -ExclusionPath $root, $data -ErrorAction SilentlyContinue

Write-Host "Kiosk uninstall finished $(Get-Date)"
Stop-Transcript | Out-Null
shutdown.exe /r /t 10 /f /c "Kiosk software removed - restarting"
schtasks.exe /delete /tn KioskUninstall /f | Out-Null
Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue
""";
}
