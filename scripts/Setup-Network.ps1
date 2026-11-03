<#
  Run as Administrator. Sets the network to Private and turns on network discovery and file/printer sharing,
  otherwise the PCs cannot see each other and the admin app cannot connect.
#>
# Makes this PC reachable by the Kiosk Admin app: Private network profile, network discovery and file/printer sharing on.
$ErrorActionPreference = 'Continue'
try { Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' } | Set-NetConnectionProfile -NetworkCategory Private } catch { }
foreach ($g in '@FirewallAPI.dll,-32752', '@FirewallAPI.dll,-28502', '@FirewallAPI.dll,-28752') {   # Network Discovery, File and Printer Sharing, Network Discovery (WSD)
    try { Get-NetFirewallRule -Group $g -ErrorAction Stop | Enable-NetFirewallRule } catch { }
}
foreach ($s in 'fdPHost', 'FDResPub', 'SSDPSRV', 'upnphost', 'LanmanServer') {
    try { Set-Service $s -StartupType Automatic -ErrorAction Stop; if ((Get-Service $s).Status -ne 'Running') { Start-Service $s } } catch { }
}
try { $wk = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation'; if (-not (Test-Path $wk)) { New-Item $wk -Force | Out-Null }; Set-ItemProperty $wk AllowInsecureGuestAuth 1 -Type DWord } catch { }
