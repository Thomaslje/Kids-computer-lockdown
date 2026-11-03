# Removes the kiosk from this PC and restores the normal Windows desktop. Run as Administrator.
param([switch]$RemoveKioskUser)

# A 32-bit PowerShell on 64-bit Windows lacks the account/firewall cmdlets and redirects the registry: switch to the real 64-bit one.
if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($k in $PSBoundParameters.Keys) {
        $v = $PSBoundParameters[$k]
        if ($v -is [switch]) { if ($v) { $a += "-$k" } } else { $a += "-$k"; $a += [string]$v }
    }
    & "$env:SystemRoot\Sysnative\WindowsPowerShell\v1.0\powershell.exe" @a
    exit $LASTEXITCODE
}
$ErrorActionPreference = "Continue"
Stop-Service KioskAgent -Force -ErrorAction SilentlyContinue
sc.exe delete KioskAgent | Out-Null
Get-NetFirewallRule -DisplayName "Kiosk Agent" -ErrorAction SilentlyContinue | Remove-NetFirewallRule

$wl = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty $wl Shell "explorer.exe"
Set-ItemProperty $wl AutoAdminLogon "0"
Remove-ItemProperty $wl DefaultPassword -ErrorAction SilentlyContinue

Remove-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" HideFastUserSwitching -ErrorAction SilentlyContinue
Remove-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout" "Scancode Map" -ErrorAction SilentlyContinue   # re-enables the Delete keys after a restart
Remove-Item "HKLM:\SOFTWARE\Policies\Google\Chrome" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\KioskSuite" -Recurse -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item "$env:ProgramFiles\KioskSuite" -Recurse -Force -ErrorAction SilentlyContinue
if ($RemoveKioskUser) { Remove-LocalUser -Name "Kiosk" -ErrorAction SilentlyContinue; Write-Host "Kiosk account removed." }
else { Write-Host "The 'Kiosk' account was left in place and would show a blank screen now. Re-run with -RemoveKioskUser to delete it." }
Write-Host "Kiosk removed."
